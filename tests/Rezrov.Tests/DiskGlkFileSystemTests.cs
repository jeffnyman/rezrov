using Rezrov.Glulx.Glk;
using FileMode = Rezrov.Glulx.Glk.FileMode;

namespace Rezrov.Tests;

/// <summary>
/// The file system over a real directory: the Glk opening rules on
/// disk, where names land, and how the player is asked.
/// </summary>
public class DiskGlkFileSystemTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rezrov-glk-" + Guid.NewGuid().ToString("N"));

    public DiskGlkFileSystemTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string PathOf(string name) => Path.Combine(_directory, name);

    [Fact]
    public void AFrontendCanAnswerThePromptItself()
    {
        var asked = new List<(FileUsage, FileMode)>();
        var files = new DiskGlkFileSystem(_directory, (usage, mode) =>
        {
            asked.Add((usage, mode));
            return usage == FileUsage.SavedGame ? "game.glksave" : null;
        });
        files.NamedFiles[FileUsage.Transcript] = "script.txt";

        // A named file is never asked about; the rest go to the
        // frontend, which may answer with nothing.
        Assert.Equal("script.txt", files.AskForFile(FileUsage.Transcript, FileMode.Write));
        Assert.Equal("game.glksave", files.AskForFile(FileUsage.SavedGame, FileMode.Read));
        Assert.Null(files.AskForFile(FileUsage.Data, FileMode.Write));
        Assert.Equal([(FileUsage.SavedGame, FileMode.Read), (FileUsage.Data, FileMode.Write)], asked);
    }

    [Fact]
    public void OpensFilesByTheGlkRules()
    {
        var files = new DiskGlkFileSystem(_directory);

        // [glk #file_streams] Read needs the file; Write makes it and
        // empties it; WriteAppend begins at the end; ReadWrite at the
        // start, keeping what is there.
        Assert.Null(files.Open("notes.glkdata", FileMode.Read));
        Assert.False(files.Exists("notes.glkdata"));

        using (var writing = files.Open("notes.glkdata", FileMode.Write)!)
        {
            writing.Write("abc"u8);
        }

        Assert.True(files.Exists("notes.glkdata"));
        Assert.Equal("abc", File.ReadAllText(PathOf("notes.glkdata")));

        using (var appending = files.Open("notes.glkdata", FileMode.WriteAppend)!)
        {
            Assert.Equal(3, appending.Position);
            appending.WriteByte((byte)'d');
        }

        using (var both = files.Open("notes.glkdata", FileMode.ReadWrite)!)
        {
            Assert.Equal(0, both.Position);
            both.WriteByte((byte)'x');
            Assert.Equal('b', both.ReadByte());
        }

        Assert.Equal("xbcd", File.ReadAllText(PathOf("notes.glkdata")));

        using (files.Open("notes.glkdata", FileMode.Write))
        {
        }

        Assert.Equal("", File.ReadAllText(PathOf("notes.glkdata")));
        Assert.Null(files.Open("notes.glkdata", (FileMode)9));

        // [glk op:fileref_delete_file] Gone, and gone again quietly.
        files.Delete("notes.glkdata");
        Assert.False(files.Exists("notes.glkdata"));
        files.Delete("notes.glkdata");
    }

    [Fact]
    public void NamesWithoutAPathLiveInTheDirectory()
    {
        var files = new DiskGlkFileSystem(_directory);
        var elsewhere = Path.Combine(_directory, "elsewhere.glkdata");

        using (files.Open("here.glkdata", FileMode.Write))
        {
        }

        using (files.Open(elsewhere, FileMode.Write))
        {
        }

        Assert.True(File.Exists(PathOf("here.glkdata")));
        Assert.True(files.Exists(elsewhere));

        // [glk op:fileref_create_temp] Somewhere out of the way, and
        // new each time.
        var temporary = files.TemporaryName();
        Assert.True(Path.IsPathRooted(temporary));
        Assert.NotEqual(temporary, files.TemporaryName());
    }

    [Fact]
    public void AsksThePlayerUnlessAFileWasNamedUpFront()
    {
        var questions = new StringWriter();
        var files = new DiskGlkFileSystem(_directory, new StringReader("  mine \n\nother\n"), questions);

        // [glk op:fileref_create_by_prompt] The question says what kind
        // of file and which way; the answer is trimmed, and an empty
        // one declines. A name given up front is used without asking.
        Assert.Equal("mine", files.AskForFile(FileUsage.SavedGame, FileMode.Write));
        Assert.Equal("Saved game to store: ", questions.ToString());
        Assert.Null(files.AskForFile(FileUsage.Transcript, FileMode.Read));
        Assert.EndsWith("Transcript file to load: ", questions.ToString(), StringComparison.Ordinal);

        files.NamedFiles[FileUsage.SavedGame] = "named.glksave";
        Assert.Equal("named.glksave", files.AskForFile(FileUsage.SavedGame, FileMode.Read));
        Assert.Equal("other", files.AskForFile(FileUsage.Data, FileMode.ReadWrite));
        Assert.EndsWith("Data file to store: ", questions.ToString(), StringComparison.Ordinal);

        // Input over, so every prompt is declined.
        Assert.Null(files.AskForFile(FileUsage.InputRecord, FileMode.Write));
        Assert.Null(new DiskGlkFileSystem(_directory).AskForFile(FileUsage.Data, FileMode.Write));
    }
}

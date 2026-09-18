using Rezrov.Gtui;
using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

/// <summary>
/// [zm 7.6] Asking the player for a file name in the game's own window.
/// </summary>
/// <remarks>
/// The grid frontend has no file dialog to open, so it prints the
/// question into the screen and reads the answer from the same key
/// queue the game reads from. That is what makes this testable at all:
/// the keys go in, and the screen and the file system are read
/// afterwards.
/// </remarks>
public class GridFilesTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("rezrov-gridfiles").FullName;

    private readonly BufferedScreen _screen;
    private readonly BufferedInput _input;
    private readonly GridFiles _files;

    public GridFilesTests()
    {
        _screen = new BufferedScreen(
            40,
            6,
            cursorStartsAtBottom: false,
            repaint: () => { },
            waitForKey: () => Zscii.Newline,
            fontWidth: Paint.CellWidth,
            fontHeight: Paint.CellHeight,
            capabilities: ScreenCapabilities.StatusLine | ScreenCapabilities.UpperWindow);

        _input = new BufferedInput(_screen);
        _files = new GridFiles(_screen, _input, _directory, "zork1");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Directory.Delete(_directory, recursive: true);
    }

    private void Type(string text)
    {
        foreach (var character in text)
        {
            _input.Enqueue(character);
        }

        _input.Enqueue(Zscii.Newline);
    }

    private string Screenful() =>
        string.Join("\n", Enumerable.Range(0, _screen.Height).Select(row => _screen.Buffer.RowText(row).TrimEnd()));

    [Fact]
    public void TheQuestionIsAskedInTheWindowWithTheStorysNameOffered()
    {
        // Pressing return alone takes the name offered, which is the
        // story's own, so saving is one keystroke.
        _input.Enqueue(Zscii.Newline);

        using var save = _files.OpenSaveFile();

        Assert.NotNull(save);
        Assert.Contains("Save the game as: zork1.sav", Screenful(), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_directory, "zork1.sav")));
    }

    [Fact]
    public void ANameTypedInFullIsTheOneUsed()
    {
        // The offered name is already in hand, so it is taken back out
        // a character at a time before the new one is typed.
        for (var i = 0; i < "zork1.sav".Length; i++)
        {
            _input.Enqueue(Zscii.Delete);
        }

        Type("cellar.sav");

        using var save = _files.OpenSaveFile();

        Assert.NotNull(save);
        Assert.True(File.Exists(Path.Combine(_directory, "cellar.sav")));
        Assert.False(File.Exists(Path.Combine(_directory, "zork1.sav")));
    }

    [Fact]
    public void EscapeDeclinesAndTheSaveFails()
    {
        // [zm op:save] A player who changes their mind makes the save
        // fail, which the game is told about in the ordinary way.
        _input.Enqueue(Zscii.Escape);

        Assert.Null(_files.OpenSaveFile());
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void ANameRubbedOutEntirelyIsNoNameAtAll()
    {
        for (var i = 0; i < "zork1.sav".Length + 5; i++)
        {
            _input.Enqueue(Zscii.Delete);
        }

        _input.Enqueue(Zscii.Newline);

        Assert.Null(_files.OpenSaveFile());
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void RestoringAGameThatIsNotThereFailsRatherThanThrowing()
    {
        for (var i = 0; i < "zork1.sav".Length; i++)
        {
            _input.Enqueue(Zscii.Delete);
        }

        Type("nothing-here.sav");

        Assert.Null(_files.OpenRestoreFile());
    }

    [Fact]
    public void AGameSavedIsAGameThatCanBeRestored()
    {
        _input.Enqueue(Zscii.Newline);

        using (var save = _files.OpenSaveFile())
        {
            Assert.NotNull(save);
            save.Write([1, 2, 3], 0, 3);
        }

        _input.Enqueue(Zscii.Newline);

        using var restored = _files.OpenRestoreFile();

        Assert.NotNull(restored);

        var read = new byte[3];
        Assert.Equal(3, restored.Read(read, 0, 3));
        Assert.Equal([1, 2, 3], read);
    }

    [Fact]
    public void EverythingLandsBesideTheStoryUnlessAPathIsGiven()
    {
        // A player who types a path of their own gets it; anything else
        // belongs beside the game it came from.
        for (var i = 0; i < "zork1.sav".Length; i++)
        {
            _input.Enqueue(Zscii.Delete);
        }

        var elsewhere = Path.Combine(Path.GetTempPath(), $"rezrov-elsewhere-{Guid.NewGuid():N}.sav");
        Type(elsewhere);

        using (var save = _files.OpenSaveFile())
        {
            Assert.NotNull(save);
        }

        Assert.True(File.Exists(elsewhere));
        Assert.Empty(Directory.GetFiles(_directory));

        File.Delete(elsewhere);
    }
}

using System.Text;
using Rezrov.Glulx;
using Rezrov.Glulx.Execution;
using Rezrov.Glulx.Glk;
using Rezrov.Glulx.Instructions;
using static Rezrov.Tests.GlulxAssembler;
using FileMode = Rezrov.Glulx.Glk.FileMode;

namespace Rezrov.Tests;

/// <summary>
/// [glk #fileref] File references and [glk #file_streams] file streams
/// over the in-memory file system: naming, prompting, the four ways of
/// opening, the four encodings, positions, and a saved game going to a
/// file and coming back through the glk opcode.
/// </summary>
public class GlkFileTests
{
    private const uint SavedGame = (uint)FileUsage.SavedGame;
    private const uint Data = (uint)FileUsage.Data;
    private const uint Transcript = (uint)FileUsage.Transcript;
    private const uint Text = GlkFileReference.TextModeUsage;

    private const uint FilerefCreateByName = 0x0061;
    private const uint StreamOpenFile = 0x0042;
    private const uint StreamClose = 0x0044;

    private static (GlkLibrary Glk, MemoryGlkFileSystem Files) Library()
    {
        var files = new MemoryGlkFileSystem();
        return (new GlkLibrary(new RecordingGlkDisplay(), files), files);
    }

    private static string Contents(MemoryGlkFileSystem files, string name) => Encoding.Latin1.GetString(files.Files[name]);

    private static void Put(GlkLibrary glk, GlkStream stream, string text)
    {
        foreach (var character in text)
        {
            glk.PutChar(stream, character);
        }
    }

    private static string ReadAll(GlkLibrary glk, GlkStream stream)
    {
        var text = new StringBuilder();
        for (var character = glk.GetChar(stream); character >= 0; character = glk.GetChar(stream))
        {
            text.Append(char.ConvertFromUtf32(character));
        }

        return text.ToString();
    }

    [Theory]
    [InlineData("advent", SavedGame, "advent.glksave")]
    [InlineData("advent", Transcript, "advent.txt")]
    [InlineData("advent", (uint)FileUsage.InputRecord, "advent.txt")]
    [InlineData("notes", Data, "notes.glkdata")]
    [InlineData("notes", Data | Text, "notes.glkdata")]
    [InlineData("a/b\\c:d\"e|f?g*h<i>j", Data, "abcdefghij.glkdata")]
    [InlineData("notes.old.txt", Data, "notes.glkdata")]
    [InlineData("", Data, "null.glkdata")]
    [InlineData(".hidden", Data, "null.glkdata")]
    public void NamesFollowTheRecommendedRules(string name, uint usage, string expected)
    {
        // [glk op:fileref_create_by_name] Illegal characters deleted,
        // cut at the first period, "null" for nothing, and the suffix
        // for the usage.
        Assert.Equal(expected, GlkFileReference.SafeName(name, usage));
    }

    [Theory]
    [InlineData("mygame", SavedGame, "mygame.glksave")]
    [InlineData("mygame.sav", SavedGame, "mygame.sav")]
    [InlineData("saves.v2/mygame", SavedGame, "saves.v2/mygame.glksave")]
    [InlineData("C:\\saves\\mygame.sav", SavedGame, "C:\\saves\\mygame.sav")]
    [InlineData("script", Transcript | Text, "script.txt")]
    public void AChosenNameGetsASuffixOnlyWithoutOne(string chosen, uint usage, string expected)
    {
        // [glk op:fileref_create_by_prompt] The suffix is added when the
        // file name has no extension, and a directory's dot does not
        // count.
        Assert.Equal(expected, GlkFileReference.WithSuffix(chosen, usage));
    }

    [Fact]
    public void AFileWrittenCanBeReadBack()
    {
        var (glk, files) = Library();
        var fileref = glk.CreateFileReference(Data | Text, "notes", 7);

        Assert.Equal("notes.glkdata", fileref.Name);
        Assert.Equal(FileUsage.Data, fileref.Type);
        Assert.True(fileref.IsText);
        Assert.Equal(7u, fileref.Rock);
        Assert.False(glk.FileExists(fileref));

        var writing = glk.OpenFileStream(fileref, false, FileMode.Write, 1)!;
        Put(glk, writing, "hello\n");
        Assert.True(writing.Writable);
        Assert.False(writing.Readable);
        Assert.Equal(6u, writing.Position);
        Assert.Equal((0u, 6u), glk.CloseStream(writing));

        // [glk #file_streams] The file holds the bytes, exists now, and
        // reads back to its end.
        Assert.Equal("hello\n", Contents(files, "notes.glkdata"));
        Assert.True(glk.FileExists(fileref));

        var reading = glk.OpenFileStream(fileref, false, FileMode.Read, 2)!;
        Assert.Equal("hello\n", ReadAll(glk, reading));
        Assert.Equal(-1, glk.GetChar(reading));
        Assert.Equal((6u, 0u), glk.CloseStream(reading));
        Assert.Equal(0, glk.Streams.Count);
        Assert.Empty(glk.Warnings);
    }

    [Fact]
    public void ReadingAMissingFileFailsQuietly()
    {
        var (glk, _) = Library();
        var fileref = glk.CreateFileReference(Data, "missing", 0);

        // [glk #file_streams] Read needs the file to exist, and the
        // answer is null with no fuss; the other modes make it.
        Assert.Null(glk.OpenFileStream(fileref, false, FileMode.Read, 0));
        Assert.Empty(glk.Warnings);
        Assert.NotNull(glk.OpenFileStream(fileref, false, FileMode.ReadWrite, 0));
        Assert.True(glk.FileExists(fileref));

        Assert.Null(glk.OpenFileStream(fileref, false, (FileMode)9, 0));
        Assert.Null(glk.OpenFileStream(null, false, FileMode.Read, 0));
        Assert.Single(glk.Warnings);
    }

    [Fact]
    public void ModesEmptyAppendOrKeepTheFile()
    {
        var (glk, files) = Library();
        var fileref = glk.CreateFileReference(Data, "notes", 0);
        files.Put("notes.glkdata", "abc"u8.ToArray());

        // [glk #file_streams] Write truncates, WriteAppend starts at the
        // end, and ReadWrite starts at the beginning without truncating.
        var appending = glk.OpenFileStream(fileref, false, FileMode.WriteAppend, 0)!;
        Assert.Equal(3u, appending.Position);
        Put(glk, appending, "x");
        glk.CloseStream(appending);
        Assert.Equal("abcx", Contents(files, "notes.glkdata"));

        var both = glk.OpenFileStream(fileref, false, FileMode.ReadWrite, 0)!;
        Assert.Equal(0u, both.Position);
        Put(glk, both, "y");
        Assert.Equal('b', glk.GetChar(both));
        glk.CloseStream(both);
        Assert.Equal("ybcx", Contents(files, "notes.glkdata"));

        var writing = glk.OpenFileStream(fileref, false, FileMode.Write, 0)!;
        Put(glk, writing, "z");
        glk.CloseStream(writing);
        Assert.Equal("z", Contents(files, "notes.glkdata"));
    }

    [Fact]
    public void DeletingAndTemporaryFiles()
    {
        var (glk, files) = Library();
        var fileref = glk.CreateFileReference(Data, "notes", 0);
        files.Put("notes.glkdata", [1]);

        // [glk op:fileref_delete_file] The file goes; the reference
        // stays.
        glk.DeleteFile(fileref);
        Assert.False(glk.FileExists(fileref));
        Assert.Equal(1, glk.FileReferences.Count);

        // [glk op:fileref_create_temp] Always a new file.
        var first = glk.CreateTemporaryFileReference(Data, 0);
        var second = glk.CreateTemporaryFileReference(Data, 0);
        Assert.NotEqual(first.Name, second.Name);
        Assert.False(glk.FileExists(first));
        Assert.NotNull(glk.OpenFileStream(first, false, FileMode.Write, 0));
        Assert.True(glk.FileExists(first));

        // Nothing to do for no reference.
        glk.DeleteFile(null);
        Assert.False(glk.FileExists(null));
        Assert.Null(glk.CopyFileReference(Data, null, 0));
    }

    [Fact]
    public void PromptsAskTheFileSystem()
    {
        var (glk, files) = Library();
        files.Answers.Enqueue("mygame");
        files.Answers.Enqueue(null);
        files.Answers.Enqueue("nothing");
        files.Answers.Enqueue("mygame");

        // [glk op:fileref_create_by_prompt] The usage and mode reach the
        // player, the name comes back with a suffix, a declined prompt
        // is null, and for reading so is a file that does not exist.
        var chosen = glk.PromptForFileReference(SavedGame, FileMode.Write, 3)!;
        Assert.Equal("mygame.glksave", chosen.Name);
        Assert.Equal(3u, chosen.Rock);
        Assert.Equal((FileUsage.SavedGame, FileMode.Write), files.Prompts[0]);

        Assert.Null(glk.PromptForFileReference(SavedGame, FileMode.Write, 0));
        Assert.Null(glk.PromptForFileReference(SavedGame, FileMode.Read, 0));

        files.Put("mygame.glksave", [1]);
        Assert.NotNull(glk.PromptForFileReference(SavedGame, FileMode.Read, 0));
        Assert.Equal(4, files.Prompts.Count);

        Assert.Null(glk.PromptForFileReference(SavedGame, (FileMode)9, 0));
        Assert.Single(glk.Warnings);
        Assert.Equal(4, files.Prompts.Count);
    }

    [Fact]
    public void CopyingAReferenceKeepsTheNameAndChangesTheUsage()
    {
        var (glk, _) = Library();
        var binary = glk.CreateFileReference(Data, "notes", 1);
        var text = glk.CopyFileReference(Data | Text, binary, 2)!;

        // [glk op:fileref_create_from_fileref] The same file, now text,
        // with its own rock; the original is unchanged.
        Assert.Equal(binary.Name, text.Name);
        Assert.True(text.IsText);
        Assert.False(binary.IsText);
        Assert.Equal(2u, text.Rock);
        Assert.Equal(2, glk.FileReferences.Count);
    }

    [Fact]
    public void UnicodeFilesAreBigEndianWordsOrUtf8()
    {
        var (glk, files) = Library();
        var binary = glk.CreateFileReference(Data, "binary", 0);
        var text = glk.CreateFileReference(Data | Text, "text", 0);
        var bytes = glk.CreateFileReference(Data, "bytes", 0);

        // [glk #file_streams] A Unicode stream in binary form is four
        // bytes a character, in text form UTF-8 without a byte order
        // mark, and a byte stream cannot hold the character at all.
        var writingBinary = glk.OpenFileStream(binary, true, FileMode.Write, 0)!;
        glk.PutChar(writingBinary, 0x3B1);
        glk.PutChar(writingBinary, 'a');
        Assert.Equal(2u, writingBinary.Position);
        glk.CloseStream(writingBinary);
        Assert.Equal(new byte[] { 0, 0, 0x03, 0xB1, 0, 0, 0, 0x61 }, files.Files["binary.glkdata"]);

        var writingText = glk.OpenFileStream(text, true, FileMode.Write, 0)!;
        glk.PutChar(writingText, 0x3B1);
        glk.PutChar(writingText, 'a');
        glk.PutChar(writingText, 0x1F600);
        glk.PutChar(writingText, 0xD800);
        glk.CloseStream(writingText);
        Assert.Equal(new byte[] { 0xCE, 0xB1, 0x61, 0xF0, 0x9F, 0x98, 0x80, (byte)'?' }, files.Files["text.glkdata"]);

        var writingBytes = glk.OpenFileStream(bytes, false, FileMode.Write, 0)!;
        glk.PutChar(writingBytes, 0x3B1);
        glk.PutChar(writingBytes, 0xE9);
        glk.CloseStream(writingBytes);
        Assert.Equal(new byte[] { (byte)'?', 0xE9 }, files.Files["bytes.glkdata"]);

        // And each reads back as it was written.
        Assert.Equal("\u03B1a", ReadAll(glk, glk.OpenFileStream(binary, true, FileMode.Read, 0)!));
        Assert.Equal("\u03B1a\U0001F600?", ReadAll(glk, glk.OpenFileStream(text, true, FileMode.Read, 0)!));
        Assert.Equal("?\u00E9", ReadAll(glk, glk.OpenFileStream(bytes, false, FileMode.Read, 0)!));

        // [glk #file_streams] Bytes that are not UTF-8 end the stream.
        files.Put("bad.glkdata", [0x61, 0xFF, 0x62]);
        Assert.Equal("a", ReadAll(glk, glk.OpenFileStream(glk.CreateFileReference(Data | Text, "bad", 0), true, FileMode.Read, 0)!));
        files.Put("cut.glkdata", [0x61, 0xCE]);
        Assert.Equal("a", ReadAll(glk, glk.OpenFileStream(glk.CreateFileReference(Data | Text, "cut", 0), true, FileMode.Read, 0)!));
    }

    [Fact]
    public void PositionsCountCharacters()
    {
        var (glk, files) = Library();
        files.Put("bytes.glkdata", "abc"u8.ToArray());
        files.Put("words.glkdata", [0, 0, 0, 1, 0, 0, 0, 2]);
        var bytes = glk.OpenFileStream(glk.CreateFileReference(Data, "bytes", 0), false, FileMode.Read, 0)!;
        var words = glk.OpenFileStream(glk.CreateFileReference(Data, "words", 0), true, FileMode.Read, 0)!;

        // [glk #stream_positions] Bytes in a byte stream, words in a
        // binary Unicode stream, from the start, the mark, or the end,
        // and never outside the file.
        bytes.SetPosition(1, SeekMode.Start);
        Assert.Equal('b', glk.GetChar(bytes));
        bytes.SetPosition(-1, SeekMode.End);
        Assert.Equal(2u, bytes.Position);
        Assert.Equal('c', glk.GetChar(bytes));
        bytes.SetPosition(-2, SeekMode.Current);
        Assert.Equal('b', glk.GetChar(bytes));
        bytes.SetPosition(-9, SeekMode.Current);
        Assert.Equal(0u, bytes.Position);
        bytes.SetPosition(9, SeekMode.Start);
        Assert.Equal(3u, bytes.Position);

        words.SetPosition(1, SeekMode.Start);
        Assert.Equal(2, glk.GetChar(words));
        Assert.Equal(2u, words.Position);
        words.SetPosition(-2, SeekMode.End);
        Assert.Equal(1, glk.GetChar(words));
    }

    [Fact]
    public void CloseFilesFlushesEverythingStillOpen()
    {
        var (glk, files) = Library();
        var one = glk.OpenFileStream(glk.CreateFileReference(Data, "one", 0), false, FileMode.Write, 0)!;
        var two = glk.OpenFileStream(glk.CreateFileReference(Data, "two", 0), false, FileMode.Write, 0)!;
        Put(glk, one, "1");
        Put(glk, two, "2");
        Assert.Empty(files.Files["one.glkdata"]);

        glk.CloseFiles();

        Assert.Equal("1", Contents(files, "one.glkdata"));
        Assert.Equal("2", Contents(files, "two.glkdata"));
        Assert.Equal(0, glk.Streams.Count);
    }

    [Fact]
    public void ASavedGameGoesToAFileAndComesBack()
    {
        // [glulx op:save] The whole road: a reference by name, a file
        // stream on it, the save, the stream closed, the file opened
        // again for reading, and the restore coming back to the save
        // with -1.
        var code = new GlulxAssembler().Function("main").Op(Opcode.SetIOSys, C(2), C(0));
        Glk(code, FilerefCreateByName, Ram(0), C(SavedGame), At("name"), C(0));
        Glk(code, StreamOpenFile, Ram(4), Ram(0), C((uint)FileMode.Write), C(0));
        code.Op(Opcode.Copy, C(1), Ram(28))
            .Op(Opcode.Save, Ram(4), Ram(8))
            .Op(Opcode.Jne, Ram(8), C(-1), To("saved"))
            .Op(Opcode.Copy, C(99), Ram(12))
            .Return(C(0))
            .Label("saved")
            .Op(Opcode.Copy, C(2), Ram(28));
        Glk(code, StreamClose, Discard, Ram(4), C(0));
        Glk(code, StreamOpenFile, Ram(16), Ram(0), C((uint)FileMode.Read), C(0));
        code.Op(Opcode.Restore, Ram(16), Ram(20))
            .Op(Opcode.Copy, C(7), Ram(24))
            .Return(C(0))
            .CString("name", "game");

        var (glk, files) = Library();
        var machine = GlulxRun.Run(code, glk: glk);

        Assert.Equal(0xFFFFFFFFu, machine.Ram(8));
        Assert.Equal(99u, machine.Ram(12));
        Assert.Equal(0u, machine.Ram(24));
        Assert.Equal(1u, machine.Ram(28));
        Assert.Equal("FORM", Contents(files, "game.glksave")[..4]);
        Assert.Empty(glk.Warnings);
    }

    /// <summary>
    /// [glulx op:glk] Pushes the arguments last first and calls the
    /// function, storing its result.
    /// </summary>
    private static GlulxAssembler Glk(GlulxAssembler code, uint selector, Arg result, params Arg[] args)
    {
        for (var i = args.Length - 1; i >= 0; i--)
        {
            code.Op(Opcode.Copy, args[i], Sp);
        }

        return code.Op(Opcode.Glk, C(selector), C(args.Length), result);
    }
}

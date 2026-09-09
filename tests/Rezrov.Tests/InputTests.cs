using Rezrov.ZMachine;
using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

/// <summary>
/// The pieces of section 10 that stand on their own: which keys end a
/// command, how a file of commands is read back, and how typed
/// characters become ZSCII.
/// </summary>
public class InputTests
{
    private const int Table = 0x300;

    [Fact]
    public void OnlyNewlineEndsACommandBeforeVersion5()
    {
        // [zm 10.5.2] and [zm 10.5.2.1] The table exists from Version 5.
        var (memory, header) = Story(ZMachineVersion.V4, 129, 0);

        var terminators = TerminatingCharacters.Read(memory, header);

        Assert.Same(TerminatingCharacters.NewlineOnly, terminators);
        Assert.True(terminators.IsTerminator(Zscii.Newline));
        Assert.False(terminators.IsTerminator(Zscii.CursorUp));
    }

    [Fact]
    public void TheHeaderTableNamesFunctionKeysThatEndACommand()
    {
        // [zm 10.5.2.1] Cursor up and keypad 9 end a command; 65 is not
        // a function key and is not permitted, so it is ignored.
        var (memory, header) = Story(ZMachineVersion.V5, 129, 65, 154, 0);

        var terminators = TerminatingCharacters.Read(memory, header);

        Assert.True(terminators.IsTerminator(Zscii.Newline));
        Assert.True(terminators.IsTerminator(Zscii.CursorUp));
        Assert.True(terminators.IsTerminator(Zscii.Keypad9));
        Assert.False(terminators.IsTerminator(Zscii.CursorDown));
        Assert.False(terminators.IsTerminator(65));
        Assert.False(terminators.AnyFunctionKey);
        Assert.Equal([129, 154], terminators.Codes.Order().ToArray());
    }

    [Fact]
    public void TwoHundredFiftyFiveMeansAnyFunctionKey()
    {
        // [zm 10.5.2.1]
        var (memory, header) = Story(ZMachineVersion.V5, 255, 0);

        var terminators = TerminatingCharacters.Read(memory, header);

        Assert.True(terminators.AnyFunctionKey);
        Assert.True(terminators.IsTerminator(Zscii.F1));
        Assert.True(terminators.IsTerminator(Zscii.SingleClick));
        Assert.False(terminators.IsTerminator('a'));
    }

    [Fact]
    public void ANullTableAddressMeansNewlineOnly()
    {
        var (memory, header) = Story(ZMachineVersion.V5, 129, 0);
        memory.WriteWord(0x2E, 0);

        Assert.Same(TerminatingCharacters.NewlineOnly, TerminatingCharacters.Read(memory, header));
    }

    [Fact]
    public void CommandFileReadsPlainCommands()
    {
        var file = new CommandFile(new StringReader("take lamp\nlook\n"), UnicodeTranslationTable.Default);

        var first = file.ReadLine(Request());
        var second = file.ReadLine(Request());

        Assert.Equal("take lamp", Text(first!));
        Assert.Equal(Zscii.Newline, first!.Terminator);
        Assert.Equal("look", Text(second!));
        Assert.Null(file.ReadLine(Request()));
    }

    [Fact]
    public void CommandFileReadsCodesInBrackets()
    {
        // The style the remarks on section 7 suggest: a terminating key
        // after the command, [0] for a timed-out read, and [91] for a
        // bracket typed as itself.
        var file = new CommandFile(
            new StringReader("turn it on.[154]\nlook unde[0]\n[91]x\n"),
            UnicodeTranslationTable.Default);
        var (memory, header) = Story(ZMachineVersion.V5, 255, 0);
        var request = Request(TerminatingCharacters.Read(memory, header));

        var keypad = file.ReadLine(request)!;
        var timedOut = file.ReadLine(request)!;
        var bracket = file.ReadLine(request)!;

        Assert.Equal("turn it on.", Text(keypad));
        Assert.Equal(Zscii.Keypad9, keypad.Terminator);
        Assert.Equal("look unde", Text(timedOut));
        Assert.Equal(0, timedOut.Terminator);
        Assert.Equal("[x", Text(bracket));
    }

    [Fact]
    public void CommandFileTakesAMalformedBracketLiterally()
    {
        var file = new CommandFile(new StringReader("a[b]c[1\n"), UnicodeTranslationTable.Default);

        Assert.Equal("a[b]c[1", Text(file.ReadLine(Request())!));
    }

    [Fact]
    public void CommandFileReadsKeysOnePerLine()
    {
        // A line with nothing on it is the return key, and a click brings
        // its coordinates along, which are consumed with it.
        var file = new CommandFile(new StringReader("y\n\n[254][10][6]\n"), UnicodeTranslationTable.Default);

        Assert.Equal('y', file.ReadKey());
        Assert.Equal(Zscii.Newline, file.ReadKey());
        Assert.Equal(Zscii.SingleClick, file.ReadKey());
        Assert.Null(file.ReadKey());
    }

    [Fact]
    public void CommandFileContinuesFromLeftoverTextAndHonorsTheMaximum()
    {
        var file = new CommandFile(new StringReader("nder the rock\n"), UnicodeTranslationTable.Default);
        var request = Request(maxLength: 12, initial: "look u");

        var line = file.ReadLine(request)!;

        Assert.Equal("look under t", Text(line));
    }

    [Fact]
    public void TextReaderInputTurnsALineIntoZscii()
    {
        // [zm 10.7] A tab has no input code and vanishes; an accent goes
        // through the translation table, where e-acute is 170.
        var (memory, header) = Story(ZMachineVersion.V5);
        var input = new TextReaderInput(new StringReader("Café\tok\n"), header, memory);

        var line = input.ReadLine(Request());

        Assert.Equal([(ushort)'C', (ushort)'a', (ushort)'f', 170, (ushort)'o', (ushort)'k'], line.Text);
        Assert.Equal(Zscii.Newline, line.Terminator);
        Assert.False(input.SupportsTimedInput);
    }

    [Fact]
    public void TextReaderInputStopsAtTheMaximumAndKeepsTheInitialText()
    {
        var (memory, header) = Story(ZMachineVersion.V5);
        var input = new TextReaderInput(new StringReader("nder the rock\n"), header, memory);

        var line = input.ReadLine(Request(maxLength: 8, initial: "look u"));

        Assert.Equal("look und", Text(line));
    }

    [Fact]
    public void TextReaderInputReadsTheFirstCharacterOfALineAsAKey()
    {
        var (memory, header) = Story(ZMachineVersion.V5);
        var input = new TextReaderInput(new StringReader("yes\n\n"), header, memory);

        Assert.Equal('y', input.ReadKey(null));
        Assert.Equal(Zscii.Newline, input.ReadKey(null));
    }

    [Fact]
    public void TextReaderInputThrowsWhenTheReaderRunsOut()
    {
        var (memory, header) = Story(ZMachineVersion.V5);
        var input = new TextReaderInput(new StringReader(""), header, memory);

        Assert.Throws<EndOfStreamException>(() => input.ReadLine(Request()));
        Assert.Throws<EndOfStreamException>(() => input.ReadKey(null));
    }

    [Fact]
    public void TypedCharactersBecomeInputCodes()
    {
        var table = UnicodeTranslationTable.Default;

        // [zm 3.8.2.5] Either line ending is 13; [zm 3.8.2.2] backspace
        // and ASCII delete are both 8; [zm 3.8.2.6] escape is 27.
        Assert.Equal(Zscii.Newline, Zscii.FromUnicode('\n', table));
        Assert.Equal(Zscii.Newline, Zscii.FromUnicode('\r', table));
        Assert.Equal(Zscii.Delete, Zscii.FromUnicode('\b', table));
        Assert.Equal(Zscii.Delete, Zscii.FromUnicode((char)0x7F, table));
        Assert.Equal(Zscii.Escape, Zscii.FromUnicode((char)0x1B, table));
        Assert.Equal((ushort)'a', Zscii.FromUnicode('a', table));
        Assert.Equal((ushort)170, Zscii.FromUnicode('é', table));
        Assert.Null(Zscii.FromUnicode('€', table));
        Assert.Null(Zscii.FromUnicode('\t', table));
    }

    [Fact]
    public void LowerCasingGoesThroughTheTranslationTable()
    {
        var table = UnicodeTranslationTable.Default;

        // [zm op:read] A-umlaut is 158 and a-umlaut is 155; sharp s has
        // no upper case form to lower from and stays put.
        Assert.Equal('a', Zscii.ToLower('A', table));
        Assert.Equal('1', Zscii.ToLower('1', table));
        Assert.Equal(155, Zscii.ToLower(158, table));
        Assert.Equal(161, Zscii.ToLower(161, table));
    }

    [Fact]
    public void FunctionKeysAndInputCodesAreAsTheStandardDefinesThem()
    {
        // [zm 10.5.2.1] and [zm 3.8]
        Assert.True(Zscii.IsFunctionKey(129));
        Assert.True(Zscii.IsFunctionKey(154));
        Assert.True(Zscii.IsFunctionKey(252));
        Assert.True(Zscii.IsFunctionKey(254));
        Assert.False(Zscii.IsFunctionKey(128));
        Assert.False(Zscii.IsFunctionKey(155));
        Assert.False(Zscii.IsFunctionKey(255));

        Assert.True(Zscii.IsDefinedForInput(8));
        Assert.False(Zscii.IsDefinedForInput(9));
        Assert.True(Zscii.IsDefinedForInput(13));
        Assert.True(Zscii.IsDefinedForInput(27));
        Assert.True(Zscii.IsDefinedForInput(200));
        Assert.False(Zscii.IsDefinedForInput(255));

        // [zm 10.7.2] What a text buffer may hold.
        Assert.True(Zscii.IsDefinedForInputAndOutput('a'));
        Assert.True(Zscii.IsDefinedForInputAndOutput(200));
        Assert.False(Zscii.IsDefinedForInputAndOutput(8));
        Assert.False(Zscii.IsDefinedForInputAndOutput(129));
    }

    /// <summary>
    /// A story with a plausible header and, if any bytes are given, a
    /// terminating characters table holding them.
    /// </summary>
    private static (ZMemory Memory, StoryHeader Header) Story(ZMachineVersion version, params byte[] terminators)
    {
        var bytes = new byte[2048];
        bytes[0] = (byte)version;
        PutWord(bytes, 0x04, 0x0400);
        PutWord(bytes, 0x06, 0x0400);
        PutWord(bytes, 0x08, 0x0380);
        PutWord(bytes, 0x0A, 0x0200);
        PutWord(bytes, 0x0C, 0x0100);
        PutWord(bytes, 0x0E, 0x0400);
        bytes[0x0381] = 6;

        if (terminators.Length > 0)
        {
            PutWord(bytes, 0x2E, Table);
            terminators.CopyTo(bytes, Table);
        }

        var memory = new ZMemory(bytes);
        return (memory, new StoryHeader(memory));
    }

    private static void PutWord(byte[] bytes, int address, int value)
    {
        bytes[address] = (byte)(value >> 8);
        bytes[address + 1] = (byte)value;
    }

    private static LineInputRequest Request(TerminatingCharacters? terminators = null, int maxLength = 80, string initial = "") =>
        new(maxLength, initial.Select(c => (ushort)c).ToList(), terminators ?? TerminatingCharacters.NewlineOnly, null);

    private static string Text(LineInput line) => string.Concat(line.Text.Select(c => (char)c));
}

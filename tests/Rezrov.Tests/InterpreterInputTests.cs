using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Text;
using static Rezrov.Tests.Assembler;

namespace Rezrov.Tests;

/// <summary>
/// The input opcodes of section 10, run through the same tiny story as
/// the rest of the interpreter tests, with a scripted keyboard.
/// </summary>
public partial class InterpreterTests
{
    private const int TextBuffer = 0x300;
    private const int SecondTextBuffer = 0x320;
    private const int ParseBuffer = 0x340;
    private const int Terminators = 0x360;
    private const int UserDictionary = 0x3C0;

    [Fact]
    public void SreadStoresLowerCaseTextWithATerminatorAndParsesItInVersion3()
    {
        var input = new ScriptedInput("Take Lamp,now");
        var run = Execute(
            new Assembler()
                .Variable(Op.Sread, true, Large(TextBuffer), Large(ParseBuffer))
                .Quit(),
            story =>
            {
                story.Words("take", "lamp", ",");
                story.Bytes[TextBuffer] = 20;
                story.Bytes[ParseBuffer] = 5;
            },
            ZMachineVersion.V3,
            input);

        // [zm op:read] Byte 0 holds the maximum plus one, so 19 letters
        // may be typed; the text is lowered and stored from byte 1 with a
        // zero after it.
        Assert.Equal(19, input.Requests[0].MaxLength);
        Assert.Equal("take lamp,now\0", Ascii(run, TextBuffer + 1, 14));

        // [zm 13.6.1] Four words, the comma being one; [zm 13.6.3] each
        // with its dictionary address or 0, its length, and its position
        // counted from the start of the buffer.
        var dictionary = run.Interpreter.Dictionary;
        Assert.Equal(4, run.Story.Bytes[ParseBuffer + 1]);
        AssertToken(run, 0, dictionary.Lookup("take"), 4, 1);
        AssertToken(run, 1, dictionary.Lookup("lamp"), 4, 6);
        AssertToken(run, 2, dictionary.Lookup(","), 1, 10);
        AssertToken(run, 3, 0, 3, 11);
        Assert.NotEqual(0, dictionary.Lookup("take"));
    }

    [Fact]
    public void AreadRecordsTheLengthAndReturnsTheTerminatorInVersion5()
    {
        var run = Execute(
            new Assembler()
                .Variable(Op.Aread, true, Large(TextBuffer), Large(ParseBuffer)).Store(G0)
                .Quit(),
            story =>
            {
                story.Words("look");
                story.Bytes[TextBuffer] = 20;
                story.Bytes[TextBuffer + 6] = 0xAA;
                story.Bytes[ParseBuffer] = 5;
            },
            input: new ScriptedInput("Look"));

        // [zm op:read] The count in byte 1, the text from byte 2, and no
        // terminator after it, which some interpreters wrongly add.
        Assert.Equal(4, run.Story.Bytes[TextBuffer + 1]);
        Assert.Equal("look", Ascii(run, TextBuffer + 2, 4));
        Assert.Equal(0xAA, run.Story.Bytes[TextBuffer + 6]);
        Assert.Equal(Zscii.Newline, run.Global(G0));
        Assert.Equal(1, run.Story.Bytes[ParseBuffer + 1]);
        AssertToken(run, 0, run.Interpreter.Dictionary.Lookup("look"), 4, 2);
    }

    [Fact]
    public void AreadContinuesFromCharactersLeftOverInTheBuffer()
    {
        var input = new ScriptedInput("k");
        var run = Execute(
            new Assembler()
                .Variable(Op.Aread, true, Large(TextBuffer), Large(ParseBuffer)).Store(G0)
                .Quit(),
            story =>
            {
                story.Bytes[TextBuffer] = 20;
                story.Bytes[TextBuffer + 1] = 3;
                "loo"u8.CopyTo(story.Bytes.AsSpan(TextBuffer + 2));
                story.Bytes[ParseBuffer] = 5;
            },
            input: input);

        // [zm op:read] A positive byte 1 is text left over from an
        // interrupted read, handed to the keyboard to continue from.
        Assert.Equal("loo", string.Concat(input.Requests[0].Initial.Select(c => (char)c)));
        Assert.Equal(4, run.Story.Bytes[TextBuffer + 1]);
        Assert.Equal("look", Ascii(run, TextBuffer + 2, 4));
    }

    [Fact]
    public void ReadKeepsOnlyAsManyCharactersAsTheBufferAllows()
    {
        var input = new ScriptedInput("lantern");
        var run = Execute(
            new Assembler()
                .Variable(Op.Aread, true, Large(TextBuffer), Large(ParseBuffer)).Store(G0)
                .Quit(),
            story =>
            {
                story.Bytes[TextBuffer] = 3;
                story.Bytes[ParseBuffer] = 5;
            },
            input: input);

        // [zm op:read] The interpreter should not accept more than the
        // maximum, and cuts the line if the keyboard did not.
        Assert.Equal(3, input.Requests[0].MaxLength);
        Assert.Equal(3, run.Story.Bytes[TextBuffer + 1]);
        Assert.Equal("lan", Ascii(run, TextBuffer + 2, 3));
        AssertToken(run, 0, 0, 3, 2);
    }

    [Fact]
    public void AreadSkipsLexicalAnalysisWhenTheParseBufferIsZero()
    {
        var run = Execute(
            new Assembler()
                .Variable(Op.Aread, true, Large(TextBuffer), Small(0)).Store(G0)
                .Quit(),
            story =>
            {
                story.Bytes[TextBuffer] = 20;
                story.Bytes[ParseBuffer] = 5;
                story.Bytes[ParseBuffer + 1] = 0xAA;
            },
            input: new ScriptedInput("look"));

        // [zm op:read] Versions 5 and later: no parse buffer, no parsing.
        Assert.Equal("look", Ascii(run, TextBuffer + 2, 4));
        Assert.Equal(0xAA, run.Story.Bytes[ParseBuffer + 1]);
    }

    [Fact]
    public void ReadHaltsOnABufferTooSmallToUse()
    {
        // [zm op:read] The standard asks for a halt when the text buffer
        // is under 3 bytes or the parse buffer under 6, since that means
        // an earlier array was overrun.
        var code = new Assembler()
            .Variable(Op.Aread, true, Large(TextBuffer), Large(ParseBuffer)).Store(G0)
            .Quit();

        Assert.Throws<InvalidOperationException>(() => Execute(
            code,
            story => story.Bytes[ParseBuffer] = 5,
            input: new ScriptedInput("look")));

        Assert.Throws<InvalidOperationException>(() => Execute(
            code,
            story => story.Bytes[TextBuffer] = 20,
            input: new ScriptedInput("look")));
    }

    [Fact]
    public void ReadRunsTheInterruptRoutineAndKeepsReadingWhenItReturnsFalse()
    {
        var input = new ScriptedInput("look") { InterruptsBeforeAnswering = 2 };
        var run = Execute(
            new Assembler()
                .Variable(Op.Aread, true, Large(TextBuffer), Large(ParseBuffer), Small(10), Large(RoutineA / 4)).Store(G0)
                .Quit(),
            story =>
            {
                story.Routine(RoutineA, 0, new Assembler().Short0(Op.Print).Text("tick").Short0(Op.Rfalse).ToArray());
                story.Bytes[TextBuffer] = 20;
                story.Bytes[ParseBuffer] = 5;
            },
            input: input);

        // [zm op:read] The routine is called every time/10 seconds and
        // may print; returning false means carry on.
        Assert.Equal(10, input.Requests[0].Timer!.TenthsOfSeconds);
        Assert.Equal("ticktick", run.Output);
        Assert.Equal("look", Ascii(run, TextBuffer + 2, 4));
        Assert.Equal(Zscii.Newline, run.Global(G0));
    }

    [Fact]
    public void ReadStopsWithTerminatorZeroWhenTheInterruptRoutineReturnsTrue()
    {
        var input = new ScriptedInput("look") { InterruptsBeforeAnswering = 1, PartialText = "loo" };
        var run = Execute(
            new Assembler()
                .Variable(Op.Aread, true, Large(TextBuffer), Large(ParseBuffer), Small(10), Large(RoutineA / 4)).Store(G0)
                .Quit(),
            story =>
            {
                story.Routine(RoutineA, 0, new Assembler().Short0(Op.Rtrue).ToArray());
                story.Bytes[TextBuffer] = 20;
                story.Bytes[ParseBuffer] = 5;
            },
            input: input);

        // [zm op:read] A timed-out read returns 0. [zm op:read deviates]
        // The text typed so far is kept, as Frotz keeps it, so that the
        // game can continue the input from it.
        Assert.Equal(0, run.Global(G0));
        Assert.Equal(3, run.Story.Bytes[TextBuffer + 1]);
        Assert.Equal("loo", Ascii(run, TextBuffer + 2, 3));
        Assert.Equal(1, run.Story.Bytes[ParseBuffer + 1]);
        Assert.Single(input.Lines);
    }

    [Fact]
    public void ReadIgnoresTheTimerBeforeVersion4()
    {
        var input = new ScriptedInput("look");
        Execute(
            new Assembler()
                .Variable(Op.Sread, true, Large(TextBuffer), Large(ParseBuffer), Small(10), Large(RoutineA / 2))
                .Quit(),
            story =>
            {
                story.Bytes[TextBuffer] = 20;
                story.Bytes[ParseBuffer] = 5;
            },
            ZMachineVersion.V3,
            input);

        // [zm op:read] Timed input exists from Version 4.
        Assert.Null(input.Requests[0].Timer);
    }

    [Fact]
    public void ReadEndsOnAFunctionKeyTheStoryNamesAsTerminating()
    {
        var input = new ScriptedInput("menu") { Terminator = Zscii.CursorUp };
        var run = Execute(
            new Assembler()
                .Variable(Op.Aread, true, Large(TextBuffer), Large(ParseBuffer)).Store(G0)
                .Quit(),
            story =>
            {
                story.PutWord(0x2E, Terminators);
                story.Bytes[Terminators] = 129;
                story.Bytes[Terminators + 1] = 65;
                story.Bytes[Terminators + 2] = 0;
                story.Bytes[TextBuffer] = 20;
                story.Bytes[ParseBuffer] = 5;
            },
            input: input);

        // [zm 10.5.2.1] The table names cursor up, and 65 is not a
        // function key so it does not count. The terminator is the
        // result.
        var terminators = input.Requests[0].Terminators;
        Assert.True(terminators.IsTerminator(Zscii.CursorUp));
        Assert.False(terminators.IsTerminator(Zscii.CursorDown));
        Assert.False(terminators.IsTerminator(65));
        Assert.Equal(Zscii.CursorUp, run.Global(G0));
        Assert.Equal("menu", Ascii(run, TextBuffer + 2, 4));
    }

    [Fact]
    public void ReadCharReturnsAKey()
    {
        var input = new ScriptedInput();
        input.Keys.Enqueue('y');
        var run = Execute(
            new Assembler()
                .Variable(Op.ReadChar, true, Small(1)).Store(G0)
                .Quit(),
            input: input);

        // [zm op:read_char]
        Assert.Equal('y', run.Global(G0));
        Assert.Null(input.KeyTimers[0]);
        Assert.Empty(run.Interpreter.RuntimeErrors);
    }

    [Fact]
    public void ReadCharReturnsZeroWhenTheInterruptRoutineStopsIt()
    {
        var input = new ScriptedInput { InterruptsBeforeAnswering = 1 };
        input.Keys.Enqueue('y');
        var run = Execute(
            new Assembler()
                .Variable(Op.ReadChar, true, Small(1), Small(5), Large(RoutineA / 4)).Store(G0)
                .Quit(),
            story => story.Routine(RoutineA, 0, new Assembler().Short0(Op.Rtrue).ToArray()),
            input: input);

        // [zm op:read_char] Time and routine work as for read.
        Assert.Equal(5, input.KeyTimers[0]!.TenthsOfSeconds);
        Assert.Equal(0, run.Global(G0));
        Assert.Single(input.Keys);
    }

    [Fact]
    public void ReadCharReportsAnInputDeviceOtherThanOne()
    {
        var input = new ScriptedInput();
        input.Keys.Enqueue('y');
        var run = Execute(
            new Assembler()
                .Variable(Op.ReadChar, true, Small(2)).Store(G0)
                .Quit(),
            input: input);

        // [zm op:read_char] The first operand must be 1. The keyboard is
        // read anyway, since there is nothing else it could have meant.
        Assert.Equal('y', run.Global(G0));
        Assert.Contains("read_char", Assert.Single(run.Interpreter.RuntimeErrors));
    }

    [Fact]
    public void TokeniseUsesTheDictionaryGivenAndKeepsSlotsWhenAsked()
    {
        var run = Execute(
            new Assembler()
                .Variable(Op.Tokenise, true, Large(TextBuffer), Large(ParseBuffer), Large(UserDictionary), Small(1))
                .Quit(),
            story =>
            {
                story.Words("take");
                story.UserWords(UserDictionary, "zork", "aaaa");
                story.Bytes[TextBuffer] = 20;
                story.Bytes[TextBuffer + 1] = 9;
                "take zork"u8.CopyTo(story.Bytes.AsSpan(TextBuffer + 2));
                story.Bytes[ParseBuffer] = 5;
                Array.Fill(story.Bytes, (byte)0xAA, ParseBuffer + 2, 8);
            });

        // [zm op:tokenise] The user dictionary is unsorted and does not
        // know "take", whose slot is left as it was because the flag is
        // set, but the word still counts. "zork" is its first entry.
        Assert.Equal(2, run.Story.Bytes[ParseBuffer + 1]);
        Assert.Equal("\xAA\xAA\xAA\xAA", Ascii(run, ParseBuffer + 2, 4));
        AssertToken(run, 1, UserDictionary + 4, 4, 7);
    }

    [Fact]
    public void TokeniseWithNoDictionaryUsesTheGamesOwnAndWritesUnknownWordsAsZero()
    {
        var run = Execute(
            new Assembler()
                .Variable(Op.Tokenise, true, Large(TextBuffer), Large(ParseBuffer))
                .Quit(),
            story =>
            {
                story.Words("take");
                story.Bytes[TextBuffer] = 20;
                story.Bytes[TextBuffer + 1] = 9;
                "take zork"u8.CopyTo(story.Bytes.AsSpan(TextBuffer + 2));
                story.Bytes[ParseBuffer] = 5;
                Array.Fill(story.Bytes, (byte)0xAA, ParseBuffer + 2, 8);
            });

        Assert.Equal(2, run.Story.Bytes[ParseBuffer + 1]);
        AssertToken(run, 0, run.Interpreter.Dictionary.Lookup("take"), 4, 2);
        AssertToken(run, 1, 0, 4, 7);
    }

    [Fact]
    public void TheParseBufferNeverTakesMoreWordsThanItSaysItCan()
    {
        var run = Execute(
            new Assembler()
                .Variable(Op.Aread, true, Large(TextBuffer), Large(ParseBuffer)).Store(G0)
                .Quit(),
            story =>
            {
                story.Bytes[TextBuffer] = 40;
                story.Bytes[ParseBuffer] = 2;
                story.Bytes[ParseBuffer + 2 + 8] = 0xAA;
            },
            input: new ScriptedInput("one two three four"));

        // [zm op:read] It should stop before going beyond the maximum.
        Assert.Equal(2, run.Story.Bytes[ParseBuffer + 1]);
        Assert.Equal(0xAA, run.Story.Bytes[ParseBuffer + 2 + 8]);
    }

    [Fact]
    public void InputStreamOnePlaysAFileOfCommandsThenReturnsToTheKeyboard()
    {
        var input = new ScriptedInput("wait") { CommandFile = new StringReader("look\n") };
        var run = Execute(
            new Assembler()
                .Variable(Op.InputStream, true, Small(1))
                .Variable(Op.Aread, true, Large(TextBuffer), Large(ParseBuffer)).Store(G0)
                .Variable(Op.Aread, true, Large(SecondTextBuffer), Large(ParseBuffer)).Store(G1)
                .Quit(),
            story =>
            {
                story.Bytes[TextBuffer] = 20;
                story.Bytes[SecondTextBuffer] = 20;
                story.Bytes[ParseBuffer] = 5;
            },
            input: input);

        // [zm 10.2] The first command comes from the file, echoed since
        // nobody typed it, and when the file ends the keyboard is back.
        Assert.Equal("look", Ascii(run, TextBuffer + 2, 4));
        Assert.Equal("wait", Ascii(run, SecondTextBuffer + 2, 4));
        Assert.Equal("look\n", run.Output);
        Assert.Equal(0, run.Interpreter.InputStream);
        Assert.Single(input.Requests);
    }

    [Fact]
    public void InputStreamOneStaysOnTheKeyboardWhenNoFileIsOffered()
    {
        var run = Execute(
            new Assembler()
                .Variable(Op.InputStream, true, Small(1))
                .Variable(Op.Aread, true, Large(TextBuffer), Large(ParseBuffer)).Store(G0)
                .Quit(),
            story =>
            {
                story.Bytes[TextBuffer] = 20;
                story.Bytes[ParseBuffer] = 5;
            },
            input: new ScriptedInput("wait"));

        // [zm 10.2.3] The frontend may decline to choose a file.
        Assert.Equal("wait", Ascii(run, TextBuffer + 2, 4));
        Assert.Equal(0, run.Interpreter.InputStream);
        Assert.Empty(run.Interpreter.RuntimeErrors);
    }

    [Fact]
    public void InputStreamRejectsNumbersOtherThanZeroAndOne()
    {
        var run = Execute(
            new Assembler()
                .Variable(Op.InputStream, true, Small(2))
                .Variable(Op.InputStream, true, Small(0))
                .Quit());

        // [zm 10.2] There are two input streams.
        Assert.Contains("input_stream", Assert.Single(run.Interpreter.RuntimeErrors));
    }

    [Fact]
    public void TheHeaderSaysWhatInputTheInterpreterOffers()
    {
        // [zm 10.3.1] and [zm 10.4.1] A story asking for the mouse and
        // menus, with bit 7 of Flags 1 set as if timed input were on.
        var story = new Story(ZMachineVersion.V5);
        story.Bytes[0x01] = 0x80;
        story.PutWord(0x10, 0x0120);
        var memory = new ZMemory(story.Bytes);
        var output = new TextWriterScreen(new StringWriter());

        var interpreter = new Interpreter(memory, output, new ScriptedInput { SupportsTimedInput = false });

        // [zm 10.5.3] Bit 7 is cleared, [zm 10.3.1.1] bit 5 of Flags 2 is
        // cleared, and [zm 10.4.1.1] so is bit 8.
        Assert.Equal(Flags1FromVersion4.None, interpreter.Header.Flags1FromVersion4);
        Assert.Equal(Flags2.None, interpreter.Header.Flags2);

        interpreter = new Interpreter(memory, output, new ScriptedInput { SupportsTimedInput = true });

        Assert.Equal(Flags1FromVersion4.TimedInputAvailable, interpreter.Header.Flags1FromVersion4);
    }

    [Fact]
    public void RestartSetsTheInterpretersHeaderBitsAgain()
    {
        // The same first-pass, second-pass program as the restart test,
        // in a story whose file says timed input is available when the
        // keyboard here cannot time anything.
        var run = Execute(
            new Assembler()
                .Variable(Op.Loadb, false, Large(0x10), Small(1)).Store(0)
                .Short1(Op.Jz, Var(0)).Branch(false, 12)
                .Long(Op.Store, Small(G0), Small(1))
                .Variable(Op.Storeb, true, Large(0x10), Small(1), Small(2))
                .Short0(Op.Restart)
                .Quit(),
            story => story.Bytes[0x01] = 0x80);

        // [zm 6.1.3] Restart brought bit 7 back from the file, and the
        // interpreter cleared it again.
        Assert.Equal(0, run.Global(G0));
        Assert.Equal(0, run.Story.Bytes[0x01] & 0x80);
    }

    private static string Ascii(Run run, int address, int length) =>
        string.Concat(run.Story.Bytes.AsSpan(address, length).ToArray().Select(b => (char)b));

    // [zm op:read] One 4-byte block per word: dictionary address, length,
    // then position in the text buffer.
    private static void AssertToken(Run run, int index, int address, int length, int position)
    {
        var block = ParseBuffer + 2 + (4 * index);
        var bytes = run.Story.Bytes;

        Assert.Equal(address, (bytes[block] << 8) | bytes[block + 1]);
        Assert.Equal(length, bytes[block + 2]);
        Assert.Equal(position, bytes[block + 3]);
    }
}

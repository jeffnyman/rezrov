using Rezrov.ZMachine;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Text;
using static Rezrov.Tests.Assembler;

namespace Rezrov.Tests;

/// <summary>
/// The output_stream opcode and the streams' place in the interpreter:
/// what read echoes and records, and what restart and the header do.
/// </summary>
public partial class InterpreterTests
{
    [Fact]
    public void OutputStreamThreeCapturesPrintedTextInMemory()
    {
        var screen = new RecordingScreen();
        var run = Execute(
            new Assembler()
                .Variable(Op.OutputStream, true, Small(3), Large(Table))
                .Short0(Op.Print).Text("lamp")
                .Short0(Op.NewLine)
                .Variable(Op.PrintNum, true, Large(0xFFF6))
                .Variable(Op.OutputStream, true, Large(0xFFFD))
                .Short0(Op.Print).Text("seen")
                .Quit(),
            screen: screen);

        // [zm 7.1.2.1] Eight characters: the word, ZSCII 13, and -10.
        var memory = run.Interpreter.Memory;
        Assert.Equal(8, memory.ReadWord(Table));
        Assert.Equal("lamp\r-10", Ascii(run, Table + 2, 8));
        Assert.Equal("seen", screen.Text);
    }

    [Fact]
    public void OutputStreamThreeNestedSeventeenDeepHaltsTheInterpreter()
    {
        var code = new Assembler();
        for (var i = 0; i <= 16; i++)
        {
            code.Variable(Op.OutputStream, true, Small(3), Large(Table + (i * 4)));
        }

        code.Quit();

        // [zm 7.1.2.1.1]
        Assert.Throws<InvalidOperationException>(() => Execute(code));
    }

    [Fact]
    public void OutputStreamTwoWritesTheTranscriptAndSetsTheHeaderBit()
    {
        var files = new ScriptedFiles { Transcript = new StringWriter() };
        var run = Execute(
            new Assembler()
                .Short0(Op.Print).Text("before")
                .Short0(Op.NewLine)
                .Variable(Op.OutputStream, true, Small(2))
                .Short0(Op.Print).Text("during")
                .Short0(Op.NewLine)
                .Variable(Op.OutputStream, true, Large(0xFFFE))
                .Short0(Op.Print).Text("after")
                .Quit(),
            files: files);

        // [zm 7.1.1] and [zm 7.4]
        Assert.Equal("during\n", files.Transcript.ToString());
        Assert.False(run.Interpreter.Header.Flags2.HasFlag(Flags2.Transcripting));
        Assert.Equal("before\nduring\nafter", run.Output);
    }

    [Fact]
    public void TheGameCanTurnTheTranscriptOnThroughFlags2()
    {
        var files = new ScriptedFiles { Transcript = new StringWriter() };
        var run = Execute(
            new Assembler()
                .Variable(Op.Storeb, true, Large(0x11), Small(0), Small(1))
                .Short0(Op.Print).Text("logged")
                .Variable(Op.Storeb, true, Large(0x11), Small(0), Small(0))
                .Short0(Op.Print).Text("not")
                .Quit(),
            files: files);

        // [zm 7.3] and [zm 11.1.2.1]
        Assert.Equal("logged", files.Transcript.ToString());
        Assert.False(run.Interpreter.Streams.TranscriptSelected);
    }

    [Fact]
    public void AnUnavailableTranscriptIsAWarningAndClearsTheBit()
    {
        var run = Execute(
            new Assembler()
                .Variable(Op.OutputStream, true, Small(2))
                .Variable(Op.Storeb, true, Large(0x11), Small(0), Small(1))
                .Short0(Op.Print).Text("x")
                .Quit());

        // [zm 7.6.5.2] and [zm 11.1.2.1]
        Assert.False(run.Interpreter.Header.Flags2.HasFlag(Flags2.Transcripting));
        Assert.Equal(2, run.Interpreter.RuntimeErrors.Count);
        Assert.All(run.Interpreter.RuntimeErrors, e => Assert.Contains("transcript", e));
    }

    [Fact]
    public void ReadEchoesInputToTheTranscriptAndRecordsItToStreamFour()
    {
        var files = new ScriptedFiles { Transcript = new StringWriter(), Record = new StringWriter() };
        var input = new ScriptedInput("Take Lamp");
        input.Keys.Enqueue('y');
        var run = Execute(
            new Assembler()
                .Variable(Op.OutputStream, true, Small(2))
                .Variable(Op.OutputStream, true, Small(4))
                .Short0(Op.Print).Text("prompt")
                .Variable(Op.Aread, true, Large(TextBuffer), Large(ParseBuffer)).Store(G0)
                .Variable(Op.ReadChar, true, Small(1)).Store(G1)
                .Quit(),
            story =>
            {
                story.Bytes[TextBuffer] = 20;
                story.Bytes[ParseBuffer] = 5;
            },
            input: input,
            files: files);

        // [zm 7.1.1.1] The transcript gets the command as typed, then
        // [zm 7.1.2.3] stream 4 gets the command and the key.
        Assert.Equal("promptTake Lamp\n", files.Transcript.ToString());
        Assert.Equal("Take Lamp\ny\n", files.Record.ToString());
        Assert.Equal("take lamp", Ascii(run, TextBuffer + 2, 9));
    }

    [Fact]
    public void ReplayedCommandsAreEchoedAndTranscribedButNotRecorded()
    {
        var files = new ScriptedFiles
        {
            Transcript = new StringWriter(),
            Record = new StringWriter(),
            CommandFile = new StringReader("look\n"),
        };
        var run = Execute(
            new Assembler()
                .Variable(Op.OutputStream, true, Small(2))
                .Variable(Op.OutputStream, true, Small(4))
                .Variable(Op.InputStream, true, Small(1))
                .Variable(Op.Aread, true, Large(TextBuffer), Large(ParseBuffer)).Store(G0)
                .Quit(),
            story =>
            {
                story.Bytes[TextBuffer] = 20;
                story.Bytes[ParseBuffer] = 5;
            },
            files: files);

        Assert.Equal("look\n", run.Output);
        Assert.Equal("look\n", files.Transcript.ToString());
        Assert.Equal("", files.Record.ToString());
    }

    [Fact]
    public void PrintUnicodeReachesEveryStreamInItsOwnForm()
    {
        var files = new ScriptedFiles { Transcript = new StringWriter() };
        var screen = new RecordingScreen();
        var run = Execute(
            new Assembler()
                .Variable(Op.OutputStream, true, Small(2))
                .Ext(11, Large('é'))
                .Variable(Op.OutputStream, true, Small(3), Large(Table))
                .Ext(11, Large('é'))
                .Ext(11, Large('€'))
                .Variable(Op.OutputStream, true, Large(0xFFFD))
                .Quit(),
            screen: screen,
            files: files);

        // [zm 7.5.1] to [zm 7.5.3]
        Assert.Equal("é", screen.Text);
        Assert.Equal("é", files.Transcript.ToString());
        Assert.Equal(170, run.Interpreter.Memory.ReadByte(Table + 2));
        Assert.Equal((byte)'?', run.Interpreter.Memory.ReadByte(Table + 3));
    }

    [Fact]
    public void RestartEmptiesTheMemoryStreamButKeepsTheTranscript()
    {
        var files = new ScriptedFiles { Transcript = new StringWriter() };
        var run = Execute(
            new Assembler()
                .Variable(Op.Loadb, false, Large(0x10), Small(1)).Store(0)
                .Short1(Op.Jz, Var(0)).Branch(false, 20)
                .Variable(Op.OutputStream, true, Small(2))
                .Variable(Op.OutputStream, true, Small(3), Large(Table))
                .Long(Op.Store, Small(G0), Small(1))
                .Variable(Op.Storeb, true, Large(0x10), Small(1), Small(3))
                .Short0(Op.Restart)
                .Short0(Op.Print).Text("again")
                .Quit(),
            files: files);

        // [zm 6.1.3] and [zm 11.1.2.1]: Flags 2 survives, so the transcript
        // is still on after the restart; the memory stream is gone.
        Assert.Equal(0, run.Global(G0));
        Assert.Equal(0, run.Interpreter.Streams.MemoryDepth);
        Assert.Equal("again", files.Transcript.ToString());
    }

    [Fact]
    public void OutputStreamZeroAndUnknownStreams()
    {
        var run = Execute(
            new Assembler()
                .Variable(Op.OutputStream, true, Small(0))
                .Variable(Op.OutputStream, true, Small(6))
                .Short0(Op.Print).Text("still here")
                .Quit());

        // [zm op:output_stream] 0 does nothing; 6 is not a stream.
        Assert.Equal("still here", run.Output);
        Assert.Contains("output_stream", Assert.Single(run.Interpreter.RuntimeErrors));
    }
}

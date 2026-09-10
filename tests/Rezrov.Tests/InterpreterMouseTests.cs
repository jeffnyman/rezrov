using Rezrov.ZMachine;
using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Text;
using static Rezrov.Tests.Assembler;

namespace Rezrov.Tests;

/// <summary>
/// [zm 10.3] Mouse clicks: as input, as positions in the header, as
/// read_mouse sees them, and as command files carry them.
/// </summary>
public partial class InterpreterTests
{
    private static void GiveMouseWords(Story story)
    {
        // [zm 10.3.1] A header extension table of at least two words.
        story.PutWord(0x36, Extension);
        story.PutWord(Extension, 3);
    }

    [Fact]
    public void AClickIsAKeyWithAPositionInTheHeader()
    {
        var input = new ScriptedInput { SupportsMouse = true };
        input.Keys.Enqueue(Zscii.SingleClick);
        input.Clicks.Enqueue(new MouseClick(10, 6, 1));

        var run = Execute(
            new Assembler().Variable(Op.ReadChar, true, Small(1)).Store(G0).Quit(),
            story =>
            {
                GiveMouseWords(story);
                story.PutWord(0x10, 0x0020);
            },
            input: input);

        // [zm 10.3.3] The click comes back as its character, [zm 10.3.2]
        // its x and y go into words 1 and 2, and [zm 10.3.1] the header's
        // mouse bit stays set.
        Assert.Equal(Zscii.SingleClick, run.Global(G0));
        Assert.Equal((10, 6), (run.Interpreter.Header.MouseX, run.Interpreter.Header.MouseY));
        Assert.True(run.Interpreter.Header.Flags2.HasFlag(Flags2.WantsMouse));
    }

    [Fact]
    public void WithoutAMouseTheHeaderSaysSo()
    {
        var run = Execute(new Assembler().Quit(), story => story.PutWord(0x10, 0x0020));

        // [zm 10.3.1.1]
        Assert.False(run.Interpreter.Header.Flags2.HasFlag(Flags2.WantsMouse));
    }

    [Fact]
    public void AClickCanEndACommandAndIsRecordedWithIt()
    {
        var input = new ScriptedInput("look") { SupportsMouse = true, Terminator = Zscii.SingleClick };
        input.Clicks.Enqueue(new MouseClick(10, 6, 1));
        var files = new ScriptedFiles { Record = new StringWriter() };

        var run = Execute(
            new Assembler()
                .Variable(Op.OutputStream, true, Small(4))
                .Variable(Op.Aread, true, Large(TextBuffer), Large(ParseBuffer)).Store(G0)
                .Quit(),
            story =>
            {
                GiveMouseWords(story);
                story.Bytes[TextBuffer] = 20;
                story.Bytes[ParseBuffer] = 5;
                story.PutWord(0x2E, Table);
                story.Bytes[Table] = 255;
            },
            input: input,
            files: files);

        // [zm 10.5.2.1] The click terminates when the table allows it,
        // [zm 10.3.2] its position is noted, and [zm 7.1.2.3] stream 4
        // writes the click with its position, as "[254][10][6]".
        Assert.Equal(Zscii.SingleClick, run.Global(G0));
        Assert.Equal((10, 6), (run.Interpreter.Header.MouseX, run.Interpreter.Header.MouseY));
        Assert.Equal("look[254][10][6]\n", files.Record!.ToString());
    }

    [Fact]
    public void AClickPlayedFromAFileBringsItsPosition()
    {
        var run = Execute(
            new Assembler().Variable(Op.ReadChar, true, Small(1)).Store(G0).Quit(),
            GiveMouseWords,
            before: interpreter => interpreter.PlayCommands(new StringReader("[253][12][7]\n")));

        // [zm 10.2] The file is the keyboard for now, position and all.
        Assert.Equal(Zscii.DoubleClick, run.Global(G0));
        Assert.Equal((12, 7), (run.Interpreter.Header.MouseX, run.Interpreter.Header.MouseY));
    }

    [Fact]
    public void TheMouseWindowKeepsClicksOutsideItFromBeingReported()
    {
        var input = new ScriptedInput { SupportsMouse = true };
        input.Keys.Enqueue(Zscii.SingleClick);
        input.Clicks.Enqueue(new MouseClick(1, 1, 1));
        input.Keys.Enqueue(Zscii.SingleClick);
        input.Clicks.Enqueue(new MouseClick(7, 8, 2));

        var run = RunVersion6(
            new Assembler()
                .Ext(16, Small(2), Small(5), Small(5))
                .Ext(17, Small(2), Small(10), Small(10))
                .Ext(23, Small(2))
                .Variable(Op.ReadChar, true, Small(1)).Store(G0)
                .Ext(22, Large(Table))
                .Quit(),
            setup: GiveMouseWords,
            input: input);

        var memory = run.Interpreter.Memory;

        // [zm 10.3.4] The click at (1,1) is outside window 2, at (5,5)
        // to (14,14), so only the second is reported; [zm op:read_mouse]
        // then gives y, x, the buttons, and no menu.
        Assert.Equal(Zscii.SingleClick, run.Global(G0));
        Assert.Empty(input.Keys);
        Assert.Equal((8, 7, 2, 0), (memory.ReadWord(Table), memory.ReadWord(Table + 2), memory.ReadWord(Table + 4), memory.ReadWord(Table + 6)));
        Assert.Equal((7, 8), (run.Interpreter.Header.MouseX, run.Interpreter.Header.MouseY));
    }

    [Fact]
    public void CommandFilesWriteAndReadClickPositions()
    {
        var click = new MouseClick(10, 6, 1);

        // [zm 7.1.2.3] The remarks' own example, both ways.
        Assert.Equal("[254][10][6]", CommandFile.FormatKey(Zscii.SingleClick, click));
        Assert.Equal("look[254][10][6]", CommandFile.Format("look".Select(c => (ushort)c).ToList(), Zscii.SingleClick, click));
        Assert.Equal("q", CommandFile.FormatKey('q', click));

        var file = new CommandFile(new StringReader("[254][10][6]\n"), UnicodeTranslationTable.Default);
        Assert.Equal(Zscii.SingleClick, file.ReadKey());
        Assert.Equal(new MouseClick(10, 6, 1), file.LastClick);
    }
}

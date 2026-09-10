using Rezrov.ZMachine;
using Rezrov.ZMachine.Screen;
using static Rezrov.Tests.Assembler;

namespace Rezrov.Tests;

/// <summary>
/// [zm 11.1.7] The header extension table: what the interpreter writes
/// there, and [zm op:output_stream] the formatted text stream 3 can
/// produce in Version 6.
/// </summary>
public partial class InterpreterTests
{
    private const int Extension = 0x600;

    private static void GiveExtensionTable(Story story, int words, int flags3)
    {
        // [zm 11.1.7] The word at $36 points at a table whose first word
        // counts the words after it.
        story.PutWord(0x36, Extension);
        story.PutWord(Extension, words);
        story.PutWord(Extension + 8, flags3);
    }

    [Fact]
    public void TheExtensionGetsTheTrueDefaultColorsAndACleanFlags3()
    {
        var five = Execute(new Assembler().Quit(), story => GiveExtensionTable(story, 6, 0x00FF), screen: new RecordingScreen());
        var six = RunVersion6(new Assembler().Quit(), setup: story => GiveExtensionTable(story, 6, 0x00FF));

        // [zm 11.1.7.3] Words 5 and 6 hold the true colors behind the
        // defaults, white on blue here; [zm 11.1.7.4] the transparency
        // bit is provided in Version 6 only, and [zm 11.1.7.4.1] every
        // other bit is cleared.
        Assert.Equal(0x7FFF, five.Interpreter.Memory.ReadWord(Extension + 10));
        Assert.Equal(0x59A0, five.Interpreter.Memory.ReadWord(Extension + 12));
        Assert.Equal(Flags3.None, five.Interpreter.Header.Flags3);
        Assert.Equal(Flags3.WantsTransparency, six.Interpreter.Header.Flags3);
    }

    [Fact]
    public void AShortExtensionTableIsLeftAlone()
    {
        var run = Execute(new Assembler().Quit(), story => GiveExtensionTable(story, 3, 0x00FF), screen: new RecordingScreen());

        // [zm 11.1.7.2] Writing past the end of the table does nothing,
        // so the bytes beyond it keep whatever the story put there.
        Assert.Equal(0x00FF, run.Interpreter.Memory.ReadWord(Extension + 8));
        Assert.Equal(0, run.Interpreter.Memory.ReadWord(Extension + 10));
    }

    [Fact]
    public void Stream3WithAWidthStoresFormattedLines()
    {
        var run = RunVersion6(new Assembler()
            .Ext(17, Small(0), Small(24), Small(14))
            .Variable(Op.OutputStream, true, Small(3), Large(Table), Small(0))
            .Short0(Op.Print).Text("here is an abacus")
            .Variable(Op.OutputStream, true, Large(0xFFFD))
            .Variable(Op.OutputStream, true, Small(3), Large(Table + 0x40), Large(0xFFF8))
            .Short0(Op.Print).Text("here is an abacus")
            .Variable(Op.OutputStream, true, Large(0xFFFD))
            .Quit());

        var memory = run.Interpreter.Memory;

        // [zm op:output_stream] Wrapped as window 0, fourteen units wide,
        // would wrap it, then to a box eight units wide; each line a
        // count and its characters, ended by a zero word, as print_form
        // reads.
        Assert.Equal(["here is an", "abacus"], Lines(Table));
        Assert.Equal(["here is", "an", "abacus"], Lines(Table + 0x40));
        Assert.Equal(6, run.Interpreter.Header.OutputStream3Width);

        List<string> Lines(int at)
        {
            var lines = new List<string>();
            while (memory.ReadWord(at) is var count && count != 0)
            {
                lines.Add(string.Concat(Enumerable.Range(0, count).Select(i => (char)memory.ReadByte(at + 2 + i))));
                at += 2 + count;
            }

            return lines;
        }
    }

    [Fact]
    public void Stream3WithAWidthPrintsBackThroughPrintForm()
    {
        var run = RunVersion6(new Assembler()
            .Ext(17, Small(0), Small(24), Small(10))
            .Variable(Op.OutputStream, true, Small(3), Large(Table), Small(0))
            .Short0(Op.Print).Text("here is an abacus")
            .Variable(Op.OutputStream, true, Large(0xFFFD))
            .Ext(26, Large(Table))
            .Quit());

        var windows = run.Interpreter.Windows!;

        // [zm op:print_form] The formatted lines come back out as lines.
        Assert.Equal("here is an", windows.RowText(0)[..10]);
        Assert.Equal("abacus", windows.RowText(1)[..6]);
    }
}

using Rezrov.Tui;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Screen;

namespace Rezrov.Tests;

/// <summary>
/// The terminal frontend's grid: how the model's runs, newlines, and
/// windows land on rows, and how the lower window scrolls.
/// </summary>
public class ScreenBufferTests
{
    private static readonly TextAttributes Blank = new(TextStyle.Roman, ScreenColor.White, ScreenColor.Black, 1);

    [Fact]
    public void TextStartsAtTheBottomOrTheTopByVersion()
    {
        // [zm 8.6.3] Versions 1 to 4 start at the bottom so text scrolls
        // up; Version 5 starts at the top.
        var early = new ScreenBuffer(10, 4, Blank, cursorStartsAtBottom: true);
        var late = new ScreenBuffer(10, 4, Blank, cursorStartsAtBottom: false);

        early.Print("hi", Blank);
        late.Print("hi", Blank);

        Assert.Equal("hi        ", early.RowText(3));
        Assert.Equal("hi        ", late.RowText(0));
    }

    [Fact]
    public void TheLowerWindowScrollsUnderTheUpperWindow()
    {
        var (buffer, model) = MakeWithModel(10, 4);
        model.SplitWindow(1);
        model.SetWindow(ScreenModel.Upper);
        Print(model, "STATUS");
        model.SetWindow(ScreenModel.Lower);
        buffer.UpdateUpper(model);

        buffer.Print("one", Blank);
        buffer.NewLine();
        buffer.Print("two", Blank);
        buffer.NewLine();
        buffer.Print("three", Blank);
        buffer.NewLine();
        buffer.Print("four", Blank);

        // [zm 8.7.3.1] The lower window scrolled; [zm 8.7.2.1] the upper
        // window did not.
        Assert.Equal("STATUS    ", buffer.RowText(0));
        Assert.Equal("two       ", buffer.RowText(1));
        Assert.Equal("three     ", buffer.RowText(2));
        Assert.Equal("four      ", buffer.RowText(3));
        Assert.Equal(1, buffer.UpperLines);
        Assert.Equal((3, 4), (buffer.CursorRow, buffer.CursorColumn));
    }

    [Fact]
    public void TheStatusLineTakesTheTopRowInVersion3()
    {
        var (buffer, model) = MakeWithModel(20, 5, ZMachineVersion.V3);
        model.ShowStatusLine("Hall", timeGame: false, 3, 4);
        model.SplitWindow(1);
        model.SetWindow(ScreenModel.Upper);
        Print(model, "map");
        model.SetWindow(ScreenModel.Lower);

        buffer.UpdateUpper(model);

        // [zm 8.6.1.1] The status line on top, the upper window below it.
        Assert.Equal(1, buffer.StatusRows);
        Assert.Equal(" Hall", buffer.RowText(0)[..5]);
        Assert.Equal("map", buffer.RowText(1)[..3]);
        Assert.Equal(2, buffer.LowerTop);
    }

    [Fact]
    public void ASplitMovesACursorItWouldSwallow()
    {
        // [zm 8.7.2.2]
        var (buffer, model) = MakeWithModel(10, 5);
        buffer.Print("x", Blank);
        Assert.Equal(0, buffer.CursorRow);

        model.SplitWindow(2);
        buffer.UpdateUpper(model);

        Assert.Equal((2, 0), (buffer.CursorRow, buffer.CursorColumn));
    }

    [Fact]
    public void BackspaceAndEraseToEndOfLineEditTheCurrentRow()
    {
        var buffer = new ScreenBuffer(10, 2, Blank, cursorStartsAtBottom: false);
        buffer.Print("hello", Blank);

        buffer.Backspace();
        buffer.Backspace();
        Assert.Equal("hel       ", buffer.RowText(0));
        Assert.Equal(3, buffer.CursorColumn);

        buffer.Print("p there", Blank);
        buffer.Backspace();
        buffer.Backspace();
        buffer.Backspace();
        buffer.Backspace();
        buffer.Backspace();
        buffer.Backspace();
        buffer.EraseToEndOfLine(Blank);
        Assert.Equal("help      ", buffer.RowText(0));
    }

    [Fact]
    public void ErasingTheLowerWindowLeavesTheUpperWindowAlone()
    {
        var (buffer, model) = MakeWithModel(10, 4);
        model.SplitWindow(1);
        model.SetWindow(ScreenModel.Upper);
        Print(model, "kept");
        model.SetWindow(ScreenModel.Lower);
        buffer.UpdateUpper(model);
        buffer.Print("gone", Blank);

        buffer.EraseLower(Blank);

        // [zm 8.7.3.2]
        Assert.Equal("kept      ", buffer.RowText(0));
        Assert.Equal("          ", buffer.RowText(1));
        Assert.Equal((1, 0), (buffer.CursorRow, buffer.CursorColumn));
    }

    [Fact]
    public void ARunPastTheWidthWraps()
    {
        var buffer = new ScreenBuffer(5, 3, Blank, cursorStartsAtBottom: false);

        buffer.Print("abcdefg", Blank);

        Assert.Equal("abcde", buffer.RowText(0));
        Assert.Equal("fg   ", buffer.RowText(1));
    }

    [Fact]
    public void ResizeKeepsWhatFitsAndTheCursorInside()
    {
        var buffer = new ScreenBuffer(10, 4, Blank, cursorStartsAtBottom: true);
        buffer.Print("bottom row", Blank);

        buffer.Resize(6, 2);

        Assert.Equal((6, 2), (buffer.Width, buffer.Height));
        Assert.Equal(1, buffer.CursorRow);
        Assert.Equal(5, buffer.CursorColumn);
    }

    [Fact]
    public void CellsKeepTheirAttributes()
    {
        var buffer = new ScreenBuffer(4, 1, Blank, cursorStartsAtBottom: false);
        var bold = Blank with { Style = TextStyle.Bold, Foreground = ScreenColor.Red };

        buffer.Print("ab", bold);

        Assert.Equal(bold, buffer[0, 1].Attributes);
        Assert.Equal(Blank with { Style = TextStyle.Roman }, buffer[0, 2].Attributes);
    }

    [Fact]
    public void TheCursorHidesWhenAVersion6GameHidesIt()
    {
        var bytes = new byte[2048];
        bytes[0] = 6;
        PutWord(bytes, 0x04, 0x0400);
        PutWord(bytes, 0x0E, 0x0400);
        var memory = new ZMemory(bytes);
        var model = new WindowedScreenModel(new RecordingScreen(10, 4), new StoryHeader(memory), memory);
        var buffer = new ScreenBuffer(10, 4, Blank, cursorStartsAtBottom: false);

        // [zm op:set_cursor] -1 hides the cursor and -2 shows it, and
        // the terminal follows on each repaint.
        model.SetCursor(-1, 0);
        buffer.UpdateWindows(model);
        Assert.False(buffer.CursorVisible);

        model.SetCursor(-2, 0);
        buffer.UpdateWindows(model);
        Assert.True(buffer.CursorVisible);
    }

    private static (ScreenBuffer Buffer, ScreenModel Model) MakeWithModel(int width, int height, ZMachineVersion version = ZMachineVersion.V5)
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

        var memory = new ZMemory(bytes);
        var header = new StoryHeader(memory);
        var screen = new RecordingScreen(width, height);
        var model = new ScreenModel(screen, header, memory);
        return (new ScreenBuffer(width, height, Blank, cursorStartsAtBottom: version <= ZMachineVersion.V4), model);
    }

    private static void PutWord(byte[] bytes, int address, int value)
    {
        bytes[address] = (byte)(value >> 8);
        bytes[address + 1] = (byte)value;
    }

    private static void Print(ScreenModel model, string text)
    {
        foreach (var c in text)
        {
            model.Print(c);
        }
    }
}

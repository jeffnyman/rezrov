using Rezrov.Glulx;
using Rezrov.Glulx.Glk;
using Rezrov.Tui;
using GlkWindowType = Rezrov.Glulx.Glk.WindowType;

namespace Rezrov.Tests;

/// <summary>
/// [glk #line_events] and [glk #char_events] The terminal's Glk
/// display: lines edited in a window, keys, the timer, the paging
/// prompt, and a change of size.
/// </summary>
public class TerminalGlkDisplayTests
{
    private const uint Buffer = 0x400;

    private static (GlkLibrary Glk, TerminalGlkDisplay Display, GlulxMemory Memory) Library(int width = 20, int height = 5, TextReader? commands = null)
    {
        var display = new TerminalGlkDisplay(width, height, () => { }, commands);
        var memory = new GlulxMemory(TestGlulx.File(ramStart: 0x400, extStart: 0x800, endMem: 0xA00));
        return (new GlkLibrary(display), display, memory);
    }

    private static string Row(TerminalGlkDisplay display, int row)
    {
        display.Repaint();
        return display.Screen.RowText(row);
    }

    private static void Type(TerminalGlkDisplay display, string text)
    {
        foreach (var character in text)
        {
            display.Enqueue(character);
        }
    }

    [Fact]
    public void ALineIsEditedInATextBufferAndEnterEndsIt()
    {
        var (glk, display, memory) = Library();
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;
        glk.PutChar(story.Stream, '>');
        glk.RequestLineEvent(story, memory, Buffer, 20, 0, false);

        Type(display, "lokk");
        display.Enqueue(GlkKeyCode.Delete);
        display.Enqueue(GlkKeyCode.Delete);
        Type(display, "ok");
        display.Enqueue(GlkKeyCode.Return);
        var result = glk.Select();

        // [glk #line_events] Backspace takes back a character, the line
        // is echoed in the buffer with a new line after it, and the
        // event carries the text.
        Assert.Equal(EventType.LineInput, result.Type);
        Assert.Equal(4u, result.Value1);
        Assert.Equal("look", System.Text.Encoding.Latin1.GetString(memory.Slice(Buffer, 4)));
        Assert.Equal(">look               ", Row(display, 0));
        Assert.Equal((1, 0), display.Cursor);
    }

    [Fact]
    public void ALineInATextGridIsLaidOverTheGridUntilItIsDone()
    {
        var (glk, display, memory) = Library();
        var grid = glk.OpenWindow(null, 0, 0, GlkWindowType.TextGrid, 1)!;
        glk.PutChar(grid.Stream, '?');
        glk.RequestLineEvent(grid, memory, Buffer, 20, 0, false);
        Type(display, "ab");

        // The typed text shows at the grid's cursor while a key is
        // awaited; a terminator the game asked for ends the line and is
        // reported.
        GlkLibrary.SetLineTerminators(grid, [GlkKeyCode.Func1]);
        display.Enqueue(GlkKeyCode.Func1);
        var result = glk.Select();

        Assert.Equal(EventType.LineInput, result.Type);
        Assert.Equal(GlkKeyCode.Func1, result.Value2);
        Assert.Null(display.Screen.Overlay);
        Assert.Equal("?ab                 ", Row(display, 0));
    }

    [Fact]
    public void ACommandsFileTypesTheLine()
    {
        var (glk, display, memory) = Library(commands: new StringReader("north\n"));
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;
        glk.RequestLineEvent(story, memory, Buffer, 20, 0, false);

        var result = glk.Select();

        Assert.Equal(EventType.LineInput, result.Type);
        Assert.Equal("north", System.Text.Encoding.Latin1.GetString(memory.Slice(Buffer, 5)));
        Assert.Equal("north               ", Row(display, 0));
    }

    [Fact]
    public void AKeyAnswersACharacterRequest()
    {
        var (glk, display, _) = Library();
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;
        glk.RequestCharEvent(story, false);
        display.Enqueue(GlkKeyCode.Down);

        var result = glk.Select();

        Assert.Equal(EventType.CharInput, result.Type);
        Assert.Equal(GlkKeyCode.Down, result.Value1);
    }

    [Fact]
    public void TheTimerFiresWhenNoKeyComes()
    {
        var (glk, display, _) = Library();
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;
        glk.RequestCharEvent(story, false);

        // [glk #timer_events] With a key request and a timer, the timer
        // wins when nothing is typed within its interval.
        var input = display.WaitForInput([], [story], TimeSpan.FromMilliseconds(1));
        Assert.Equal(GlkInputKind.Timer, input.Kind);

        var alone = display.WaitForInput([], [], TimeSpan.FromMilliseconds(1));
        Assert.Equal(GlkInputKind.Timer, alone.Kind);
    }

    [Fact]
    public void WithNothingToWaitForTheInputHasEnded()
    {
        var (_, display, _) = Library();

        Assert.Equal(GlkInputKind.Ended, display.WaitForInput([], [], null).Kind);
    }

    [Fact]
    public void AWindowFullOfNewTextPausesForAKey()
    {
        var (glk, display, _) = Library(width: 20, height: 3);
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;
        display.Enqueue(' ');

        // Two lines fill the window's height less one, so the third
        // waits on the key queued above; the prompt is gone once it has.
        foreach (var line in new[] { "one\n", "two\n", "three\n" })
        {
            foreach (var character in line)
            {
                glk.PutChar(story.Stream, character);
            }
        }

        Assert.Equal(1, display.MorePrompts);
        Assert.Null(display.Screen.Overlay);
    }

    [Fact]
    public void AResizeIsReportedAsAnArrangementBeforeInput()
    {
        var (glk, display, _) = Library();
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;
        glk.RequestCharEvent(story, false);

        display.Resize(30, 8);
        var result = glk.Select();

        // [glk #arrange_events] The layout is redone for the new size
        // and the game hears of it, with no window named.
        Assert.Equal(EventType.Arrange, result.Type);
        Assert.Null(result.Window);
        Assert.Equal((30, 8), (story.Width, story.Height));
        Assert.Equal((30, 8), (display.Width, display.Height));
    }

    [Fact]
    public void ANoticeShowsInTheBottomRowUntilAKey()
    {
        var (_, display, _) = Library(width: 20, height: 3);
        display.Enqueue('x');

        display.Notice("[Over]");

        Assert.Equal("[Over]              ", Row(display, 2));
        Assert.Null(display.Cursor);
    }
}

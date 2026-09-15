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

    private static void Write(GlkLibrary glk, GlkWindow window, string text)
    {
        foreach (var character in text)
        {
            glk.PutChar(window.Stream, character);
        }
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
    public void AClickIsReportedToTheWindowItLandsIn()
    {
        var (glk, display, _) = Library();
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;
        var status = glk.OpenWindow(story, WindowMethod.Above | WindowMethod.Fixed, 2, GlkWindowType.TextGrid, 2)!;
        glk.RequestMouseEvent(status);

        // [glk #mouse_events] The event carries the cell of the window,
        // counted from the window's own corner rather than the screen's.
        display.EnqueueClick(status.Left + 3, status.Top + 1);
        var input = display.WaitForInput([], [], null);

        Assert.Equal(GlkInputKind.Mouse, input.Kind);
        Assert.Same(status, input.Window);
        Assert.Equal((3u, 1u), (input.Column, input.Row));

        // A click in the other window, which asked for nothing, is
        // nothing: the wait goes on until the library wakes it.
        glk.RequestMouseEvent(status);
        display.EnqueueClick(story.Left, story.Top);
        display.Wake();
        Assert.Equal(GlkInputKind.Woken, display.WaitForInput([], [], null).Kind);
        Assert.True(status.MouseRequest);
    }

    [Fact]
    public void AClickOnLinkTextIsReportedAsTheLink()
    {
        var (glk, display, _) = Library();
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;

        GlkLibrary.SetHyperlink(story.Stream, 42);
        Write(glk, story, "north");
        GlkLibrary.SetHyperlink(story.Stream, 0);
        Write(glk, story, " or down");
        GlkLibrary.RequestHyperlinkEvent(story);

        display.EnqueueClick(2, 0);
        var input = display.WaitForInput([], [], null);

        // [glk #link_events] The value the game gave the text that was
        // clicked on.
        Assert.Equal(GlkInputKind.Hyperlink, input.Kind);
        Assert.Same(story, input.Window);
        Assert.Equal(42u, input.Link);

        // [glk #link_creating] Link text is shown in a color of its own,
        // and ordinary text beside it is not.
        Assert.Equal("north or down       ", Row(display, 0));
        Assert.Equal(GlkScreen.LinkColor, display[0, 2].Attributes.Foreground);
        Assert.NotEqual(GlkScreen.LinkColor, display[0, 8].Attributes.Foreground);

        // A click on text that is not a link says nothing.
        GlkLibrary.RequestHyperlinkEvent(story);
        display.EnqueueClick(8, 0);
        display.Wake();
        Assert.Equal(GlkInputKind.Woken, display.WaitForInput([], [], null).Kind);
    }

    [Fact]
    public void AClickWhileALineIsBeingTypedKeepsTheLine()
    {
        var (glk, display, memory) = Library();
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;
        GlkLibrary.SetHyperlink(story.Stream, 3);
        Write(glk, story, "map");
        GlkLibrary.SetHyperlink(story.Stream, 0);
        GlkLibrary.RequestHyperlinkEvent(story);
        glk.RequestLineEvent(story, memory, Buffer, 20, 0, false);

        // [glk #link_events] A window can be taking a line and watching
        // for links at once.
        Type(display, "no");
        display.EnqueueClick(1, 0);
        var input = display.WaitForInput([story], [], null);
        Assert.Equal(GlkInputKind.Hyperlink, input.Kind);
        Assert.Equal(3u, input.Link);

        // The line goes on from where it was left when the game asks
        // again, and what was typed is still there.
        Type(display, "rth");
        display.Enqueue(GlkKeyCode.Return);
        var line = display.WaitForInput([story], [], null);
        Assert.Equal("north", line.Text);
    }

    [Fact]
    public void AWakeEndsAWaitWithNoKey()
    {
        var (glk, display, memory) = Library();
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;

        // [glk #sound_playing] A wake from another thread ends a wait
        // for a key, for a line, and for the timer, each reported as a
        // wake and not as a key or the timer; a key typed after is
        // still there for the next wait.
        glk.RequestLineEvent(story, memory, Buffer, 20, 0, false);
        display.Wake();
        Assert.Equal(GlkInputKind.Woken, display.WaitForInput([story], [], TimeSpan.FromSeconds(5)).Kind);

        GlkLibrary.CancelLineEvent(story);
        glk.RequestCharEvent(story, false);
        display.Wake();
        Assert.Equal(GlkInputKind.Woken, display.WaitForInput([], [story], null).Kind);

        display.Wake();
        Assert.Equal(GlkInputKind.Woken, display.WaitForInput([], [], TimeSpan.FromSeconds(5)).Kind);

        display.Wake();
        display.Enqueue('x');
        Assert.Equal('x', display.WaitForAnyKey());
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

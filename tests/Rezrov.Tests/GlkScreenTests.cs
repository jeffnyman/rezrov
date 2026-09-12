using Rezrov.Glulx.Glk;
using Rezrov.Tui;
using Rezrov.ZMachine.Screen;
using GlkWindowType = Rezrov.Glulx.Glk.WindowType;

namespace Rezrov.Tests;

/// <summary>
/// [glk #window_arrangement] The terminal's picture of a Glk display:
/// the window tree painted as rectangles, text buffers wrapped and
/// scrolled, text grids copied, and the cursor placed.
/// </summary>
public class GlkScreenTests
{
    private static readonly TextAttributes Normal = new(TextStyle.Roman, ScreenColor.Default, ScreenColor.Default, 1);

    /// <summary>
    /// A display that hands everything to a screen, which is what the
    /// terminal's Glk display will do once it also takes input.
    /// </summary>
    private sealed class ScreenDisplay(GlkScreen screen) : IGlkDisplay
    {
        public int Width => screen.Width;

        public int Height => screen.Height;

        public void Print(GlkWindow window, uint character, GlkStyle style) => screen.Print(window, character, style);

        public void Clear(GlkWindow window) => screen.Clear(window);

        public void Arranged(GlkWindow? root) => screen.Arranged(root);

        public GlkInput WaitForInput(IReadOnlyList<GlkWindow> lineRequests, IReadOnlyList<GlkWindow> charRequests, TimeSpan? timeout) => GlkInput.Ended;
    }

    private static (GlkLibrary Glk, GlkScreen Screen) Library(int width = 20, int height = 6)
    {
        var screen = new GlkScreen(width, height, Normal);
        return (new GlkLibrary(new ScreenDisplay(screen)), screen);
    }

    /// <summary>A row of the screen, painted afresh.</summary>
    private static string Row(GlkScreen screen, int row)
    {
        screen.Repaint();
        return screen.RowText(row);
    }

    private static void Print(GlkLibrary glk, GlkWindow window, string text, GlkStyle style = GlkStyle.Normal)
    {
        GlkLibrary.SetStyle(window.Stream, style);
        foreach (var character in text)
        {
            glk.PutChar(window.Stream, character);
        }
    }

    [Fact]
    public void AnEmptyScreenIsBlank()
    {
        var (_, screen) = Library();

        Assert.Equal("                    ", Row(screen, 0));
        Assert.Null(screen.Root);
        screen.Repaint();
        Assert.Equal(Cell.Blank(Normal), screen[5, 19]);
    }

    [Fact]
    public void AStatusGridAndAStoryBufferPaintWhereTheLayoutPutThem()
    {
        var (glk, screen) = Library();
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;
        var status = glk.OpenWindow(story, WindowMethod.Above | WindowMethod.Fixed, 1, GlkWindowType.TextGrid, 2)!;

        Print(glk, status, "Kitchen", GlkStyle.Normal);
        Print(glk, story, "You are here.\nA table.", GlkStyle.Normal);

        // [glk #window_textgrid] The grid's cells are copied to its
        // rows; [glk #window_textbuf] the buffer's paragraphs are laid
        // out below it, one line each.
        Assert.Equal("Kitchen             ", Row(screen, 0));
        Assert.Equal("You are here.       ", Row(screen, 1));
        Assert.Equal("A table.            ", Row(screen, 2));
        Assert.Equal("                    ", Row(screen, 3));
        Assert.Same(glk.Root, screen.Root);
    }

    [Fact]
    public void TextBuffersWrapAtWordsAndScrollToTheNewestLine()
    {
        var (glk, screen) = Library(width: 12, height: 3);
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;

        Print(glk, story, "The quick brown fox jumps over the lazy dog.");

        // Twelve columns take "The quick", then "brown fox", and so on;
        // three rows show the last three lines.
        Assert.Equal("jumps over  ", Row(screen, 0));
        Assert.Equal("the lazy    ", Row(screen, 1));
        Assert.Equal("dog.        ", Row(screen, 2));
        Assert.Equal((2, 4), screen.CursorFor(story));
    }

    [Fact]
    public void AWordWiderThanTheWindowBreaksInTheMiddle()
    {
        var (glk, screen) = Library(width: 5, height: 3);
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;

        Print(glk, story, "abcdefgh ij");

        Assert.Equal("abcde", Row(screen, 0));
        Assert.Equal("fgh  ", Row(screen, 1));
        Assert.Equal("ij   ", Row(screen, 2));
    }

    [Fact]
    public void StylesBecomeTerminalAttributes()
    {
        var (glk, screen) = Library();
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;

        Print(glk, story, "a", GlkStyle.Emphasized);
        Print(glk, story, "b", GlkStyle.Header);
        Print(glk, story, "c", GlkStyle.Input);
        Print(glk, story, "d", GlkStyle.BlockQuote);

        // [glk #stream_style] Emphasis is italic, a header and input are
        // bold, and a style the terminal cannot show is plain.
        screen.Repaint();
        Assert.Equal(TextStyle.Italic, screen[0, 0].Attributes.Style);
        Assert.Equal(TextStyle.Bold, screen[0, 1].Attributes.Style);
        Assert.Equal(TextStyle.Bold, screen[0, 2].Attributes.Style);
        Assert.Equal(TextStyle.Roman, screen[0, 3].Attributes.Style);
    }

    [Fact]
    public void SideBySideWindowsShareTheRows()
    {
        var (glk, screen) = Library(width: 20, height: 2);
        var left = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;
        var right = glk.OpenWindow(left, WindowMethod.Right | WindowMethod.Proportional, 50, GlkWindowType.TextGrid, 2)!;

        Print(glk, left, "left side text");
        Print(glk, right, "grid");

        // [glk #window_arrangement] Ten columns each: the buffer wraps
        // at its own width, and the grid starts at column ten.
        Assert.Equal("left side grid      ", Row(screen, 0));
        Assert.Equal("text                ", Row(screen, 1));
        Assert.Equal((0, 14), screen.CursorFor(right));
        Assert.Equal((1, 4), screen.CursorFor(left));
    }

    [Fact]
    public void ClearingABufferEmptiesItAndClosingAWindowRemovesIt()
    {
        var (glk, screen) = Library();
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;
        var status = glk.OpenWindow(story, WindowMethod.Above | WindowMethod.Fixed, 1, GlkWindowType.TextGrid, 2)!;
        Print(glk, status, "Status");
        Print(glk, story, "Some text");

        story.Clear();
        Assert.Equal("                    ", Row(screen, 1));
        Assert.Equal((1, 0), screen.CursorFor(story));

        // [glk #window_opening] Closing the status line gives the story
        // the top row; text printed after fills from there.
        glk.CloseWindow(status);
        Print(glk, story, "Back");
        Assert.Equal("Back                ", Row(screen, 0));
        Assert.Null(screen.CursorFor(status));
    }

    [Fact]
    public void ABlankWindowShowsNothing()
    {
        var (glk, screen) = Library(width: 10, height: 4);
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;
        var blank = glk.OpenWindow(story, WindowMethod.Above | WindowMethod.Fixed, 2, GlkWindowType.Blank, 2)!;
        Print(glk, story, "text");

        // [glk #window_types] A blank window has no units, so its fixed
        // size is nothing and it takes no rows at all.
        Assert.Equal((0, 0), (blank.Width, blank.Height));
        Assert.Equal("text      ", Row(screen, 0));
        Assert.Null(screen.CursorFor(blank));
    }

    [Fact]
    public void ResizingWrapsAgainAtTheNewWidth()
    {
        var (glk, screen) = Library(width: 20, height: 4);
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;
        Print(glk, story, "one two three four");
        Assert.Equal("one two three four  ", Row(screen, 0));

        // The terminal shrinks: the screen takes the new size, the
        // library lays the tree out for it, and the paragraph wraps
        // again.
        screen.Resize(9, 4);
        glk.Arrange();
        Assert.Equal((9, 4), (story.Width, story.Height));
        Assert.Equal("one two  ", Row(screen, 0));
        Assert.Equal("three    ", Row(screen, 1));
        Assert.Equal("four     ", Row(screen, 2));
    }

    [Fact]
    public void CharactersBeyondTheBasicPlaneAreQuestionMarks()
    {
        var (glk, screen) = Library();
        var story = glk.OpenWindow(null, 0, 0, GlkWindowType.TextBuffer, 1)!;

        glk.PutChar(story.Stream, 0x3B1);
        glk.PutChar(story.Stream, 0x1F600);

        Assert.Equal("α?                  ", Row(screen, 0));
    }

    [Fact]
    public void ThePaneKeepsAScrollbackButNotForever()
    {
        var pane = new TextPane();
        pane.Resize(10);
        for (var i = 0; i < TextPane.Scrollback + 5; i++)
        {
            pane.Put('x', Normal);
            pane.Put('\n', Normal);
        }

        Assert.Equal(TextPane.Scrollback, pane.ParagraphCount);
        Assert.Equal(3, pane.VisibleLines(3).Count);
        Assert.Equal((2, 0), pane.Cursor(3));
    }
}

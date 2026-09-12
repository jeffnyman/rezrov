using Rezrov.Glulx.Glk;

namespace Rezrov.Tests;

/// <summary>
/// [glk #window] The window tree: opening, splitting, sizing, closing,
/// and the text grid and echo stream behaviors.
/// </summary>
public class GlkWindowTests
{
    private static (GlkLibrary Glk, RecordingGlkDisplay Display) Library(int width = 40, int height = 10)
    {
        var display = new RecordingGlkDisplay(width, height);
        return (new GlkLibrary(display), display);
    }

    [Fact]
    public void TheFirstWindowFillsTheDisplay()
    {
        var (glk, display) = Library();

        var window = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 7)!;

        // [glk #window_opening] With no windows, the split arguments are
        // ignored and the window takes the whole display.
        Assert.Same(window, glk.Root);
        Assert.Equal((40, 10), (window.Width, window.Height));
        Assert.Equal(7u, window.Rock);
        Assert.Null(window.Parent);
        Assert.Equal(1, glk.Windows.Count);
        Assert.Equal(1, glk.Streams.Count);
        Assert.Equal(1, display.ArrangedCount);
        Assert.Same(window, display.LastRoot);
    }

    [Fact]
    public void SplittingMakesAPairWithTheNewWindowAsKey()
    {
        var (glk, _) = Library();
        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;

        var status = glk.OpenWindow(story, WindowMethod.Above | WindowMethod.Fixed, 2, WindowType.TextGrid, 2)!;

        // [glk #window_arrangement] The split window is replaced by a
        // pair holding both, with the new window above and sized.
        var pair = Assert.IsType<PairWindow>(glk.Root);
        Assert.Same(status, pair.First);
        Assert.Same(story, pair.Second);
        Assert.Same(status, pair.Key);
        Assert.Same(status, pair.SizedChild);
        Assert.Equal(0u, pair.Rock);
        Assert.Same(pair, status.Parent);
        Assert.Same(pair, story.Parent);
        Assert.Equal((40, 2), (status.Width, status.Height));
        Assert.Equal((40, 8), (story.Width, story.Height));
        Assert.Equal((0, 0), (pair.Width, pair.Height));
        Assert.Equal(3, glk.Windows.Count);
    }

    [Fact]
    public void ProportionalSplitsDivideByPercentage()
    {
        var (glk, _) = Library();
        var a = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;

        var b = glk.OpenWindow(a, WindowMethod.Left | WindowMethod.Proportional, 25, WindowType.TextBuffer, 2)!;

        // [glk #window_opening] 25 percent to the new window on the left,
        // the rest to the old one.
        Assert.Equal((10, 10), (b.Width, b.Height));
        Assert.Equal((30, 10), (a.Width, a.Height));
    }

    [Fact]
    public void TheLayoutPlacesWindowsAsWellAsSizingThem()
    {
        var (glk, _) = Library();
        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;
        var status = glk.OpenWindow(story, WindowMethod.Above | WindowMethod.Fixed, 2, WindowType.TextGrid, 2)!;
        var side = glk.OpenWindow(story, WindowMethod.Right | WindowMethod.Proportional, 25, WindowType.TextBuffer, 3)!;

        // [glk #window_arrangement] The status line sits at the top, the
        // story below it, and the side window takes the right quarter of
        // the story's space: the first child of a pair starts where the
        // pair does, and the second where the first ends.
        Assert.Equal((0, 0, 40, 2), (status.Left, status.Top, status.Width, status.Height));
        Assert.Equal((0, 2, 30, 8), (story.Left, story.Top, story.Width, story.Height));
        Assert.Equal((30, 2, 10, 8), (side.Left, side.Top, side.Width, side.Height));
        Assert.Equal((0, 2), (story.Parent!.Left, story.Parent.Top));

        // Closing the status line moves the rest up to the top.
        glk.CloseWindow(status);
        Assert.Equal((0, 0, 30, 10), (story.Left, story.Top, story.Width, story.Height));
        Assert.Equal((30, 0, 10, 10), (side.Left, side.Top, side.Width, side.Height));
    }

    [Fact]
    public void ClosingAChildGivesItsSpaceToTheSibling()
    {
        var (glk, _) = Library();
        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;
        var status = glk.OpenWindow(story, WindowMethod.Above | WindowMethod.Fixed, 2, WindowType.TextGrid, 2)!;
        glk.SetCurrentStream(null);

        var counts = glk.CloseWindow(status);

        // [glk op:window_close] The pair goes with the window, the
        // sibling becomes the root, and the counts come back.
        Assert.Same(story, glk.Root);
        Assert.Null(story.Parent);
        Assert.Equal((40, 10), (story.Width, story.Height));
        Assert.Equal(1, glk.Windows.Count);
        Assert.Equal(1, glk.Streams.Count);
        Assert.Equal((0u, 0u), counts);
    }

    [Fact]
    public void ClosingTheRootClosesEverything()
    {
        var (glk, display) = Library();
        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;
        glk.OpenWindow(story, WindowMethod.Above | WindowMethod.Fixed, 2, WindowType.TextGrid, 2);
        glk.SetCurrentStream(story.Stream);

        glk.CloseWindow(glk.Root!);

        Assert.Null(glk.Root);
        Assert.Equal(0, glk.Windows.Count);
        Assert.Equal(0, glk.Streams.Count);
        Assert.Null(glk.CurrentStream);
        Assert.Null(display.LastRoot);
    }

    [Fact]
    public void ClosingTheKeyWindowCollapsesTheSizedChild()
    {
        // [glk #window_opening] The specification's example: A split
        // with C above it at two rows, then C split with D to its right;
        // closing C leaves the pair above A with no key, so D gets no
        // height.
        var (glk, _) = Library();
        var a = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;
        var c = glk.OpenWindow(a, WindowMethod.Above | WindowMethod.Fixed, 2, WindowType.TextGrid, 3)!;
        var d = glk.OpenWindow(c, WindowMethod.Right | WindowMethod.Proportional, 50, WindowType.TextGrid, 4)!;
        Assert.Equal((20, 2), (d.Width, d.Height));

        glk.CloseWindow(c);

        var upper = Assert.IsType<PairWindow>(glk.Root);
        Assert.Null(upper.Key);
        Assert.Same(d, upper.First);
        Assert.Equal((40, 0), (d.Width, d.Height));
        Assert.Equal((40, 10), (a.Width, a.Height));

        // [glk #window_changing] A new key and size make it useful again.
        glk.SetArrangement(upper, WindowMethod.Above | WindowMethod.Fixed, 3, d);
        Assert.Equal((40, 3), (d.Width, d.Height));
        Assert.Equal((40, 7), (a.Width, a.Height));
    }

    [Fact]
    public void ArrangementCanResizeButNotRotate()
    {
        var (glk, _) = Library();
        var a = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;
        var c = glk.OpenWindow(a, WindowMethod.Above | WindowMethod.Fixed, 2, WindowType.TextGrid, 3)!;
        var pair = (PairWindow)glk.Root!;

        // [glk #window_changing] Below with the same key moves the
        // constraint to the lower child, A, measured in C's font.
        glk.SetArrangement(pair, WindowMethod.Below | WindowMethod.Fixed, 3, null);
        Assert.Same(a, pair.SizedChild);
        Assert.Equal((40, 3), (a.Width, a.Height));
        Assert.Equal((40, 7), (c.Width, c.Height));

        glk.SetArrangement(pair, WindowMethod.Above | WindowMethod.Proportional, 70, null);
        Assert.Equal((40, 7), (c.Width, c.Height));

        glk.SetArrangement(pair, WindowMethod.Left | WindowMethod.Fixed, 3, null);
        Assert.Contains(glk.Warnings, w => w.Contains("vertical", StringComparison.Ordinal));

        glk.SetArrangement(a, WindowMethod.Above | WindowMethod.Fixed, 3, null);
        Assert.Contains(glk.Warnings, w => w.Contains("not a pair", StringComparison.Ordinal));

        glk.SetArrangement(pair, WindowMethod.Above | WindowMethod.Fixed, 3, pair);
        Assert.Contains(glk.Warnings, w => w.Contains("key window", StringComparison.Ordinal));
    }

    [Fact]
    public void OpeningWithoutOrWithABadSplitFails()
    {
        var (glk, _) = Library();
        var a = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;

        // [glk #window_opening] Once a window exists a split is required;
        // graphics windows are not available at all.
        Assert.Null(glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 2));
        Assert.Null(glk.OpenWindow(a, WindowMethod.Above, 2, WindowType.TextBuffer, 2));
        Assert.Null(glk.OpenWindow(a, WindowMethod.Above | WindowMethod.Fixed, 2, WindowType.Graphics, 2));
        Assert.Equal(1, glk.Windows.Count);
        Assert.Equal(3, glk.Warnings.Count);
    }

    [Fact]
    public void IterationWalksEveryWindowOnceInCreationOrder()
    {
        var (glk, _) = Library();
        var a = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 11)!;
        var b = glk.OpenWindow(a, WindowMethod.Below | WindowMethod.Fixed, 2, WindowType.TextGrid, 22)!;

        // [glk #opaque_iteration] a, b, then the pair made for b.
        var seen = new List<(uint Id, uint Rock)>();
        for (var window = glk.Windows.Next(null); window is not null; window = glk.Windows.Next(window))
        {
            seen.Add((window.Id, window.Rock));
        }

        Assert.Equal([(a.Id, 11u), (b.Id, 22u), (glk.Root!.Id, 0u)], seen);
    }

    [Fact]
    public void TextGridsLayCharactersOutAndWrap()
    {
        var (glk, _) = Library(5, 2);
        var grid = (TextGridWindow)glk.OpenWindow(null, 0, 0, WindowType.TextGrid, 1)!;

        foreach (var character in "abcdefg")
        {
            grid.Stream.PutChar(character);
        }

        // [glk #window_textgrid] Left to right, top to bottom, wrapping
        // at the end of a row, and nothing past the last row.
        Assert.Equal("abcde", grid.Row(0));
        Assert.Equal("fg   ", grid.Row(1));
        Assert.Equal((2, 1), (grid.CursorX, grid.CursorY));

        grid.Stream.PutChar('\n');
        grid.Stream.PutChar('z');
        Assert.Equal("fg   ", grid.Row(1));

        // [glk op:window_move_cursor] Back into view, and past the end of
        // a row wraps on the next print.
        grid.MoveCursor(4, 0);
        grid.Stream.PutChar('x');
        grid.Stream.PutChar('y');
        Assert.Equal("abcdx", grid.Row(0));
        Assert.Equal("yg   ", grid.Row(1));

        grid.Stream.SetStyle(GlkStyle.Emphasized);
        grid.Stream.PutChar('e');
        Assert.Equal(GlkStyle.Emphasized, grid.StyleAt(1, 1));

        grid.Clear();
        Assert.Equal("     ", grid.Row(0));
        Assert.Equal((0, 0), (grid.CursorX, grid.CursorY));
        Assert.Equal(GlkStyle.Normal, grid.StyleAt(1, 1));
        Assert.Equal(12u, grid.Stream.WriteCount);
    }

    [Fact]
    public void TextGridsKeepTheirTopLeftWhenResized()
    {
        var (glk, display) = Library(5, 2);
        var grid = (TextGridWindow)glk.OpenWindow(null, 0, 0, WindowType.TextGrid, 1)!;
        foreach (var character in "abcdefghij")
        {
            grid.Stream.PutChar(character);
        }

        display.Width = 3;
        display.Height = 3;
        glk.Arrange();

        // [glk #window_textgrid] Smaller throws the right away; larger
        // fills the new bottom with blanks.
        Assert.Equal((3, 3), (grid.Width, grid.Height));
        Assert.Equal("abc", grid.Row(0));
        Assert.Equal("fgh", grid.Row(1));
        Assert.Equal("   ", grid.Row(2));
    }

    [Fact]
    public void TextBuffersPrintToTheDisplayWithTheirStyle()
    {
        var (glk, display) = Library();
        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;

        glk.SetCurrentStream(story.Stream);
        glk.PutChar('H');
        GlkLibrary.SetStyle(story.Stream, GlkStyle.Header);
        glk.PutChar(0x3B1);
        GlkLibrary.SetStyle(story.Stream, (GlkStyle)99);
        glk.PutChar('!');
        story.Clear();

        // [glk op:set_style] An unknown style is normal.
        Assert.Equal("Hα!", display.Text(story));
        Assert.Equal([GlkStyle.Normal, GlkStyle.Header, GlkStyle.Normal], display.Styles);
        Assert.Equal([story], display.Cleared);
        Assert.Equal(3u, story.Stream.WriteCount);
    }

    [Fact]
    public void EchoStreamsGetACopyOfTextAndStyles()
    {
        var (glk, display) = Library();
        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;
        var other = glk.OpenWindow(story, WindowMethod.Below | WindowMethod.Proportional, 50, WindowType.TextBuffer, 2)!;

        glk.SetEchoStream(story, other.Stream);
        GlkLibrary.SetStyle(story.Stream, GlkStyle.Alert);
        glk.PutChar(story.Stream, 'x');

        // [glk #echo_streams] The echo is one way, and includes styles.
        Assert.Equal("x", display.Text(story));
        Assert.Equal("x", display.Text(other));
        Assert.Equal(GlkStyle.Alert, other.Stream.Style);
        Assert.Equal(1u, other.Stream.WriteCount);

        // A loop is refused, and closing the echo window stops the echo.
        glk.SetEchoStream(other, story.Stream);
        Assert.Null(other.EchoStream);
        Assert.Contains(glk.Warnings, w => w.Contains("loop", StringComparison.Ordinal));

        glk.CloseWindow(other);
        Assert.Null(story.EchoStream);
    }

    [Fact]
    public void StyleHintsAreRememberedUntilCleared()
    {
        var (glk, _) = Library();

        glk.SetStyleHint(WindowType.AllTypes, GlkStyle.User1, StyleHint.Weight, 1);
        glk.SetStyleHint(WindowType.TextGrid, GlkStyle.User1, StyleHint.Weight, -1);

        // [glk #stream_style_hints] A type's own hint, or the hint for all
        // types, and none at all is not the same as zero.
        Assert.Equal(-1, glk.StyleHintFor(WindowType.TextGrid, GlkStyle.User1, StyleHint.Weight));
        Assert.Equal(1, glk.StyleHintFor(WindowType.TextBuffer, GlkStyle.User1, StyleHint.Weight));
        Assert.Null(glk.StyleHintFor(WindowType.TextBuffer, GlkStyle.User2, StyleHint.Weight));

        glk.ClearStyleHint(WindowType.AllTypes, GlkStyle.User1, StyleHint.Weight);
        Assert.Null(glk.StyleHintFor(WindowType.TextBuffer, GlkStyle.User1, StyleHint.Weight));
    }

    [Theory]
    [InlineData(GestaltSelector.Version, 0u, 0x00000706u)]
    [InlineData(GestaltSelector.Unicode, 0u, 1u)]
    [InlineData(GestaltSelector.CharOutput, 'a', 2u)]
    [InlineData(GestaltSelector.CharOutput, 0x3B1u, 2u)]
    [InlineData(GestaltSelector.CharOutput, '\n', 2u)]
    [InlineData(GestaltSelector.CharOutput, 9u, 0u)]
    [InlineData(GestaltSelector.CharOutput, 127u, 0u)]
    [InlineData(GestaltSelector.LineInput, 'a', 1u)]
    [InlineData(GestaltSelector.LineInput, 27u, 0u)]
    [InlineData(GestaltSelector.Graphics, 0u, 0u)]
    [InlineData(GestaltSelector.Sound, 0u, 0u)]
    [InlineData((GestaltSelector)0x1500, 0u, 0u)]
    public void GestaltAnswersForWhatIsBuilt(GestaltSelector selector, uint value, uint expected)
    {
        var (glk, _) = Library();
        Assert.Equal(expected, GlkLibrary.Gestalt((uint)selector, value, null));
    }

    [Fact]
    public void CharOutputReportsTheGlyphCount()
    {
        var (glk, _) = Library();
        var extra = new uint[1];

        // [glk #encoding_out] One glyph for a printable character, none
        // for one that cannot be printed.
        GlkLibrary.Gestalt((uint)GestaltSelector.CharOutput, 'a', extra);
        Assert.Equal(1u, extra[0]);
        GlkLibrary.Gestalt((uint)GestaltSelector.CharOutput, 7, extra);
        Assert.Equal(0u, extra[0]);
    }
}

using Rezrov.Glulx.Glk;
using Rezrov.Gui;

namespace Rezrov.Tests;

/// <summary>
/// [glk #window_textbuf] The text of a Glk text buffer window: the runs
/// it arrives as, where the lines break at a width, and the line the
/// player is typing.
/// </summary>
/// <remarks>
/// The font is invented here, which is the point of the measuring seam:
/// every ordinary character is ten wide and every line twenty tall, so
/// a width of 100 holds exactly ten characters and the wrapping can be
/// stated exactly rather than approximately. One style is made wider
/// than the rest, so that a test can tell whether the style was carried
/// into the measurement.
/// </remarks>
public class BufferTextTests
{
    [Fact]
    public void TextArrivesACharacterAtATimeAndComesOutAsLines()
    {
        var text = new BufferText(new Ruler());
        Print(text, "hello");

        var lines = text.Lines(100);

        Assert.Single(lines);
        Assert.Equal(["hello"], Words(lines[0]));
        Assert.Equal(5, text.Length);
        Assert.Equal(20, lines[0].Height);
    }

    [Fact]
    public void ALineBreakStartsANewLineWhereverItFalls()
    {
        var text = new BufferText(new Ruler());
        Print(text, "one\ntwo\n\nfour");

        var lines = text.Lines(1000);

        Assert.Equal(4, lines.Count);
        Assert.Equal(["one"], Words(lines[0]));
        Assert.Equal(["two"], Words(lines[1]));
        Assert.Empty(lines[2].Pieces);
        Assert.Equal(["four"], Words(lines[3]));
    }

    [Fact]
    public void TextWrapsBetweenWordsAtTheWidth()
    {
        // Ten characters to the line. "the quick " fills it exactly,
        // "brown" would overrun, so it starts the next line, and "fox"
        // still fits beside it at nine.
        var text = new BufferText(new Ruler());
        Print(text, "the quick brown fox");

        var lines = text.Lines(100);

        Assert.Equal(2, lines.Count);
        Assert.Equal(["the", " ", "quick", " "], Words(lines[0]));
        Assert.Equal(["brown", " ", "fox"], Words(lines[1]));
    }

    [Fact]
    public void AWordTooLongForALineIsBrokenRatherThanLost()
    {
        // Nothing can be done for a word wider than the whole window
        // but break it where it reaches the edge.
        var text = new BufferText(new Ruler());
        Print(text, "supercalifragilistic");

        var lines = text.Lines(50);

        Assert.Equal(4, lines.Count);
        Assert.Equal(["super"], Words(lines[0]));
        Assert.Equal(["calif"], Words(lines[1]));
        Assert.Equal("supercalifragilistic", string.Concat(lines.SelectMany(Words)));
    }

    [Fact]
    public void APieceKnowsWhereItStartsAndHowWideItIs()
    {
        var text = new BufferText(new Ruler());
        Print(text, "ab cd");

        var pieces = text.Lines(1000)[0].Pieces;

        Assert.Equal(3, pieces.Count);
        Assert.Equal((0.0, 20.0), (pieces[0].Left, pieces[0].Width));
        Assert.Equal((20.0, 10.0), (pieces[1].Left, pieces[1].Width));
        Assert.Equal((30.0, 20.0), (pieces[2].Left, pieces[2].Width));
    }

    [Fact]
    public void AStyleIsCarriedThroughToTheMeasurementAndThePiece()
    {
        // [glk #stream_style] The header style is twice as wide in this
        // font, so the same four characters wrap where plain ones would
        // not, and the piece says which style to draw it in.
        var text = new BufferText(new Ruler());
        Print(text, "ab", GlkStyle.Header);
        Print(text, "cd");

        var pieces = text.Lines(1000)[0].Pieces;

        Assert.Equal(2, pieces.Count);
        Assert.Equal(GlkStyle.Header, pieces[0].Style);
        Assert.Equal(40.0, pieces[0].Width);
        Assert.Equal(GlkStyle.Normal, pieces[1].Style);
        Assert.Equal((40.0, 20.0), (pieces[1].Left, pieces[1].Width));

        // A taller style makes the whole line as tall as itself.
        Assert.Equal(40.0, text.Lines(1000)[0].Height);
    }

    [Fact]
    public void ALinkIsCarriedThroughToThePiece()
    {
        // [glk #link_creating] Which link a piece belongs to is what a
        // display needs in order to report the one that was touched.
        var text = new BufferText(new Ruler());
        Print(text, "go ");
        Print(text, "north", GlkStyle.Normal, 7);
        Print(text, "!");

        var pieces = text.Lines(1000)[0].Pieces;

        Assert.Equal([0u, 0u, 7u, 0u], pieces.Select(p => p.Link).ToArray());
    }

    [Fact]
    public void TheLineBeingTypedIsShownAfterTheTextAndIsNotPartOfIt()
    {
        // [glk #line_events] Showing the line as it is typed is the
        // display's work, so it is drawn with the text but does not
        // become text: the length does not move and it can be taken
        // back a character at a time.
        var text = new BufferText(new Ruler());
        Print(text, ">");
        text.Pending = "loo";

        var pieces = text.Lines(1000)[0].Pieces;

        Assert.Equal([">", "loo"], Words(text.Lines(1000)[0]));
        Assert.Equal(GlkStyle.Input, pieces[1].Style);
        Assert.Equal(1, text.Length);

        text.Pending = "look";
        Assert.Equal([">", "look"], Words(text.Lines(1000)[0]));

        text.Pending = "";
        Assert.Equal([">"], Words(text.Lines(1000)[0]));
    }

    [Fact]
    public void TheSameWidthIsLaidOutOnceAndANewWidthWrapsAgain()
    {
        var text = new BufferText(new Ruler());
        Print(text, "the quick brown fox");

        var first = text.Lines(100);

        Assert.Same(first, text.Lines(100));
        Assert.Equal(2, first.Count);

        // Wider, and the whole of it fits on one line again.
        Assert.Single(text.Lines(1000));

        // And printing more makes the layout stale even at a width it
        // has already been asked about.
        Print(text, " ran");
        Assert.Equal(3, text.Lines(100).Count);
    }

    [Fact]
    public void TextSitsAtTheTopUntilThereIsMoreOfItThanFits()
    {
        // [glk #window_textbuf] Lines are twenty tall in this font, so a
        // window of a hundred holds five of them.
        var text = new BufferText(new Ruler());
        Print(text, "one\ntwo\nthree");

        // Three lines of text in a window of five: the last of them ends
        // sixty down, which leaves the space below it empty rather than
        // pushing the text against the bottom edge.
        Assert.Equal(60.0, text.Bottom(1000, 100));

        // Five lines exactly fill it, and the arithmetic agrees from
        // both directions.
        Print(text, "\nfour\nfive");
        Assert.Equal(100.0, text.Bottom(1000, 100));

        // More than fits, and the last line sits against the bottom
        // edge, whatever has run off the top. Anything larger here would
        // draw the newest line below the window, where it cannot be
        // read.
        Print(text, "\nsix\nseven");
        Assert.Equal(140.0, text.Height(1000));
        Assert.Equal(100.0, text.Bottom(1000, 100));
    }

    [Fact]
    public void ClearingThrowsEverythingAway()
    {
        var text = new BufferText(new Ruler());
        Print(text, "the quick brown fox");
        text.Clear();

        Assert.Equal(0, text.Length);
        Assert.Single(text.Lines(100));
        Assert.Empty(text.Lines(100)[0].Pieces);
        Assert.Equal(20.0, text.Height(100));
    }

    private static void Print(BufferText text, string what, GlkStyle style = GlkStyle.Normal, uint link = 0)
    {
        foreach (var character in what)
        {
            text.Put(character, style, link);
        }
    }

    private static IEnumerable<string> Words(Line line) => line.Pieces.Select(p => p.Text);

    /// <summary>
    /// A font of exact numbers: ten wide and twenty tall, except the
    /// header style, which is twice both.
    /// </summary>
    private sealed class Ruler : IGlyphs
    {
        public double CellWidth => 10;

        public double CellHeight => 20;

        public double Width(string text, GlkStyle style) =>
            text.Length * (style == GlkStyle.Header ? 20 : 10);

        public double LineHeight(GlkStyle style) => style == GlkStyle.Header ? 40 : 20;
    }
}

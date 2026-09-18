using Rezrov.Core.Graphics;
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
    /// <summary>[glk op:image_draw] The picture's own size.</summary>
    private static readonly ImageSizing Natural =
        new(ImageRule.WidthOrig | ImageRule.HeightOrig, 0, 0, 0);

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
    public void ScrollingBackBringsWhatWentOffTheTopIntoView()
    {
        // Seven lines of twenty in a window of a hundred: five fit, and
        // the other forty pixels have gone off the top.
        var text = Lines(7);

        Assert.Equal(140.0, text.Height(1000));
        Assert.Equal(40.0, text.Furthest(1000, 100));
        Assert.Equal(100.0, text.Bottom(1000, 100));

        // Scrolling back moves the whole of it down, which is what
        // brings the earlier lines into view.
        text.ScrollBy(20, 1000, 100);
        Assert.Equal(120.0, text.Bottom(1000, 100));

        // And it stops once the first line is showing, however much
        // further it is asked to go.
        text.ScrollBy(500, 1000, 100);
        Assert.Equal(40.0, text.Scroll);
        Assert.Equal(140.0, text.Bottom(1000, 100));

        // Forward again, and it stops at the end of the text.
        text.ScrollBy(-500, 1000, 100);
        Assert.Equal(0.0, text.Scroll);
        Assert.Equal(100.0, text.Bottom(1000, 100));
    }

    [Fact]
    public void TextThatAllFitsCannotBeScrolled()
    {
        var text = Lines(3);

        Assert.Equal(0.0, text.Furthest(1000, 100));

        text.ScrollBy(50, 1000, 100);

        Assert.Equal(0.0, text.Scroll);
        Assert.Equal(60.0, text.Bottom(1000, 100));
    }

    [Fact]
    public void AnythingNewToReadBringsTheEndBackIntoView()
    {
        // Scrolled back, and then the game says something: what it said
        // is what the player wants to see, so the view follows it.
        var text = Lines(7);
        text.ScrollBy(40, 1000, 100);
        Assert.Equal(40.0, text.Scroll);

        Print(text, "and then");
        Assert.Equal(0.0, text.Scroll);

        // So does typing, which happens at the end of the text.
        text.ScrollBy(40, 1000, 100);
        text.Pending = "l";
        Assert.Equal(0.0, text.Scroll);

        // And so does asking outright.
        text.ScrollBy(40, 1000, 100);
        text.ScrollToEnd();
        Assert.Equal(0.0, text.Scroll);
    }

    [Fact]
    public void AWindowMadeTallerDoesNotLeaveTheTextScrolledPastItsStart()
    {
        // Scrolled all the way back in a short window, then the window
        // is made tall enough to hold everything: the scroll that was
        // reasonable before would now show blank above the first line.
        var text = Lines(7);
        text.ScrollBy(500, 1000, 100);

        Assert.Equal(40.0, text.Scroll);
        Assert.Equal(140.0, text.Bottom(1000, 200));
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

    [Fact]
    public void AnInlinePictureTakesItsPlaceAmongTheWords()
    {
        var text = new BufferText(new Ruler());
        Print(text, "a");
        Assert.True(text.Draw(Picture(30, 50), ImageAlign.InlineUp, Natural, 0));
        Print(text, "b");

        var line = text.Lines(1000)[0];

        // The picture is thirty wide, so the letter after it starts
        // forty along.
        Assert.Equal(["a", "b"], Words(line));
        Assert.Equal(40.0, line.Pieces[1].Left);

        // [glk #graphics_textbuf] Its bottom edge is on the baseline,
        // so it reaches fifty above it and the line grows to hold it:
        // fifty above the baseline and the font's four below.
        var inset = Assert.Single(line.Images);
        Assert.Equal((10.0, 30.0, 50.0), (inset.Left, inset.Width, inset.Height));
        Assert.Equal((50.0, 54.0), (line.Baseline, line.Height));
        Assert.Equal(0.0, inset.Top);
    }

    [Fact]
    public void EachInlineAlignmentSitsWhereTheSpecificationSaysItDoes()
    {
        // [glk #graphics_textbuf] The font's baseline is sixteen down
        // and a line of it twenty tall. A picture ten tall standing on
        // the baseline starts six down; hanging from the top of the
        // line it starts at the top; centered between the two it starts
        // three down.
        Assert.Equal(6.0, Sits(ImageAlign.InlineUp));
        Assert.Equal(0.0, Sits(ImageAlign.InlineDown));
        Assert.Equal(3.0, Sits(ImageAlign.InlineCenter));

        static double Sits(ImageAlign align)
        {
            var text = new BufferText(new Ruler());
            Print(text, "x");
            text.Draw(Picture(10, 10), align, Natural, 0);
            return text.Lines(1000)[0].Images[0].Top;
        }
    }

    [Fact]
    public void AMarginPictureStandsTheTextAsideUntilItHasPassed()
    {
        // [glk #graphics_textbuf] A picture thirty wide and fifty tall
        // in the left margin, in a window a hundred across: the lines
        // beside it start thirty along and hold seven characters where
        // they would otherwise hold ten.
        var text = new BufferText(new Ruler());
        Assert.True(text.Draw(Picture(30, 50), ImageAlign.MarginLeft, Natural, 0));
        Print(text, "one two three four");

        var lines = text.Lines(100);

        Assert.Equal(3, lines.Count);
        Assert.Equal([30.0, 30.0, 30.0], lines.Select(l => l.Pieces[0].Left).ToArray());
        Assert.Equal(["one", " ", "two", " "], Words(lines[0]));

        // The picture goes in every line it reaches down through, since
        // painting starts at the bottom of the window and works up, and
        // one kept only in the line it began at would be lost as soon
        // as that line had gone off the top.
        Assert.Equal([0.0, -20.0, -40.0], lines.Select(l => l.Images[0].Top).ToArray());
        Assert.Equal((0.0, 30.0, 50.0), Shape(lines[0].Images[0]));

        // And the line below it has the whole width back.
        Print(text, " five");
        lines = text.Lines(100);

        Assert.Equal(4, lines.Count);
        Assert.Equal(0.0, lines[3].Pieces[0].Left);
        Assert.Empty(lines[3].Images);
    }

    [Fact]
    public void APictureInTheRightMarginTakesTheRoomFromThatEdge()
    {
        var text = new BufferText(new Ruler());
        Assert.True(text.Draw(Picture(30, 50), ImageAlign.MarginRight, Natural, 0));
        Print(text, "one two three");

        var lines = text.Lines(100);

        // [glk #graphics_textbuf] The text still starts at the left
        // edge and runs out seven characters along instead of ten.
        Assert.Equal(0.0, lines[0].Pieces[0].Left);
        Assert.Equal(["one", " ", "two", " "], Words(lines[0]));
        Assert.Equal((70.0, 30.0, 50.0), Shape(lines[0].Images[0]));
    }

    [Fact]
    public void AMarginPictureIsOnlyPlacedAtTheStartOfALine()
    {
        var text = new BufferText(new Ruler());
        Print(text, "words");

        // [glk #graphics_textbuf] No picture appears at all, which is
        // what the specification says becomes of one asked for where
        // text has already been printed on the line.
        Assert.False(text.Draw(Picture(30, 50), ImageAlign.MarginLeft, Natural, 0));
        Assert.Empty(text.Lines(1000)[0].Images);

        // A line ending puts the text back at the start of a line, and
        // two pictures may share a margin, so the first of them leaves
        // the line still unstarted and the second goes beside it. An
        // inline picture counts as text, and the margin picture after
        // it is refused.
        Print(text, "\n");
        Assert.True(text.Draw(Picture(30, 50), ImageAlign.MarginLeft, Natural, 0));
        Assert.True(text.Draw(Picture(10, 10), ImageAlign.MarginLeft, Natural, 0));
        Assert.True(text.Draw(Picture(10, 10), ImageAlign.InlineUp, Natural, 0));
        Assert.False(text.Draw(Picture(10, 10), ImageAlign.MarginRight, Natural, 0));

        var second = text.Lines(1000)[1];
        Assert.Equal([0.0, 30.0, 40.0], second.Images.Select(i => i.Left).ToArray());
    }

    [Fact]
    public void AFlowBreakTakesTheTextDownPastTheMarginPictures()
    {
        var text = new BufferText(new Ruler());
        text.Draw(Picture(30, 50), ImageAlign.MarginLeft, Natural, 0);
        Print(text, "one\n");
        text.FlowBreak();
        Print(text, "two");

        var lines = text.Lines(100);

        // [glk op:window_flow_break] One line of text beside the
        // picture, then the gap down to its bottom edge as a line of
        // its own, then the text again at the left edge.
        Assert.Equal(3, lines.Count);
        Assert.Equal(["one"], Words(lines[0]));
        Assert.Equal(30.0, lines[0].Pieces[0].Left);
        Assert.Empty(lines[1].Pieces);
        Assert.Equal(30.0, lines[1].Height);
        Assert.Equal(["two"], Words(lines[2]));
        Assert.Equal(0.0, lines[2].Pieces[0].Left);

        // [glk #graphics_textbuf] And where the text is beside no
        // picture at all it does nothing, which is also what the
        // specification says.
        var plain = new BufferText(new Ruler());
        Print(plain, "one\n");
        plain.FlowBreak();
        Print(plain, "two");

        Assert.Equal(2, plain.Lines(100).Count);
    }

    [Fact]
    public void ATallMarginPictureLeavesRoomBelowTheLastLine()
    {
        // A picture fifty tall beside one line of twenty: the space it
        // still needs is part of how tall the text is, or it would hang
        // below the window where it could not be seen.
        var text = new BufferText(new Ruler());
        text.Draw(Picture(30, 50), ImageAlign.MarginLeft, Natural, 0);
        Print(text, "one");

        Assert.Equal(50.0, text.Height(100));
        Assert.Equal(2, text.Lines(100).Count);
        Assert.Equal(30.0, text.Lines(100)[1].Height);
    }

    [Fact]
    public void APictureMeasuredAgainstTheWindowIsMeasuredAgainWhenItChanges()
    {
        // [glk #graphics_textbuf] Half the window's width, keeping the
        // picture's own shape, which is a different size in a window of
        // a different size. This is why the rules are kept rather than
        // the answer.
        var text = new BufferText(new Ruler());
        var half = new ImageSizing(ImageRule.WidthRatio | ImageRule.AspectRatio, 0x8000, 0x10000, 0);
        text.Draw(Picture(40, 20), ImageAlign.InlineUp, half, 0);

        Assert.Equal((100.0, 50.0), Grown(text.Lines(200)[0].Images[0]));
        Assert.Equal((200.0, 100.0), Grown(text.Lines(400)[0].Images[0]));

        static (double Width, double Height) Grown(Inset inset) => (inset.Width, inset.Height);
    }

    [Fact]
    public void AnIndentedStyleSetsItsLinesInFromTheEdge()
    {
        // [glk #stream_style_hints] The block quote is set in twenty
        // and its first line ten further again, so the paragraph starts
        // at thirty and every line after it at twenty.
        var text = new BufferText(new Ruler());
        Print(text, "one two three four", GlkStyle.BlockQuote);

        var lines = text.Lines(100);

        Assert.Equal(3, lines.Count);
        Assert.Equal([30.0, 20.0, 20.0], lines.Select(l => l.Pieces[0].Left).ToArray());

        // A line that is here because the one above it ran out of room
        // is a continuation, so the paragraph indentation comes back
        // only after a line ending of the game's own.
        Print(text, "\nfive");
        Assert.Equal(0.0, text.Lines(100)[3].Pieces[0].Left);
    }

    [Fact]
    public void ACenteredStyleSitsBetweenTheEdgesAndAFlushOneAgainstTheFar()
    {
        // Three characters are thirty wide in a window of a hundred, so
        // centering leaves thirty-five on each side and setting it
        // against the far edge leaves seventy before it.
        var centered = new BufferText(new Ruler());
        Print(centered, "abc", GlkStyle.Note);
        Assert.Equal(35.0, centered.Lines(100)[0].Pieces[0].Left);

        var flush = new BufferText(new Ruler());
        Print(flush, "abc", GlkStyle.Alert);
        Assert.Equal(70.0, flush.Lines(100)[0].Pieces[0].Left);

        // [glk #stream_style_hints] The space a line wrapped after is
        // not part of what is being moved, or a centered line would sit
        // half a space to the left of where it belongs.
        var wrapped = new BufferText(new Ruler());
        Print(wrapped, "abc abcdefghij", GlkStyle.Note);
        Assert.Equal(35.0, wrapped.Lines(100)[0].Pieces[0].Left);
    }

    [Fact]
    public void TheLinkUnderAPointIsTheOneThatWasSelected()
    {
        // [glk #link_events] Two lines, the second of which is a link.
        // The text sits at the top of the window while it fits, so the
        // first line is the top twenty pixels and the second the twenty
        // below it.
        var text = new BufferText(new Ruler());
        Print(text, "plain\n");
        Print(text, "go north", GlkStyle.Normal, 7);

        Assert.Equal(0u, text.LinkAt(10, 10, 100, 60));
        Assert.Equal(7u, text.LinkAt(10, 30, 100, 60));

        // Past the end of the words on a line, and past the text
        // altogether, there is no link to select.
        Assert.Equal(0u, text.LinkAt(95, 30, 100, 60));
        Assert.Equal(0u, text.LinkAt(10, 55, 100, 60));
    }

    [Fact]
    public void APictureTakesTheLinkOfTheTextAroundIt()
    {
        // [glk #link_creating] Which the specification is explicit
        // about, margin pictures included, so a player can select a
        // picture in a link as readily as the words in one.
        var text = new BufferText(new Ruler());
        text.Draw(Picture(30, 50), ImageAlign.MarginLeft, Natural, 9);
        Print(text, "beside it");

        Assert.Equal(9u, text.Lines(100)[0].Images[0].Link);
        Assert.Equal(9u, text.LinkAt(10, 10, 100, 200));

        // And the words beside it carry no link of their own.
        Assert.Equal(0u, text.LinkAt(40, 10, 100, 200));

        // The same in the right margin, which is where the one game in
        // the corpus that puts a picture in a link puts it: the picture
        // is thirty wide against the far edge of a hundred, so it holds
        // the last thirty pixels of every line it reaches down through.
        var right = new BufferText(new Ruler());
        right.Draw(Picture(30, 50), ImageAlign.MarginRight, Natural, 9);
        Print(right, "beside it");

        Assert.Equal(9u, right.LinkAt(80, 10, 100, 200));
        Assert.Equal(9u, right.LinkAt(80, 30, 100, 200));
        Assert.Equal(0u, right.LinkAt(10, 10, 100, 200));
    }

    private static (double Left, double Width, double Height) Shape(Inset inset) =>
        (inset.Left, inset.Width, inset.Height);

    /// <summary>A picture of the given size, all of it opaque.</summary>
    private static Pixels Picture(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        Array.Fill(rgba, (byte)255);
        return new Pixels(width, height, rgba);
    }

    /// <summary>A buffer of so many lines, one word to each.</summary>
    private static BufferText Lines(int count)
    {
        var text = new BufferText(new Ruler());
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                text.Put('\n', GlkStyle.Normal, 0);
            }

            Print(text, $"line{i}");
        }

        return text;
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
    /// <remarks>
    /// [glk #stream_style_hints] Three more styles carry a look of
    /// their own, so that a test can state exactly what the layout does
    /// with one: the block quote is set in two characters with its
    /// first line one further, the note is centered, and the alert is
    /// set against the far edge.
    /// </remarks>
    private sealed class Ruler : IGlyphs
    {
        public double CellWidth => 10;

        public double CellHeight => 20;

        public double Width(string text, GlkStyle style) =>
            text.Length * (style == GlkStyle.Header ? 20 : 10);

        public double LineHeight(GlkStyle style) => style == GlkStyle.Header ? 40 : 20;

        public GlkAppearance Look(GlkStyle style) => new(
            Indentation: style == GlkStyle.BlockQuote ? 20 : 0,
            ParaIndentation: style == GlkStyle.BlockQuote ? 10 : 0,
            Justification: style switch
            {
                GlkStyle.Note => Justification.Centered,
                GlkStyle.Alert => Justification.RightFlush,
                _ => Justification.LeftFlush,
            },
            Size: LineHeight(style),
            Weight: 0,
            Oblique: false,
            Proportional: true,
            TextColor: 0x00000000,
            BackColor: 0x00FFFFFF,
            Reverse: false);
    }
}

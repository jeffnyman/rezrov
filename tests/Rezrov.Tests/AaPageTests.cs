using Rezrov.AaMachine;
using Rezrov.Core.Graphics;
using Rezrov.Gui;

namespace Rezrov.Tests;

/// <summary>
/// [aam output] An Aa-machine story laid out on a page: where the
/// lines break, how far apart the paragraphs sit, and where the boxes
/// a style sheet asks for come to rest.
/// </summary>
/// <remarks>
/// The face is invented here, which is the point of the measuring
/// seam: every character is exactly as wide as the text is tall, so a
/// measurement in ems, a measurement in characters and a count of
/// letters are all the same number and the layout can be stated
/// exactly rather than approximately. Ordinary text is ten tall, so a
/// width of a hundred holds ten characters and a line takes thirteen
/// and a half.
/// </remarks>
public class AaPageTests
{
    private const double Size = 10;
    private const double LineHeight = Size * 1.35;

    [Fact]
    public void TextComesOutAsLinesWithTheirPlaceOnThePage()
    {
        var text = Plain();
        text.Put("hello");

        var page = text.Lay(100);
        var line = Assert.Single(page.Lines);

        Assert.Equal(0, line.Top);
        Assert.Equal(LineHeight, line.Height);
        Assert.Equal(LineHeight, page.Height);

        // The line the text stands on sits below the top of the line
        // by what the face reaches up plus its share of the room left
        // over, which is what makes a line of text look evenly spaced.
        Assert.Equal((Size * 0.8) + ((LineHeight - Size) / 2), line.Baseline);

        var piece = Assert.Single(line.Pieces);

        Assert.Equal("hello", piece.Words);
        Assert.Equal(0, piece.Left);
        Assert.Equal(50, piece.Width);
    }

    [Fact]
    public void TextWrapsBetweenWordsAtTheWidthOfTheBox()
    {
        // Ten characters to the line. "the quick " fills it exactly,
        // "brown" would overrun, so it starts the next line, and "fox"
        // still fits beside it at nine.
        var text = Plain();
        text.Put("the quick brown fox");

        var page = text.Lay(100);

        Assert.Equal(2, page.Lines.Count);
        Assert.Equal(["the", " ", "quick", " "], Words(page.Lines[0]));
        Assert.Equal(["brown", " ", "fox"], Words(page.Lines[1]));
        Assert.Equal(LineHeight, page.Lines[1].Top);
    }

    [Fact]
    public void AWordTooLongForALineIsBrokenRatherThanLost()
    {
        var text = Plain();
        text.Put("supercalifragilistic");

        var page = text.Lay(50);

        Assert.Equal(4, page.Lines.Count);
        Assert.Equal("supercalifragilistic", string.Concat(page.Lines.SelectMany(Words)));
    }

    [Fact]
    public void AParagraphIsSetOffFromTheTextAboveItAndFromNothingElse()
    {
        // [aam output] A paragraph that follows text is set off from it
        // by a line's worth of space, which is what the reference
        // interpreter asks its browser for.
        var text = Plain();
        text.Put("one");
        text.EndParagraph();
        text.Put("two");

        var page = text.Lay(100);

        Assert.Equal(2, page.Lines.Count);
        Assert.Equal(0, page.Lines[0].Top);
        Assert.Equal(LineHeight + Size, page.Lines[1].Top);

        // A paragraph break before anything has been printed sets
        // nothing off from anything, so the first line is still at the
        // top of the page.
        var second = Plain();
        second.EndParagraph();
        second.EndParagraph();
        second.Put("one");

        Assert.Equal(0, Assert.Single(second.Lay(100).Lines).Top);
    }

    [Fact]
    public void ALineBreakEndsTheLineAndTwoOfThemLeaveOneBlank()
    {
        var text = Plain();
        text.Put("one");
        text.Newline();
        text.Newline();
        text.Put("two");

        var page = text.Lay(100);

        Assert.Equal(3, page.Lines.Count);
        Assert.Equal(["one"], Words(page.Lines[0]));
        Assert.Empty(page.Lines[1].Pieces);
        Assert.Equal(["two"], Words(page.Lines[2]));

        // The blank line is as tall as a line, and the lines below it
        // are not set off by anything further: a line break is not a
        // paragraph break.
        Assert.Equal(LineHeight, page.Lines[1].Top);
        Assert.Equal(LineHeight * 2, page.Lines[2].Top);
    }

    [Fact]
    public void APictureSitsInTheRunOfTheTextAndIsBroughtDownToFit()
    {
        var text = Plain();
        text.Put("see ");
        text.Draw(Picture(40, 20), "a picture");

        var line = Assert.Single(text.Lay(100).Lines);
        var inset = Assert.Single(line.Pictures);

        Assert.Equal(40, inset.Left);
        Assert.Equal(40, inset.Width);
        Assert.Equal(20, inset.Height);

        // It stands on the line the text stands on, so its bottom and
        // the bottom of the letters beside it are the same.
        Assert.Equal(line.Baseline, inset.Top + inset.Height);

        // A picture wider than the box it is in is brought down rather
        // than allowed to run off the edge, keeping its shape.
        var wide = Plain();
        wide.Draw(Picture(200, 100), "a wide picture");

        var big = Assert.Single(Assert.Single(wide.Lay(100).Lines).Pictures);

        Assert.Equal(100, big.Width);
        Assert.Equal(50, big.Height);
    }

    [Fact]
    public void AResourceWithNoPictureShowsTheWordsTheStoryCarries()
    {
        var text = Plain();
        text.Draw(null, "four coloured bands");

        var page = text.Lay(400);

        Assert.Empty(page.Lines.SelectMany(line => line.Pictures));
        Assert.Contains("[four coloured bands]", string.Concat(page.Lines.SelectMany(Words)), StringComparison.Ordinal);
    }

    [Fact]
    public void ALinkIsFoundAgainByWhereItWasDrawn()
    {
        var text = Plain();
        text.Put("take the ");
        text.EnterLink(7);
        text.Put("lamp");
        text.LeaveLink();

        var page = text.Lay(200);
        var line = Assert.Single(page.Lines);

        Assert.Equal([0, 7], line.Pieces.Select(piece => piece.Link).Distinct());

        // The page is as tall as its one line, so it sits at the top
        // of a window of any height and can be pointed at there.
        Assert.Equal(7, text.LinkAt(95, 5, 200, 100));
        Assert.Equal(0, text.LinkAt(5, 5, 200, 100));
        Assert.Equal(0, text.LinkAt(95, 50, 200, 100));
    }

    [Fact]
    public void ADivBeginsALineOfItsOwnAndCarriesItsMarginsInEms()
    {
        // tethered's act titles ask for a margin of a line and a half
        // above and half a line below, and the halves that a grid of
        // characters had to drop are kept here.
        var styles = Story("tethered.aastory");
        var title = Enumerable.Range(0, styles.Count).Single(styles.IsUppercase);

        var text = Plain(styles);
        text.Put("before");
        text.EnterDiv(title);
        text.Put("act one");
        text.LeaveDiv();
        text.Put("after");

        var page = text.Lay(400);

        Assert.Equal(3, page.Lines.Count);

        // The title is set in text half again as large, so its line is
        // half again as tall, and it is shouted and spread out.
        var banner = page.Lines[1];
        var shouted = banner.Pieces[0];

        Assert.Equal("ACT ONE", string.Concat(Words(banner)));
        Assert.Equal(Size * 1.5, shouted.Look.Size);

        // Its letters stand a full line apart, which is a line of its
        // own text rather than of ordinary text.
        Assert.Equal(Size * 1.5, shouted.Look.Spacing);
        Assert.Equal(LineHeight * 1.5, banner.Height);

        // A margin of a line and a half, measured in its own text
        // rather than in the text above it, which is what an em is.
        Assert.Equal(LineHeight + (Size * 1.5 * 1.5), banner.Top);

        // And it is centered in the width, which it asked for all of.
        Assert.True(shouted.Left > 50, $"the title starts at {shouted.Left}, which is not centered");
    }

    [Fact]
    public void TwoMarginsThatMeetAreOneGapRatherThanTwo()
    {
        // The story's room headings ask for a third of a line above
        // them, and the block they follow asks for two lines below.
        // CSS runs two margins that meet into the wider of the two,
        // and a reader who saw three lines would know it had not.
        var styles = Story("picture-test.aastory");
        var named = Names(styles);

        var text = Plain(styles);
        text.EnterDiv(named["initial-spacer"]);
        text.Put("above");
        text.LeaveDiv();
        text.EnterDiv(named["roomheader"]);
        text.Put("Foyer");
        text.LeaveDiv();

        var page = text.Lay(400);

        Assert.Equal(2, page.Lines.Count);
        Assert.Equal(LineHeight + (Size * 2), page.Lines[1].Top);
    }

    [Fact]
    public void ABoxAsksForAWidthAndGetsThePageToItselfInTheMiddleOfIt()
    {
        // pas-de-deux sets one block thirty characters wide with a
        // line around it, and asks for the room left over to be shared
        // between its two sides.
        var styles = Story("pas-de-deux.aastory");
        var framed = Enumerable.Range(0, styles.Count).Single(i => styles.Border(i).IsDrawn);

        var text = Plain(styles);
        text.EnterDiv(framed);
        text.Put("a note");
        text.LeaveDiv();

        var page = text.Lay(400);
        var frame = Assert.Single(page.Frames);

        // Thirty characters, a strip of padding either side, and a
        // line a pixel thick around the whole.
        Assert.Equal(1, frame.Border);
        Assert.Equal(0xFF888888, frame.BorderColor);
        Assert.Equal((30 * Size) + (2 * Size * 0.2) + 2, frame.Width);

        // What is left over is halved, so the two sides of it are as
        // far from the edges of the page as each other.
        Assert.Equal(400 - frame.Width - frame.Left, frame.Left, 6);

        // And its text begins inside the line and the padding.
        var piece = Assert.Single(page.Lines).Pieces[0];

        Assert.Equal(frame.Left + 1 + (Size * 0.2), piece.Left, 6);
    }

    [Fact]
    public void ABlockSetAsideLeavesTheTextBesideItRoomToRun()
    {
        // This story's status classes set a column against each edge,
        // one twelve characters wide and one ten.
        var styles = Conformance("codepoints");
        var named = Names(styles);

        var text = Plain(styles);
        text.EnterDiv(named["rightcol"]);
        text.Put("score");
        text.LeaveDiv();
        text.Put("a room name that runs on and on and on");

        var page = text.Lay(400);

        // The column went against the right edge at the width it asked
        // for, and the text beside it stops where the column begins.
        var aside = page.Lines.Single(line => Words(line).Contains("score"));

        Assert.Equal(400 - (10 * Size), Assert.Single(aside.Pieces).Left);

        var beside = page.Lines.First(line => Words(line).Contains("a"));

        Assert.All(
            beside.Pieces,
            piece => Assert.True(
                piece.Left + piece.Width <= 400 - (10 * Size) + 0.01,
                $"a piece reaches {piece.Left + piece.Width}, which is under the column"));

        // And the text still begins at the left edge rather than being
        // pushed aside by a column that is not in its way.
        Assert.Equal(0, beside.Pieces[0].Left);
    }

    [Fact]
    public void ABlockSetAsideAtTheLeftPushesTheTextAcross()
    {
        var styles = Conformance("codepoints");
        var named = Names(styles);

        var text = Plain(styles);
        text.EnterDiv(named["leftcol"]);
        text.Put("aside");
        text.LeaveDiv();
        text.Put("beside");

        var page = text.Lay(400);
        var beside = page.Lines.Single(line => Words(line).Contains("beside"));

        Assert.Equal(12 * Size, beside.Pieces[0].Left);
    }

    [Fact]
    public void ABoxWithABackgroundIsPaintedBehindItsText()
    {
        // D'ARKUN sets its quoted passages in silver with rounded
        // corners, which is a box to paint rather than a way to set
        // the text.
        var styles = Story("D-ARKUN.aastory");
        var quoted = Enumerable.Range(0, styles.Count)
            .Single(i => styles.Families(i).Contains("Shadows"));

        var text = Plain(styles);
        text.EnterDiv(quoted);
        text.Put("a memory");
        text.LeaveDiv();

        var page = text.Lay(400);
        var frame = Assert.Single(page.Frames);

        Assert.Equal(0xFFC0C0C0, frame.Background);
        Assert.Equal(5, frame.Radius);

        // [aam story] A margin of one em on every side, where the em
        // is the box's own text and not the text outside it: the class
        // asks for text a quarter again as large, so its margin is a
        // quarter wider too. That is what CSS means by an em and it is
        // easy to get wrong in the other direction.
        Assert.Equal(Size * 1.25, frame.Left);
        Assert.Equal(Size * 1.25, frame.Top);

        // The text inside it carries no background of its own, since
        // the box is what is painted.
        Assert.Equal<uint>(0, Assert.Single(page.Lines).Pieces[0].Look.Paper);
    }

    [Fact]
    public void AStatusClassIsAtLeastAsTallAsItAsked()
    {
        var styles = Conformance("codepoints");
        var named = Names(styles);

        var text = Plain(styles);
        text.EnterDiv(named["status2"]);
        text.Put("hi");
        text.LeaveDiv();

        // Two lines of room for one line of text, which is a status
        // area keeping the space it reserved.
        Assert.Equal(Size * 2, text.Lay(400).Height);
    }

    [Fact]
    public void AClassThatIsNotShownIsNotLaidOutAtAll()
    {
        var styles = Story("picture-test.aastory");

        var text = Plain(styles);
        text.Put("shown");

        // Nothing in the corpus hides itself, so the check is that a
        // class which does not ask to be hidden is not hidden.
        Assert.NotEmpty(text.Lay(400).Lines);
    }

    [Fact]
    public void LayingOutAgainAtTheSameWidthGivesBackTheSamePage()
    {
        var text = Plain();
        text.Put("hello");

        Assert.Same(text.Lay(100), text.Lay(100));
        Assert.NotSame(text.Lay(100), text.Lay(200));
    }

    [Fact]
    public void ClearingKeepsTheDivsTheStoryIsStillInside()
    {
        var styles = Story("picture-test.aastory");
        var named = Names(styles);

        var text = Plain(styles);
        text.EnterDiv(named["status"]);
        text.Put("gone");
        text.Clear();
        text.Put("kept");

        var page = text.Lay(400);

        Assert.Equal(["kept"], page.Lines.SelectMany(Words).Where(word => word != " "));

        // The div is still open, so what follows is still in it, which
        // is what the machine counts on: it clears the screen without
        // leaving the divs it entered.
        Assert.Equal(named["status"], text.Innermost);
    }

    private static AaText Plain(AaStyles? styles = null) =>
        new(
            new Ruler(),
            new AaSheet(
                styles ?? AaStyles.None,
                new Ruler(),
                new AaLook(string.Empty, Size, false, false, 0, AaTheme.Ink, 0)));

    private static AaStyles Story(string name)
    {
        var path = Corpus.AaStoryFile(name);
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        return AaStory.Read(File.ReadAllBytes(path!)).Styles;
    }

    private static AaStyles Conformance(string name)
    {
        var path = Corpus.AaConformanceFile(name);
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        return AaStory.Read(File.ReadAllBytes(path!)).Styles;
    }

    private static Dictionary<string, int> Names(AaStyles styles) =>
        Enumerable.Range(0, styles.Count)
            .Where(i => styles.Name(i) is not null)
            .ToDictionary(i => styles.Name(i)!, i => i, StringComparer.Ordinal);

    private static List<string> Words(AaLine line) =>
        line.Pieces.Select(piece => piece.Words).ToList();

    private static Pixels Picture(int width, int height) =>
        new(width, height, new byte[width * height * 4]);

    /// <summary>
    /// A face where every character is exactly as wide as the text is
    /// tall, so that ems, characters and letters are all one number.
    /// </summary>
    private sealed class Ruler : IAaGlyphs
    {
        public double Width(string text, AaLook look)
        {
            ArgumentNullException.ThrowIfNull(text);

            return text.Length * look.Size;
        }

        public double Ascent(AaLook look) => look.Size * 0.8;

        public double Descent(AaLook look) => look.Size * 0.2;

        public double CharacterWidth(AaLook look) => look.Size;
    }
}

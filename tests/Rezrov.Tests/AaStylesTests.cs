using Rezrov.AaMachine;

namespace Rezrov.Tests;

/// <summary>
/// [aam story] The LOOK chunk, read for the part of it a grid of
/// characters can honor.
/// </summary>
public class AaStylesTests
{
    [Fact]
    public void TheStyleSheetSaysWhichClassIsWhich()
    {
        var styles = Story("picture-test.aastory");

        // This story is the one whose classes carry the names the
        // Dialog source gave them, so it can be read against its own
        // source rather than guessed at.
        var named = Names(styles);

        Assert.True(styles.IsBold(named["bold"]));
        Assert.True(styles.IsItalic(named["meta"]));
        Assert.True(styles.IsBold(named["title"]));

        // [aam story] A status area is a div like any other, and how
        // tall it is is a height in its style class.
        Assert.Equal(1, styles.Ems(named["status"], "height", 0));

        // And a score is set beside the status text rather than in it,
        // in a width the story gives in characters, which is the one
        // measurement a terminal can take literally.
        Assert.True(styles.FloatsRight(named["score"]));
        Assert.Equal(17, styles.Characters(named["score"], "width"));
    }

    [Fact]
    public void AClassThatShoutsIsReadAsShouting()
    {
        var styles = Story("tethered.aastory");

        // The class the game puts its act titles in asks for a great
        // deal at once: centered, in capitals, heavy, half again as
        // large, and with the letters spread out. A terminal can do
        // the first three of those and not the rest.
        var title = Shouting(styles);

        Assert.Equal(AaAlignment.Center, styles.Alignment(title));
        Assert.True(styles.IsBold(title));

        // A width given as a percentage is not a width in characters.
        Assert.Null(styles.Characters(title, "width"));

        // [aam story] Its top margin is one and a half lines and its
        // bottom margin half a line. A grid of characters has no half
        // lines, so the fractions go.
        Assert.Equal(1, styles.Ems(title, "margin-top", 0));
        Assert.Equal(0, styles.Ems(title, "margin-bottom", 9));
    }

    [Fact]
    public void AMeasurementKeepsItsFractionAndItsUnit()
    {
        // The same class again, read by a frontend that can draw: the
        // fractions a grid of characters had to drop are the whole
        // point of a screen with pixels on it.
        var styles = Story("tethered.aastory");
        var title = Shouting(styles);

        Assert.Equal(new AaLength(1.5, AaUnit.Em), styles.Margin(title, AaSide.Top));
        Assert.Equal(new AaLength(0.5, AaUnit.Em), styles.Margin(title, AaSide.Bottom));
        Assert.Equal(new AaLength(100, AaUnit.Percent), styles.Length(title, "width"));
        Assert.Equal(new AaLength(1.5, AaUnit.Em), styles.Length(title, "font-size"));
        Assert.Equal(new AaLength(1, AaUnit.Em), styles.Length(title, "letter-spacing"));
    }

    [Fact]
    public void AUnitNothingHereMeasuresInIsNoMeasurementAtAll()
    {
        // This story sets out to be read wrongly: it gives the same
        // measurement in a unit that is CSS, a unit that is not, and
        // a unit that is CSS but means nothing here. Declining to
        // answer is the right answer for the last two.
        var styles = Conformance("codepoints");
        var named = Names(styles);

        Assert.Equal(new AaLength(2, AaUnit.Em), styles.Length(named["status2"], "height"));
        Assert.Equal(new AaLength(50, AaUnit.Percent), styles.Length(named["status5"], "height"));
        Assert.True(styles.Length(named["status6"], "height").IsNone);

        Assert.Equal(new AaLength(2, AaUnit.Ch), styles.Margin(named["marginch"], AaSide.Top));
        Assert.True(styles.Margin(named["marginch"], AaSide.Bottom).IsNone);
        Assert.Equal(new AaLength(50, AaUnit.Percent), styles.Margin(named["marginpct"], AaSide.Top));

        // So a frontend that counts blank lines counts none of them,
        // which is what the reference frontend does with this file.
        Assert.Equal(0, styles.Ems(named["marginpct"], "margin-top", 0));
        Assert.Equal(0, styles.Ems(named["marginch"], "margin-top", 0));

        // A height of no ems is a height all the same, and not the
        // absence of one: the story asks for a status area with
        // nothing in it.
        Assert.Equal(0, styles.Ems(named["status0"], "height", 7));
        Assert.Equal(4, styles.Ems(named["status4"], "height", 7));
    }

    [Fact]
    public void AShorthandGivesEverySideAndASideOfItsOwnBeatsIt()
    {
        // One measurement for a whole box is every side of it.
        var arkun = Story("D-ARKUN.aastory");
        var boxed = Only(arkun, i => arkun.Length(i, "margin").Amount == 3);

        foreach (var side in (AaSide[])[AaSide.Top, AaSide.Right, AaSide.Bottom, AaSide.Left])
        {
            Assert.Equal(new AaLength(3, AaUnit.Em), arkun.Margin(boxed, side));
            Assert.Equal(new AaLength(0.5, AaUnit.Em), arkun.Padding(boxed, side));
        }

        // And where a side was named on its own, that is the side
        // that is used and the shorthand covers the rest.
        var gosling = Conformance("gosling");
        var quoted = Only(gosling, i => gosling.Length(i, "border-radius").Amount == 12);

        Assert.Equal(new AaLength(0.67, AaUnit.Em), gosling.Padding(quoted, AaSide.Left));
        Assert.Equal(new AaLength(0.125, AaUnit.Em), gosling.Padding(quoted, AaSide.Top));
        Assert.Equal(new AaLength(0.125, AaUnit.Em), gosling.Padding(quoted, AaSide.Right));
    }

    [Fact]
    public void AColorComesByNameOrByDigitsOrByItsParts()
    {
        var arkun = Story("D-ARKUN.aastory");
        var silver = Only(arkun, i => arkun.Families(i).Contains("Shadows"));

        Assert.Equal(AaColorKind.Set, arkun.BackgroundColor(silver).Kind);
        Assert.Equal(0xFFC0C0C0, arkun.BackgroundColor(silver).Value);

        // Three digits are the six with each one doubled.
        var deux = Story("pas-de-deux.aastory");
        var framed = Only(deux, i => deux.Border(i).IsDrawn);

        Assert.Equal(0xFF888888, deux.Border(framed).Color.Value);
        Assert.Equal(new AaLength(1, AaUnit.Pixels), deux.Border(framed).Width);
        Assert.Equal("solid", deux.Border(framed).Line);

        // A color given as its parts may be a wash rather than a
        // fill, and how much of it there is has to come along, or a
        // tenth of a blue is painted as all of it.
        var gosling = Conformance("gosling");
        var washed = Only(gosling, i => gosling.BackgroundColor(i).Value == 0x1A0000FF);
        var wash = gosling.BackgroundColor(washed);

        Assert.Equal(0x1A, wash.Alpha);
        Assert.Equal(0x00, wash.Red);
        Assert.Equal(0x00, wash.Green);
        Assert.Equal(0xFF, wash.Blue);
        Assert.Equal(0xFF0000FF, gosling.Border(washed).Color.Value);
    }

    [Fact]
    public void AClassThatInsistsIsReadTheSameAsOneThatDoesNot()
    {
        // This story writes the same property twice over, the second
        // time insisting on it. There is no cascade here for the word
        // to win in, and the second one was going to stand anyway, so
        // what matters is that the word does not spoil the value it
        // is attached to.
        var gosling = Conformance("gosling");
        var upright = Only(gosling, i => gosling.Length(i, "font-size").Amount == 0.9);

        Assert.Equal(false, gosling.Italic(upright));
        Assert.False(gosling.IsItalic(upright));

        var shouted = Only(gosling, i => gosling.Color(i).IsSet);

        Assert.Equal(0xFFFF0000, gosling.Color(shouted).Value);
    }

    [Fact]
    public void SayingNothingAndSayingOrdinaryAreNotTheSameThing()
    {
        // A class that says nothing about the weight leaves whatever
        // is in force alone. A class that asks for ordinary text
        // means to cancel it. Telling the two apart is what lets a
        // heavy span inside a leaning div stand up straight.
        var styles = Conformance("codepoints");
        var named = Names(styles);

        Assert.Equal(true, styles.Bold(named["bold"]));
        Assert.Equal(false, styles.Bold(named["unbold"]));
        Assert.Null(styles.Bold(named["italic"]));

        Assert.Equal(true, styles.Italic(named["italic"]));
        Assert.Equal(false, styles.Italic(named["roman"]));
        Assert.Null(styles.Italic(named["bold"]));

        // CSS leans text two ways and no frontend here has both.
        Assert.Equal(true, styles.Italic(named["oblique"]));

        Assert.Equal(true, styles.Bold(named["bolditalic"]));
        Assert.Equal(true, styles.Italic(named["bolditalic"]));
    }

    [Fact]
    public void AClassIsSetAsideToWhicheverEdgeItNames()
    {
        var styles = Conformance("codepoints");
        var named = Names(styles);

        Assert.Equal(AaFloat.Left, styles.FloatsTo(named["leftcol"]));
        Assert.Equal(AaFloat.Right, styles.FloatsTo(named["rightcol"]));
        Assert.Equal(AaFloat.Right, styles.FloatsTo(named["pctcol"]));
        Assert.Equal(AaFloat.None, styles.FloatsTo(named["bold"]));

        // A frontend that can only set a block against the right
        // sees the one it can use and not the others.
        Assert.False(styles.FloatsRight(named["leftcol"]));
        Assert.True(styles.FloatsRight(named["rightcol"]));

        // Two of them are as wide as a number of characters and one
        // is a share of the room there is.
        Assert.Equal(12, styles.Characters(named["leftcol"], "width"));
        Assert.Equal(10, styles.Characters(named["rightcol"], "width"));
        Assert.Null(styles.Characters(named["pctcol"], "width"));
        Assert.Equal(new AaLength(25, AaUnit.Percent), styles.Length(named["pctcol"], "width"));

        // [aam story] A block may also ask to be set below whatever
        // has been put aside rather than beside it.
        var deux = Story("pas-de-deux.aastory");

        Assert.Contains(Enumerable.Range(0, deux.Count), deux.ClearsFloats);
    }

    [Fact]
    public void AFaceIsReadAsAListOrAsTheOneQuestionATerminalCanAsk()
    {
        var styles = Conformance("codepoints");
        var named = Names(styles);

        Assert.Equal(["monospace"], styles.Families(named["fixed"]));
        Assert.Equal(["Georgia", "serif"], styles.Families(named["propfont"]));
        Assert.Empty(styles.Families(named["bold"]));

        // A frontend with one face to spare reads no more of a list
        // than the word every such list ends with.
        Assert.Equal(true, styles.IsMonospace(named["fixed"]));
        Assert.Equal(false, styles.IsMonospace(named["propfont"]));
        Assert.Null(styles.IsMonospace(named["bold"]));

        // A name in quotes is the name without them, and a list that
        // ends in the word is a fixed face however it begins.
        var deux = Story("pas-de-deux.aastory");
        var quoted = Only(deux, i => deux.Families(i).Contains("Times New Roman"));

        Assert.Equal(["Times New Roman", "monospace"], deux.Families(quoted));
        Assert.Equal(true, deux.IsMonospace(quoted));
    }

    [Fact]
    public void TheOneStylePropertyThatIsNotCssIsReadAllTheSame()
    {
        // [aam story] Some devices can turn their text inside out and
        // have no other way to mark it, and this property was
        // invented for them.
        var styles = Conformance("codepoints");
        var named = Names(styles);

        Assert.Equal(true, styles.IsReversed(named["reverse"]));
        Assert.Equal(false, styles.IsReversed(named["unreverse"]));
        Assert.Null(styles.IsReversed(named["bold"]));

        // And the property it replaced is not read, which is what the
        // story calls the class that still uses it.
        Assert.Null(styles.IsReversed(named["ignoredcss"]));
    }

    [Fact]
    public void EveryStoryDescribesStylesThatCanBeRead()
    {
        var stories = Corpus.AaStoryFiles();
        Assert.SkipUnless(stories.Count > 0, "The entharion submodule is not populated.");

        foreach (var path in stories)
        {
            var name = Path.GetFileName(path);
            var styles = AaStory.Read(File.ReadAllBytes(path)).Styles;

            Assert.True(styles.Count > 0, $"{name} defines no style classes.");

            for (var i = 0; i < styles.Count; i++)
            {
                // Nothing here may throw or come back as nonsense,
                // whatever the story asked for, since a frontend is
                // told to read past what it does not understand.
                Assert.InRange(styles.Ems(i, "margin-top", 0), 0, 100);
                Assert.InRange(styles.Ems(i, "margin-bottom", 0), 0, 100);
                Assert.InRange(styles.Characters(i, "width") ?? 0, 0, 1000);
                Assert.Contains(styles.Alignment(i), (AaAlignment[])[AaAlignment.Start, AaAlignment.Center, AaAlignment.End]);
            }

            // A class nothing defines is not an error either: it is
            // simply a class with nothing in it.
            Assert.Null(styles.Property(styles.Count, "font-weight"));
            Assert.False(styles.IsBold(-1));
        }
    }

    [Fact]
    public void EveryClassOfEveryStoryAnswersSensiblyOrDeclines()
    {
        var stories = Corpus.AaStoryFiles();
        Assert.SkipUnless(stories.Count > 0, "The entharion submodule is not populated.");

        var sides = (AaSide[])[AaSide.Top, AaSide.Right, AaSide.Bottom, AaSide.Left];

        foreach (var path in stories)
        {
            var styles = AaStory.Read(File.ReadAllBytes(path)).Styles;

            for (var i = 0; i < styles.Count; i++)
            {
                foreach (var side in sides)
                {
                    // A measurement is either absent or in a unit
                    // that is one of the ones there are.
                    Assert.True(Enum.IsDefined(styles.Margin(i, side).Unit));
                    Assert.True(Enum.IsDefined(styles.Padding(i, side).Unit));
                }

                Assert.True(Enum.IsDefined(styles.FloatsTo(i)));
                Assert.True(Enum.IsDefined(styles.Color(i).Kind));
                Assert.True(Enum.IsDefined(styles.BackgroundColor(i).Kind));
                Assert.True(Enum.IsDefined(styles.Border(i).Color.Kind));

                // A face is a list of names with nothing blank in it.
                Assert.All(styles.Families(i), family => Assert.NotEmpty(family));
            }
        }
    }

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

    /// <summary>
    /// The classes of a story that carries the names its Dialog source
    /// gave them, which is what makes one readable against the other.
    /// </summary>
    private static Dictionary<string, int> Names(AaStyles styles) =>
        Enumerable.Range(0, styles.Count)
            .Where(i => styles.Name(i) is not null)
            .ToDictionary(i => styles.Name(i)!, i => i, StringComparer.Ordinal);

    /// <summary>
    /// The one class a story has that answers to something, for the
    /// stories whose classes have no names to be found by.
    /// </summary>
    private static int Only(AaStyles styles, Func<int, bool> answers) =>
        Enumerable.Range(0, styles.Count).Single(answers);

    /// <summary>The one class that asks to be shouted.</summary>
    private static int Shouting(AaStyles styles) => Only(styles, styles.IsUppercase);
}

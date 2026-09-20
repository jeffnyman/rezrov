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
        var path = Corpus.AaStoryFile("picture-test.aastory");
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        var styles = AaStory.Read(File.ReadAllBytes(path!)).Styles;

        // This story is the one whose classes carry the names the
        // Dialog source gave them, so it can be read against its own
        // source rather than guessed at.
        var named = Enumerable.Range(0, styles.Count)
            .Where(i => styles.Name(i) is not null)
            .ToDictionary(i => styles.Name(i)!, i => i, StringComparer.Ordinal);

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
        var path = Corpus.AaStoryFile("tethered.aastory");
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        var styles = AaStory.Read(File.ReadAllBytes(path!)).Styles;

        // The class the game puts its act titles in asks for a great
        // deal at once: centered, in capitals, heavy, half again as
        // large, and with the letters spread out. A terminal can do
        // the first three of those and not the rest.
        var title = Enumerable.Range(0, styles.Count).Single(i => styles.IsUppercase(i));

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
}

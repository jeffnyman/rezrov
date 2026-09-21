using Rezrov.AaMachine;
using Rezrov.Core.Graphics;

namespace Rezrov.Tests;

/// <summary>
/// [aam story] The files a story carries inside itself, which is where
/// a picture comes from.
/// </summary>
public class AaFileTests
{
    [Fact]
    public void AStoryHandsOverThePictureItCarries()
    {
        var path = Corpus.AaStoryFile("picture-test.aastory");
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        var story = AaStory.Read(System.IO.File.ReadAllBytes(path!));
        var packaged = Assert.Single(story.Files);

        Assert.Equal("testimage.png", packaged.Name);

        // The resource table points at the file by name, and following
        // that pointer has to arrive at the same bytes.
        var resource = Assert.Single(story.Resources);

        Assert.Equal("file:testimage.png", resource.Url);
        Assert.True(story.Contents(resource).SequenceEqual(story.File(packaged.Name)));

        // And those bytes have to be a picture. Reading it is the
        // proof that the name was skipped and the contents were not:
        // one byte out either way and there is no PNG here at all.
        var picture = PngReader.Read(story.Contents(resource));

        Assert.NotNull(picture);
        Assert.True(picture.Width > 0 && picture.Height > 0);

        // The story calls it four coloured bands, so it should have
        // more than one color in it.
        var colors = new HashSet<(byte, byte, byte, byte)>();

        for (var y = 0; y < picture.Height; y++)
        {
            for (var x = 0; x < picture.Width; x++)
            {
                colors.Add(picture.At(x, y));
            }
        }

        Assert.True(colors.Count >= 4, $"the test image has only {colors.Count} colors in it");
    }

    [Fact]
    public void AStoryThatCarriesNothingSaysSo()
    {
        var path = Corpus.AaStoryFile("tethered.aastory");
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        var story = AaStory.Read(System.IO.File.ReadAllBytes(path!));

        Assert.Empty(story.Files);
        Assert.True(story.File("testimage.png").IsEmpty);
    }

    [Fact]
    public void AResourceSomewhereElseIsNotAFileToBeRead()
    {
        var path = Corpus.AaStoryFile("pas-de-deux.aastory");
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        // This story carries four files and also points at three
        // places on the web, which no interpreter can go and fetch.
        var story = AaStory.Read(System.IO.File.ReadAllBytes(path!));

        Assert.Equal(4, story.Files.Count);

        var web = story.Resources.Where(r => r.Url.StartsWith("https:", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(web);
        Assert.All(web, resource => Assert.True(story.Contents(resource).IsEmpty));

        // The ones it does carry come back with something in them.
        var carried = story.Resources.Where(r => r.Url.StartsWith("file:", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(carried);
        Assert.All(carried, resource => Assert.False(story.Contents(resource).IsEmpty));
    }
}

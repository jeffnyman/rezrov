using Rezrov.AaMachine;
using Rezrov.Core;

namespace Rezrov.Tests;

/// <summary>
/// [aam story] Reading an Aa-machine story file, which is what Dialog
/// compiles to when it is not compiling to the Z-Machine.
/// </summary>
public class AaStoryTests
{
    [Fact]
    public void EveryStoryInTheCorpusReadsAndMatchesItsOwnChecksum()
    {
        var stories = Corpus.AaStoryFiles();
        Assert.SkipUnless(stories.Count > 0, "The entharion submodule is not populated.");

        var failures = new List<string>();

        foreach (var path in stories)
        {
            var name = Path.GetFileName(path);

            try
            {
                var story = AaStory.Read(File.ReadAllBytes(path));

                // The checksum is the whole proof that the container and
                // the header were read correctly: it is a running total
                // over seven chunks in a fixed order, so it only comes
                // out right if every chunk was found at the right place
                // and the right length.
                if (!story.VerifyChecksum())
                {
                    failures.Add(
                        $"{name}: the file says {story.Checksum:X8} and the contents come to {story.ComputeChecksum():X8}");
                }

                // [aam story] HEAD is first, and every chunk is named in
                // four characters.
                Assert.Equal("HEAD", story.Chunks[0].Name);
                Assert.All(story.Chunks, chunk => Assert.Equal(4, chunk.Name.Length));

                // [aam story] A word is two bytes, so far always.
                Assert.Equal(2, story.WordSize);

                // And the file is recognized for what it is without
                // being read, which is how the command line knows.
                Assert.Equal(
                    StoryFormat.AaMachine,
                    StoryFormatDetector.Detect(File.ReadAllBytes(path)));
            }
            catch (InvalidDataException e)
            {
                failures.Add($"{name}: {e.Message}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void TheHeaderReadsAsTheFileWasBuilt()
    {
        var path = Corpus.AaStoryFile("tethered.aastory");
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        var story = AaStory.Read(File.ReadAllBytes(path!));

        Assert.Equal(0, story.MajorVersion);
        Assert.Equal(2, story.MinorVersion);
        Assert.Equal(2, story.WordSize);
        Assert.Equal(4, story.Release);
        Assert.Equal("191125", story.Serial);
        Assert.Equal(1300, story.HeapSize);
        Assert.Equal(400, story.AuxSize);
        Assert.Equal(1552, story.RamSize);
        Assert.StartsWith("UUID://", story.Identifier, StringComparison.Ordinal);

        // The chunks it carries, in the order the file has them.
        Assert.Equal(
            ["HEAD", "META", "LOOK", "LANG", "MAPS", "DICT", "INIT", "CODE", "WRIT"],
            story.Chunks.Select(chunk => chunk.Name));
    }

    [Fact]
    public void SomethingThatIsNotAStoryIsRefusedWithAReason()
    {
        var notAForm = Assert.Throws<InvalidDataException>(() => AaStory.Read([1, 2, 3, 4]));
        Assert.Contains("not an Aa-machine story file", notAForm.Message, StringComparison.Ordinal);

        // An IFF form of the wrong kind: a Blorb, which is IFRS.
        byte[] blorb = [.. "FORM"u8, 0, 0, 0, 4, .. "IFRS"u8];
        var wrongKind = Assert.Throws<InvalidDataException>(() => AaStory.Read(blorb));
        Assert.Contains("not an Aa-machine story file", wrongKind.Message, StringComparison.Ordinal);

        // The right kind, but cut short, which is what a bad download
        // looks like.
        byte[] truncated = [.. "FORM"u8, 0, 0, 0xFF, 0, .. "AAVM"u8];
        var short1 = Assert.Throws<InvalidDataException>(() => AaStory.Read(truncated));
        Assert.Contains("bytes but is", short1.Message, StringComparison.Ordinal);

        // A form whose first chunk is not HEAD.
        byte[] headless = [.. "FORM"u8, 0, 0, 0, 12, .. "AAVM"u8, .. "CODE"u8, 0, 0, 0, 0];
        var noHead = Assert.Throws<InvalidDataException>(() => AaStory.Read(headless));
        Assert.Contains("must be HEAD", noHead.Message, StringComparison.Ordinal);
    }
}

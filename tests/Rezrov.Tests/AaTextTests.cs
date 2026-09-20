using Rezrov.AaMachine;

namespace Rezrov.Tests;

/// <summary>
/// [aam text] Reading the text of an Aa-machine story: the character
/// set it is written in, the tree its bits are packed against, and the
/// dictionary its commonest words come out of.
/// </summary>
public class AaTextTests
{
    [Fact]
    public void EveryStoryDescribesALanguageThatHangsTogether()
    {
        var stories = Corpus.AaStoryFiles();
        Assert.SkipUnless(stories.Count > 0, "The entharion submodule is not populated.");

        foreach (var path in stories)
        {
            var name = Path.GetFileName(path);
            var story = AaStory.Read(File.ReadAllBytes(path));
            var table = story.Language.DecodingTable;
            var nodes = table.Length / 2;

            // [aam story] At most 128 nodes, and nothing may point past
            // the last of them.
            Assert.InRange(nodes, 1, 128);

            foreach (var action in table)
            {
                if (action >= 0x81)
                {
                    Assert.InRange(action - 0x80, 0, nodes - 1);
                }
            }

            // A node nothing reaches would mean the tree had been read
            // wrongly, since every node is a decision the compressor
            // meant somebody to take.
            Assert.Equal(nodes, Reachable(table));

            // [aam story] A dictionary word is two characters or more
            // and is always in lowercase, which is what input is
            // converted to before anything is looked up.
            for (var i = 0; i < story.Dictionary.Count; i++)
            {
                var word = story.Dictionary.Characters(i);

                Assert.InRange(word.Length, 2, 255);

                foreach (var character in word)
                {
                    Assert.InRange(character, (byte)0x20, (byte)0xff);
                    Assert.Equal(character, story.Language.Characters.ToLower(character));
                }
            }

            // [aam story] Every character the game adds to ASCII knows
            // both its cases, and both of them are characters the game
            // actually has.
            var characters = story.Language.Characters;

            for (var i = 0; i < characters.Count; i++)
            {
                var character = characters[i];

                Assert.InRange(character.Codepoint, 0x20, 0x10ffff);
                Assert.InRange(character.Lower, (byte)0x80, (byte)(0x80 + characters.Count - 1));
                Assert.InRange(character.Upper, (byte)0x80, (byte)(0x80 + characters.Count - 1));
            }

            // [aam story] Punctuation that wants no space beside it has
            // to be punctuation that stands alone in the first place.
            Assert.All(story.Language.NoSpaceBefore, c => Assert.Contains(c, story.Language.StopCharacters));
            Assert.All(story.Language.NoSpaceAfter, c => Assert.Contains(c, story.Language.StopCharacters));

            Assert.True(story.Language.StopCharacters.Count > 0, $"{name} declares no stop characters.");
        }
    }

    [Fact]
    public void TheAlternativeTextOfAResourceReadsAsItWasWritten()
    {
        var path = Corpus.AaStoryFile("picture-test.aastory");
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        var story = AaStory.Read(File.ReadAllBytes(path!));
        var resource = Assert.Single(story.Resources);

        Assert.Equal("file:testimage.png", resource.Url);

        // This one sentence is the whole decoder proved at once. The
        // address came out of a three-byte string pointer, the words
        // came off the bitstream a bit at a time, and "four" is not
        // spelled out in the stream at all: it arrives as a dictionary
        // word through an escape, so the number of bits that escape
        // reads has to be exactly right or the sentence turns to
        // nonsense from there on.
        Assert.Equal("a test image of four coloured bands", story.Text.At(resource.AltText));
    }

    [Fact]
    public void AStoryOlderThanDictionaryWordsStillReadsItsResources()
    {
        var path = Corpus.AaStoryFile("pas-de-deux.aastory");
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        // A version 0.2 story, which escapes differently and never
        // names a dictionary word, so its text is spelled out in full.
        var story = AaStory.Read(File.ReadAllBytes(path!));

        Assert.Equal(0, story.MajorVersion);
        Assert.Equal(2, story.MinorVersion);

        var texts = story.Resources.Select(resource => story.Text.At(resource.AltText)).ToList();

        Assert.Equal(
            [
                "https://linusakesson.net/",
                "https://imslp.org/",
                "https://ifcomp.org/",
                "score.pdf",
                "how_to_read.pdf",
                "seating.pdf",
                "seating.png",
            ],
            texts);
    }

    [Fact]
    public void AStorySaysWhoWroteItInItsOwnCharacters()
    {
        var path = Corpus.AaStoryFile("tethered.aastory");
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        var story = AaStory.Read(File.ReadAllBytes(path!));

        Assert.Equal("Tethered", story.Metadata.Title);
        Assert.Equal("An interactive roleplay", story.Metadata.Noun);
        Assert.Equal("2019-11-25", story.Metadata.ReleaseDate);
        Assert.Equal("Dialog compiler version 0h/04", story.Metadata.Compiler);

        // The ring on that A is character $80 in this game and
        // something else entirely in the next one, so reading the name
        // is the plain proof that the table was read the right way
        // round.
        Assert.Equal("Linus Åkesson", story.Metadata.Author);

        // [aam text] Changing case is the table's other job, and it
        // goes to the game's own characters rather than to ASCII ones.
        // This game is written in Swedish and Polish at once, so it
        // carries both the ring and the stroke.
        var characters = story.Language.Characters;

        Assert.Equal(0x00e5, characters.Codepoint(characters.ToLower(0x80)));
        Assert.Equal(0x00c5, characters.Codepoint(characters.ToUpper(characters.ToLower(0x80))));
        Assert.Equal(0x0141, characters.Codepoint(characters.ToUpper(0x81)));
        Assert.Equal(0x0142, characters.Codepoint(characters.ToLower(characters.ToUpper(0x81))));
    }

    [Fact]
    public void EveryCharacterATreeCanSayComesBackFromItsOwnBits()
    {
        var stories = Corpus.AaStoryFiles();
        Assert.SkipUnless(stories.Count > 0, "The entharion submodule is not populated.");

        foreach (var path in stories)
        {
            var bytes = File.ReadAllBytes(path);
            var story = AaStory.Read(bytes);
            var routes = Routes(story.Language.DecodingTable);

            // Make the story say every character its own tree has a
            // leaf for. The resources above prove only the letters they
            // happen to use, while a tree carries leaves for most of
            // the printable range and for the game's own characters,
            // whose bytes sit $60 further along and would come out as
            // punctuation if that were read wrongly.
            var written = new List<byte>();
            var bits = new List<bool>();

            foreach (var (action, route) in routes.OrderBy(route => route.Key))
            {
                if (action is 0x5f or 0x80)
                {
                    continue;
                }

                bits.AddRange(route);
                written.Add((byte)(0x20 + action));
            }

            bits.AddRange(routes[0x80]);

            var name = Path.GetFileName(path);
            var decoded = AaStory.Read(Saying(bytes, story, bits)).Text.At(0);

            Assert.Equal(story.Language.Characters.Text(written.ToArray()), decoded);

            // A tree only carries leaves for the characters its game
            // actually uses, so the count varies, but any game that can
            // hold a conversation can say the alphabet.
            foreach (var letter in "abcdefghijklmnopqrstuvwxyz ")
            {
                Assert.Contains((byte)letter, written);
            }

            Assert.True(written.Count > 60, $"{name} can say only {written.Count} characters.");
        }
    }

    /// <summary>
    /// The same story with one bitstream written over the start of its
    /// compressed text, so that a test can put words in its mouth.
    /// </summary>
    private static byte[] Saying(byte[] bytes, AaStory story, List<bool> bits)
    {
        var writ = story.Chunks.First(chunk => chunk.Name == "WRIT");
        var copy = bytes.ToArray();

        // [aam story] Bits are packed into bytes starting with the most
        // significant, and a stream begins on a byte boundary.
        for (var i = 0; i < bits.Count; i++)
        {
            var at = writ.Offset + (i / 8);
            var mask = (byte)(0x80 >> (i % 8));

            if (i % 8 == 0)
            {
                copy[at] = 0;
            }

            if (bits[i])
            {
                copy[at] |= mask;
            }
        }

        return copy;
    }

    /// <summary>
    /// The bits that lead to each thing a decoding tree can say, found
    /// by walking it from the root.
    /// </summary>
    private static Dictionary<byte, bool[]> Routes(ReadOnlySpan<byte> table)
    {
        var routes = new Dictionary<byte, bool[]>();
        var route = new List<bool>();

        Walk(table, 0, route, routes);

        return routes;
    }

    private static void Walk(ReadOnlySpan<byte> table, int node, List<bool> route, Dictionary<byte, bool[]> routes)
    {
        for (var bit = 0; bit < 2; bit++)
        {
            var action = table[(node * 2) + bit];

            route.Add(bit == 1);

            if (action >= 0x81)
            {
                Walk(table, action - 0x80, route, routes);
            }
            else
            {
                routes.TryAdd(action, route.ToArray());
            }

            route.RemoveAt(route.Count - 1);
        }
    }

    private static int Reachable(ReadOnlySpan<byte> table)
    {
        var seen = new HashSet<int>();
        var pending = new Stack<int>();

        pending.Push(0);

        while (pending.Count > 0)
        {
            var node = pending.Pop();

            if (!seen.Add(node))
            {
                continue;
            }

            for (var bit = 0; bit < 2; bit++)
            {
                var action = table[(node * 2) + bit];

                if (action >= 0x81)
                {
                    pending.Push(action - 0x80);
                }
            }
        }

        return seen.Count;
    }
}

using Rezrov.ZMachine;
using Rezrov.ZMachine.Lexing;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

public class DictionaryTableTests
{
    private const int DictionaryAddress = 0x200;

    // The three separators Inform always uses: full stop, comma, double
    // quote. [zm 13.2]
    private static readonly byte[] Separators = [(byte)'.', (byte)',', (byte)'"'];

    /// <summary>
    /// A story file whose only interesting content is a dictionary at
    /// $200, built from a list of words with the production encoder. The
    /// entries are sorted the way [zm 13.5] requires unless asked not to
    /// be, in which case the count is stored negative.
    /// </summary>
    private sealed class Story
    {
        public byte[] Bytes { get; } = new byte[4096];

        public Story(ZMachineVersion version, string[] words, bool sorted = true, int dataBytes = 3)
        {
            Bytes[0x00] = (byte)version;
            PutWord(0x04, 0x0040);
            PutWord(0x08, DictionaryAddress);
            PutWord(0x0E, 0x0040);
            PutWord(0x18, 0x0080);

            var encoder = new ZTextEncoder(version, AlphabetTable.Default);
            var entries = words.Select(encoder.EncodeWord).ToList();
            if (sorted)
            {
                entries.Sort((a, b) => a.AsSpan().SequenceCompareTo(b));
            }

            // [zm 13.2] The header, then [zm 13.3] the entries.
            var at = DictionaryAddress;
            Bytes[at++] = (byte)Separators.Length;
            Separators.CopyTo(Bytes, at);
            at += Separators.Length;

            EntryLength = encoder.EncodedLength + dataBytes;
            Bytes[at++] = (byte)EntryLength;
            PutWord(at, sorted ? entries.Count : -entries.Count);
            at += 2;

            FirstEntryAddress = at;
            foreach (var entry in entries)
            {
                entry.CopyTo(Bytes, at);
                at += EntryLength;
            }
        }

        public int EntryLength { get; }

        public int FirstEntryAddress { get; }

        public void PutWord(int address, int value)
        {
            Bytes[address] = (byte)(value >> 8);
            Bytes[address + 1] = (byte)value;
        }

        public DictionaryTable Dictionary()
        {
            var memory = new ZMemory(Bytes);
            var header = new StoryHeader(memory);
            var decoder = new ZTextDecoder(memory, header);
            return DictionaryTable.Standard(memory, header, decoder, ZTextEncoder.ForStory(header, memory));
        }
    }

    private static readonly string[] Words = ["lantern", "xyzzy", "an", "anaconda", "mailbox", "open"];

    [Theory]
    [InlineData(ZMachineVersion.V3, 7)]
    [InlineData(ZMachineVersion.V5, 9)]
    public void ReadsTheHeader(ZMachineVersion version, int entryLength)
    {
        var story = new Story(version, Words);
        var dictionary = story.Dictionary();

        // [zm 13.1] From the header word at $08.
        Assert.Equal(DictionaryAddress, dictionary.Address);

        // [zm 13.2]
        Assert.Equal(Separators, dictionary.WordSeparators);
        Assert.Equal(entryLength, dictionary.EntryLength);
        Assert.Equal(Words.Length, dictionary.Count);
        Assert.True(dictionary.IsSorted);
        Assert.Equal(story.FirstEntryAddress, dictionary.FirstEntryAddress);
    }

    [Fact]
    public void DecodesEntriesInSortedOrder()
    {
        // [zm 13.5] Numerical order of the encoded text, which the remarks
        // note is alphabetical order for ordinary words, with "an" before
        // "anaconda".
        var later = new Story(ZMachineVersion.V5, Words).Dictionary();
        Assert.Equal(
            ["an", "anaconda", "lantern", "mailbox", "open", "xyzzy"],
            Enumerable.Range(0, later.Count).Select(later.Word));

        // [zm 13.3] Six Z-characters before Version 4, so the longer words
        // are stored, and decode, cut to six letters.
        var early = new Story(ZMachineVersion.V3, Words).Dictionary();
        Assert.Equal(
            ["an", "anacon", "lanter", "mailbo", "open", "xyzzy"],
            Enumerable.Range(0, early.Count).Select(early.Word));
    }

    [Theory]
    [InlineData(ZMachineVersion.V3)]
    [InlineData(ZMachineVersion.V5)]
    public void LooksUpWordsBySearchingTheSortedEntries(ZMachineVersion version)
    {
        var story = new Story(version, Words);
        var dictionary = story.Dictionary();

        // "lantern" is the third entry once sorted.
        Assert.Equal(story.FirstEntryAddress + (2 * story.EntryLength), dictionary.Lookup("lantern"));
        Assert.Equal(story.FirstEntryAddress, dictionary.Lookup("an"));
        Assert.Equal(story.FirstEntryAddress + (5 * story.EntryLength), dictionary.Lookup("xyzzy"));

        Assert.Equal(0, dictionary.Lookup("grue"));
        Assert.Equal(0, dictionary.Lookup(""));
    }

    [Fact]
    public void ALongWordMatchesTheEntryItTruncatesTo()
    {
        var dictionary = new Story(ZMachineVersion.V3, Words).Dictionary();

        // [zm 3.7] Six Z-characters in Version 3, so "lantern" is stored
        // as "lanter", and anything that begins that way is the same word.
        Assert.NotEqual(0, dictionary.Lookup("lanternxyz"));
        Assert.Equal(dictionary.Lookup("lantern"), dictionary.Lookup("lanter"));

        // Where the ninth character fits, it counts.
        var later = new Story(ZMachineVersion.V5, Words).Dictionary();
        Assert.NotEqual(0, later.Lookup("lantern"));
        Assert.Equal(0, later.Lookup("lanternxy"));
    }

    [Fact]
    public void SearchesAnUnsortedUserDictionaryFromEndToEnd()
    {
        // [zm op:tokenise] A negative count means unsorted. The words are
        // stored in the order given here, which is not alphabetical.
        var story = new Story(ZMachineVersion.V5, ["zebra", "apple", "mango"], sorted: false);
        var dictionary = story.Dictionary();

        Assert.False(dictionary.IsSorted);
        Assert.Equal(3, dictionary.Count);
        Assert.Equal(story.FirstEntryAddress, dictionary.Lookup("zebra"));
        Assert.Equal(story.FirstEntryAddress + story.EntryLength, dictionary.Lookup("apple"));
        Assert.Equal(0, dictionary.Lookup("kiwi"));
    }

    [Fact]
    public void RejectsEntriesTooShortForAnEncodedWord()
    {
        // [zm 13.2] At least 6 bytes from Version 4, so 5 is too few.
        var story = new Story(ZMachineVersion.V5, Words, dataBytes: -1);

        Assert.Throws<InvalidDataException>(() => story.Dictionary());
    }

    [Fact]
    public void LookupIsExactAboutTheEncodedLength()
    {
        var dictionary = new Story(ZMachineVersion.V5, Words).Dictionary();

        Assert.Throws<ArgumentException>(() => dictionary.LookupEncoded(new byte[4]));
    }

    [Fact]
    public void SplitsOnSpacesAndSeparators()
    {
        // [zm 13.6.1] The example from the standard: erratically spaced
        // text becomes fred, the comma, go, and fishing.
        var text = "fred,go fishing"u8;

        var words = Lexer.Split(text, Separators);

        Assert.Equal([(0, 4), (4, 1), (5, 2), (8, 7)], words);
    }

    [Fact]
    public void SpacesAreIgnoredButSeparatorsAreWords()
    {
        // [zm 13.6.1] Runs of spaces at either end or in the middle
        // produce nothing, while adjacent separators are each a word.
        var text = "  look  ,.  at \"x\" "u8;

        var words = Lexer.Split(text, Separators);

        Assert.Equal([(2, 4), (8, 1), (9, 1), (12, 2), (15, 1), (16, 1), (17, 1)], words);
    }

    [Fact]
    public void AnalysisLooksUpEachWordAfterLowerCasing()
    {
        var dictionary = new Story(ZMachineVersion.V3, Words).Dictionary();

        // [zm 13.6.2] Each word is encoded and searched for, and [zm 3.7]
        // typed text is converted to lower case first.
        var tokens = Lexer.Analyze("Open MAILBOX, grue"u8, dictionary);

        Assert.Equal(4, tokens.Count);
        Assert.Equal(new Token(0, 4, dictionary.Lookup("open")), tokens[0]);
        Assert.Equal(new Token(5, 7, dictionary.Lookup("mailbox")), tokens[1]);
        Assert.Equal(new Token(12, 1, 0), tokens[2]);
        Assert.Equal(new Token(14, 4, 0), tokens[3]);
    }
}

using Rezrov.ZMachine;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

public class ZTextDecoderTests
{
    // Where the tests put things inside a 2K story. The header's
    // abbreviations pointer is set to AbbreviationsTable, and the string
    // and table addresses are chosen to be even, since abbreviation
    // entries are word addresses and could not point at an odd byte.
    private const int AbbreviationsTable = 0x100;
    private const int Text = 0x400;
    private const int Abbreviation = 0x500;
    private const int AlphabetTableAddress = 0x600;
    private const int ExtensionTable = 0x700;
    private const int UnicodeTable = 0x720;

    /// <summary>
    /// A story file with a valid header and nothing else, to be filled in
    /// by each test. The header's static and high memory bases are at $40,
    /// so the whole file after the header is dynamic memory, which keeps
    /// the header validation out of the way.
    /// </summary>
    private sealed class Story
    {
        public byte[] Bytes { get; } = new byte[2048];

        public Story(ZMachineVersion version)
        {
            Bytes[0x00] = (byte)version;
            PutWord(0x04, 0x0040);
            PutWord(0x0E, 0x0040);
            PutWord(0x18, AbbreviationsTable);
        }

        public void PutWord(int address, int value)
        {
            Bytes[address] = (byte)(value >> 8);
            Bytes[address + 1] = (byte)value;
        }

        public void Put(int address, byte[] data) => data.CopyTo(Bytes, address);

        // [zm 3.3] Entry n of the table is a word address.
        public void SetAbbreviation(int number, int byteAddress) =>
            PutWord(AbbreviationsTable + (number * 2), byteAddress / 2);

        public ZTextDecoder Decoder()
        {
            var memory = new ZMemory(Bytes);
            return new ZTextDecoder(memory, new StoryHeader(memory));
        }
    }

    /// <summary>
    /// Packs Z-characters into words the way [zm 3.2] describes: three to
    /// a word, most significant first, padded with 5s to fill the last
    /// word, and with the top bit set on that last word. Tests give their
    /// input as Z-characters so each one reads like the clause it checks.
    /// </summary>
    private static byte[] Words(params int[] zCharacters) => ZChars.Words(zCharacters);

    private static string Decode(ZMachineVersion version, params int[] zCharacters)
    {
        var story = new Story(version);
        story.Put(Text, Words(zCharacters));
        return story.Decoder().Decode(Text);
    }

    // Z-characters for lower case letters, from [zm 3.5.3]: 'a' is 6.
    private static int L(char c) => ZChars.Letter(c);

    [Fact]
    public void DecodesLowerCaseLettersAndSpaces()
    {
        // [zm 3.5.1] Z-character 0 is a space, and [zm 3.5.3] letters run
        // from 6.
        var text = Decode(
            ZMachineVersion.V3,
            L('h'), L('e'), L('l'), L('l'), L('o'), 0, L('w'), L('o'), L('r'), L('l'), L('d'));

        Assert.Equal("hello world", text);
    }

    [Fact]
    public void StopsAtTheEndBitAndReportsWhereTheStringEnds()
    {
        var story = new Story(ZMachineVersion.V3);
        story.Put(Text, Words(L('h'), L('e'), L('y')));

        // Whatever follows the last word must not be read.
        story.Bytes[Text + 2] = 0xFF;
        story.Bytes[Text + 3] = 0xFF;

        var text = story.Decoder().Decode(Text, out var end);

        Assert.Equal("hey", text);
        Assert.Equal(Text + 2, end);
    }

    [Fact]
    public void ShiftsThatPrintNothingAreLegal()
    {
        // [zm 3.2.4] Any run of shifts is fine, and [zm 3.6] the padding
        // at the end of a string is a run of 5s.
        Assert.Equal("a", Decode(ZMachineVersion.V3, L('a')));
        Assert.Equal("A", Decode(ZMachineVersion.V3, 4, 4, 4, L('a')));
    }

    [Fact]
    public void SingleShiftsLastOneCharacterFromVersion3()
    {
        // [zm 3.2.3] 4 puts the next character in A1, 5 puts it in A2,
        // and then it is straight back to A0. In A2, Z-character 8 is
        // the digit 0.
        var text = Decode(ZMachineVersion.V3, 4, L('a'), L('a'), 5, 8, L('a'));

        Assert.Equal("Aa0a", text);
    }

    [Theory]
    [InlineData(ZMachineVersion.V1)]
    [InlineData(ZMachineVersion.V2)]
    public void EarlyShiftsAreRelativeToTheCurrentAlphabet(ZMachineVersion version)
    {
        // [zm 3.2.2] From A0, Z-character 2 reaches A1 and 3 reaches A2,
        // for one character. Z-character 28 in A2 is '-' in every
        // version, which matters because [zm 3.5.4] the Version 1 row is
        // shifted by one place for everything before the backslash.
        Assert.Equal("Aa", Decode(version, 2, L('a'), L('a')));
        Assert.Equal("-a", Decode(version, 3, 28, L('a')));
    }

    [Theory]
    [InlineData(ZMachineVersion.V1)]
    [InlineData(ZMachineVersion.V2)]
    public void EarlyShiftLocksStayInForce(ZMachineVersion version)
    {
        // [zm 3.2.2] 4 locks one alphabet forward and 5 locks two forward,
        // measured from the alphabet already locked. So from A0, 4 locks
        // A1; then from A1, 5 wraps around to A0.
        var text = Decode(version, 4, L('a'), L('b'), 5, L('c'), L('a'));

        Assert.Equal("ABca", text);
    }

    [Theory]
    [InlineData(ZMachineVersion.V1)]
    [InlineData(ZMachineVersion.V2)]
    public void EarlyTemporaryShiftsMeasureFromTheLockedAlphabet(ZMachineVersion version)
    {
        // With A1 locked, a temporary 2 goes one forward from the lock to
        // A2, prints one character there, and falls back to the lock.
        Assert.Equal("-B", Decode(version, 4, 2, 28, L('b')));

        // Two temporary shifts in a row. The standard does not say
        // whether the second measures from the lock or from the first
        // shift; Frotz measures from the lock, so 2 then 2 is still A1
        // rather than A2, and the following 'a' is a capital rather than
        // the start of an escape.
        Assert.Equal("A", Decode(version, 2, 2, L('a')));
    }

    [Fact]
    public void Version1UsesZCharacter1ForNewlineAndHasNoNewlineInA2()
    {
        // [zm 3.5.2] In Version 1, Z-character 1 is a newline.
        Assert.Equal("a\nb", Decode(ZMachineVersion.V1, L('a'), 1, L('b')));

        // [zm 3.5.4] And the A2 row has no newline, so Z-character 7
        // there is the digit 0, where every later version has a newline.
        Assert.Equal("0", Decode(ZMachineVersion.V1, 5, 7));
        Assert.Equal("\n", Decode(ZMachineVersion.V2, 5, 7));
        Assert.Equal("\n", Decode(ZMachineVersion.V3, 5, 7));
    }

    [Fact]
    public void Version1HasALessThanSignWhereLaterVersionsDoNot()
    {
        // [zm 3.5.4] Dropping the newline from the A2 row moves every
        // character before the backslash up one place and makes room for
        // '<' right after it. So Z-character 27 is '<' in Version 1 and
        // the backslash itself from Version 2 on, while 28 is '-' in
        // both, since that is where the two rows fall back into step.
        Assert.Equal("<", Decode(ZMachineVersion.V1, 5, 27));
        Assert.Equal("\\", Decode(ZMachineVersion.V3, 5, 27));
        Assert.Equal("-", Decode(ZMachineVersion.V1, 5, 28));
        Assert.Equal("-", Decode(ZMachineVersion.V3, 5, 28));
    }

    [Theory]
    [InlineData(ZMachineVersion.V1)]
    [InlineData(ZMachineVersion.V3)]
    [InlineData(ZMachineVersion.V8)]
    public void ZsciiEscapeBuildsATenBitCode(ZMachineVersion version)
    {
        // [zm 3.4] The example from the standard's editor's note: '@' is
        // ZSCII 64, which appears in no alphabet, so shift to A2, escape,
        // then 64 >> 5 and 64 & 31.
        Assert.Equal("@", Decode(version, 5, 6, 2, 0));
    }

    [Fact]
    public void TheEscapeIsZCharacter6InA2Only()
    {
        // [zm 3.4] In A0 the value 6 is simply the letter a.
        Assert.Equal("aa", Decode(ZMachineVersion.V3, L('a'), 6));
    }

    [Fact]
    public void AnIncompleteEscapeAtTheEndIsIgnored()
    {
        // [zm 3.6.1] Ending in the middle of a construction is legal, and
        // the partial construction is dropped. Exactly three Z-characters
        // here, so there is no padding to be mistaken for the missing
        // half.
        Assert.Equal("", Decode(ZMachineVersion.V3, 5, 6, 2));
        Assert.Equal("a", Decode(ZMachineVersion.V3, L('a'), 5, 6));
    }

    [Fact]
    public void AbbreviationsExpandFromTheTableFromVersion3()
    {
        var story = new Story(ZMachineVersion.V3);
        story.Put(Abbreviation, Words(L('h'), L('e'), L('l'), L('l'), L('o')));
        story.Put(Abbreviation + 0x10, Words(L('w'), L('o'), L('r'), L('l'), L('d')));
        story.Put(Abbreviation + 0x20, Words(L('z'), L('o'), L('r'), L('k')));

        // [zm 3.3] Entry 32(z-1)+x: bank 1 with x 0 is entry 0, bank 2
        // with x 3 is entry 35, bank 3 with x 31 is entry 95, the last.
        story.SetAbbreviation(0, Abbreviation);
        story.SetAbbreviation(35, Abbreviation + 0x10);
        story.SetAbbreviation(95, Abbreviation + 0x20);

        story.Put(Text, Words(1, 0, 0, 2, 3, 0, 3, 31));

        Assert.Equal("hello world zork", story.Decoder().Decode(Text));
    }

    [Fact]
    public void Version2HasOnlyTheFirstAbbreviationBank()
    {
        var story = new Story(ZMachineVersion.V2);
        story.Put(Abbreviation, Words(L('h'), L('i')));
        story.SetAbbreviation(4, Abbreviation);

        // [zm 3.3] In Version 2, Z-character 1 introduces an abbreviation
        // but 2 and 3 do not; [zm 3.2.2] they are still shifts there.
        story.Put(Text, Words(1, 4, 0, 2, L('a')));

        Assert.Equal("hi A", story.Decoder().Decode(Text));
    }

    [Fact]
    public void AnAbbreviationInAStoryWithNoTableIsAnError()
    {
        var story = new Story(ZMachineVersion.V3);

        // Some Infocom files leave the table address at zero because they
        // never use abbreviations. Text that does use one then has nothing
        // to refer to.
        story.PutWord(0x18, 0);
        story.Put(Text, Words(1, 0));

        Assert.Throws<InvalidDataException>(() => story.Decoder().Decode(Text));
    }

    [Fact]
    public void AnAbbreviationMayNotUseAnAbbreviation()
    {
        var story = new Story(ZMachineVersion.V3);

        // An abbreviation whose text begins with another abbreviation.
        story.Put(Abbreviation, Words(1, 0, L('x')));
        story.SetAbbreviation(0, Abbreviation);
        story.Put(Text, Words(1, 0));

        // [zm 3.3.1]
        Assert.Throws<InvalidDataException>(() => story.Decoder().Decode(Text));
    }

    [Fact]
    public void AStoryMaySupplyItsOwnAlphabetTableFromVersion5()
    {
        var story = new Story(ZMachineVersion.V5);

        // [zm 3.5.5.1] Three blocks of 26. Every entry here is a letter
        // the default table would never produce at that position.
        var table = new byte[AlphabetTable.LengthInBytes];
        Array.Fill(table, (byte)'x', 0, 26);
        Array.Fill(table, (byte)'y', 26, 26);
        Array.Fill(table, (byte)'q', 52, 26);
        story.Put(AlphabetTableAddress, table);

        // [zm 3.5.5] The word at $34 points at it.
        story.PutWord(0x34, AlphabetTableAddress);

        story.Put(Text, Words(L('a'), 4, L('a'), 5, 8));
        var decoder = story.Decoder();

        Assert.Equal("xyq", decoder.Decode(Text));

        // [zm 3.5.5.1] Z-characters 6 and 7 of A2 keep their meanings no
        // matter what the table says about them.
        story.Put(Text, Words(5, 7));
        Assert.Equal("\n", decoder.Decode(Text));

        story.Put(Text, Words(5, 6, 2, 0));
        Assert.Equal("@", decoder.Decode(Text));
    }

    [Fact]
    public void TheAlphabetTableWordIsIgnoredBeforeVersion5()
    {
        var story = new Story(ZMachineVersion.V4);

        var table = new byte[AlphabetTable.LengthInBytes];
        Array.Fill(table, (byte)'x');
        story.Put(AlphabetTableAddress, table);
        story.PutWord(0x34, AlphabetTableAddress);

        story.Put(Text, Words(L('a')));

        // [zm 3.5.5] Only Version 5 and later look at $34.
        Assert.Equal("a", story.Decoder().Decode(Text));
    }

    [Fact]
    public void ExtraCharactersUseTheDefaultUnicodeTable()
    {
        // [zm 3.8.5.3] ZSCII 155 is a-diaeresis and 223 is the inverted
        // question mark, the last entry. 224 is undefined and prints as
        // nothing. Each is reached through the escape as top and bottom
        // five bits.
        Assert.Equal("ä", Decode(ZMachineVersion.V3, 5, 6, 155 >> 5, 155 & 31));
        Assert.Equal("¿", Decode(ZMachineVersion.V3, 5, 6, 223 >> 5, 223 & 31));
        Assert.Equal("", Decode(ZMachineVersion.V3, 5, 6, 224 >> 5, 224 & 31));
    }

    [Fact]
    public void AStoryMaySupplyItsOwnUnicodeTableFromVersion5()
    {
        var story = new Story(ZMachineVersion.V5);

        // [zm 3.8.5.2.1] A count byte, then that many words. Two Cyrillic
        // letters here, which the default table could never produce.
        story.Bytes[UnicodeTable] = 2;
        story.PutWord(UnicodeTable + 1, 0x0416);
        story.PutWord(UnicodeTable + 3, 0x0436);

        // [zm 3.8.5.2] Reached through word 3 of the header extension
        // table, [zm 11.1.7] whose word 0 is the count of words after it.
        story.PutWord(0x36, ExtensionTable);
        story.PutWord(ExtensionTable, 3);
        story.PutWord(ExtensionTable + 6, UnicodeTable);

        var decoder = story.Decoder();
        Assert.Equal(2, decoder.ExtraCharacters.Count);

        story.Put(Text, Words(5, 6, 155 >> 5, 155 & 31, 5, 6, 156 >> 5, 156 & 31));
        Assert.Equal("Жж", decoder.Decode(Text));

        // [zm 3.8.5.2.2] Only 155 to 155+N-1 are defined, so 157 is not.
        story.Put(Text, Words(5, 6, 157 >> 5, 157 & 31));
        Assert.Equal("", decoder.Decode(Text));
    }

    [Fact]
    public void TheUnicodeTableIsIgnoredBeforeVersion5()
    {
        var story = new Story(ZMachineVersion.V4);

        story.Bytes[UnicodeTable] = 1;
        story.PutWord(UnicodeTable + 1, 0x0416);
        story.PutWord(0x36, ExtensionTable);
        story.PutWord(ExtensionTable, 3);
        story.PutWord(ExtensionTable + 6, UnicodeTable);

        story.Put(Text, Words(5, 6, 155 >> 5, 155 & 31));

        // [zm 3.8.5.2] Under Versions 1 to 4 the default table is always
        // used, so this is still a-diaeresis.
        Assert.Equal("ä", story.Decoder().Decode(Text));
    }

    [Fact]
    public void AUnicodeTableCannotOverrunTheExtraCharacterRange()
    {
        var story = new Story(ZMachineVersion.V5);

        // [zm 3.8.5] 155 to 251 is 97 codes, so 98 is one too many.
        story.Bytes[UnicodeTable] = 98;
        story.PutWord(0x36, ExtensionTable);
        story.PutWord(ExtensionTable, 3);
        story.PutWord(ExtensionTable + 6, UnicodeTable);

        Assert.Throws<InvalidDataException>(() => story.Decoder());
    }

    [Fact]
    public void ZsciiCodesTurnIntoTheCharactersTheStandardDefines()
    {
        var table = UnicodeTranslationTable.Default;

        // [zm 3.8.2.1] Null prints nothing.
        Assert.Null(Zscii.ToUnicode(0, ZMachineVersion.V5, table));

        // [zm 3.8.2.3] and [zm 3.8.2.4] exist in Version 6 only.
        Assert.Null(Zscii.ToUnicode(9, ZMachineVersion.V5, table));
        Assert.Equal('\t', Zscii.ToUnicode(9, ZMachineVersion.V6, table));
        Assert.Null(Zscii.ToUnicode(11, ZMachineVersion.V5, table));
        Assert.Equal(' ', Zscii.ToUnicode(11, ZMachineVersion.V6, table));

        // [zm 3.8.2.5]
        Assert.Equal('\n', Zscii.ToUnicode(13, ZMachineVersion.V3, table));

        // [zm 3.8.3] ASCII agrees, including the ends of the range.
        Assert.Equal(' ', Zscii.ToUnicode(32, ZMachineVersion.V3, table));
        Assert.Equal('A', Zscii.ToUnicode(65, ZMachineVersion.V3, table));
        Assert.Equal('~', Zscii.ToUnicode(126, ZMachineVersion.V3, table));

        // [zm 3.8.3.1] 127 and 128 are undefined, [zm 3.8.4] 129 to 154
        // are input only, [zm 3.8.6] 252 to 254 are input only, and
        // [zm 3.8.7] 255 is undefined.
        Assert.Null(Zscii.ToUnicode(127, ZMachineVersion.V3, table));
        Assert.Null(Zscii.ToUnicode(128, ZMachineVersion.V3, table));
        Assert.Null(Zscii.ToUnicode(133, ZMachineVersion.V3, table));
        Assert.Null(Zscii.ToUnicode(254, ZMachineVersion.V3, table));
        Assert.Null(Zscii.ToUnicode(255, ZMachineVersion.V3, table));

        // [zm 3.8.1] Nothing from 256 up is defined.
        Assert.Null(Zscii.ToUnicode(256, ZMachineVersion.V3, table));
        Assert.Null(Zscii.ToUnicode(1023, ZMachineVersion.V3, table));
    }

    [Fact]
    public void AUnicodeTableEntryThatNamesAControlCodeIsUndefined()
    {
        var story = new Story(ZMachineVersion.V5);

        // [zm 3.8.5.4.5] U+0007 is a control code that must not be used,
        // and printing it would ring a terminal bell.
        story.Bytes[UnicodeTable] = 1;
        story.PutWord(UnicodeTable + 1, 0x0007);
        story.PutWord(0x36, ExtensionTable);
        story.PutWord(ExtensionTable, 3);
        story.PutWord(ExtensionTable + 6, UnicodeTable);

        story.Put(Text, Words(5, 6, 155 >> 5, 155 & 31));

        Assert.Equal("", story.Decoder().Decode(Text));
    }

    [Fact]
    public void DecodeZsciiProducesCodesRatherThanCharacters()
    {
        var story = new Story(ZMachineVersion.V3);
        story.Put(Text, Words(L('a'), 5, 7, 0));

        var zscii = new List<ushort>();
        var end = story.Decoder().DecodeZscii(Text, zscii);

        // The letter, the newline as ZSCII 13, and the space, with the
        // Unicode step not applied.
        Assert.Equal(new ushort[] { 'a', Zscii.Newline, Zscii.Space }, zscii);
        Assert.Equal(Text + 4, end);
    }
}

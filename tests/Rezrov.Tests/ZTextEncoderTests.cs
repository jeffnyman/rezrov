using Rezrov.ZMachine;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

public class ZTextEncoderTests
{
    private static ZTextEncoder Encoder(ZMachineVersion version) =>
        new(version, version == ZMachineVersion.V1 ? AlphabetTable.Version1 : AlphabetTable.Default);

    private static byte[] Encode(ZMachineVersion version, string word) => Encoder(version).EncodeWord(word);

    [Fact]
    public void EncodesTheStandardsWorkedExample()
    {
        // [zm 3.7] The example: "i" is Z-character 14 followed by eight
        // 5s. The standard prints the first word as $48a5, and the
        // editor's note shows that is a long-standing misprint: packing
        // 14, 5, 5 gives $38a5. The other two words are as printed.
        Assert.Equal(new byte[] { 0x38, 0xA5, 0x14, 0xA5, 0x94, 0xA5 }, Encode(ZMachineVersion.V5, "i"));

        // [zm 3.7] Six Z-characters before Version 4, so two words.
        Assert.Equal(new byte[] { 0x38, 0xA5, 0x94, 0xA5 }, Encode(ZMachineVersion.V3, "i"));
    }

    [Theory]
    [InlineData(ZMachineVersion.V3, 6, 4)]
    [InlineData(ZMachineVersion.V5, 9, 6)]
    public void TheEncodedSizeFollowsTheVersion(ZMachineVersion version, int zCharacters, int bytes)
    {
        // [zm 13.3] and [zm 13.4]
        var encoder = Encoder(version);

        Assert.Equal(zCharacters, encoder.ZCharacterCount);
        Assert.Equal(bytes, encoder.EncodedLength);
        Assert.Equal(bytes, encoder.EncodeWord("x").Length);
    }

    [Fact]
    public void EncodesLowerCaseLettersWithoutShifts()
    {
        // 'h','e','l','l','o' are 13,10,17,17,20, then one pad.
        Assert.Equal(ZChars.Words(13, 10, 17, 17, 20, 5), Encode(ZMachineVersion.V3, "hello"));
    }

    [Fact]
    public void EncodesCharactersOutsideEveryAlphabetWithTheEscape()
    {
        // [zm 3.4] '@' is ZSCII 64: shift to A2, escape, 2, 0.
        Assert.Equal(ZChars.Words(5, 6, 2, 0, 5, 5), Encode(ZMachineVersion.V3, "@"));
    }

    [Fact]
    public void EncodesUpperCaseAndPunctuationWithSingleShiftsFromVersion3()
    {
        // [zm 3.2.3] 4 for one character in A1, 5 for one in A2. The
        // encoder does not lower-case, so a capital costs a shift and a
        // digit, which is only in A2, costs another.
        Assert.Equal(ZChars.Words(4, 6, 5, 8, 6, 5), Encode(ZMachineVersion.V3, "A0a"));
    }

    [Theory]
    [InlineData(ZMachineVersion.V1)]
    [InlineData(ZMachineVersion.V2)]
    public void UsesATemporaryShiftForALoneCharacterInVersions1And2(ZMachineVersion version)
    {
        // [zm 3.7.1] Only one character comes from A2, so a single shift:
        // from A0, forward two to A2 is Z-character 3. Z-character 28 is
        // '-' in both the Version 1 and Version 2 tables.
        Assert.Equal(ZChars.Words(6, 3, 28, 7, 5, 5), Encode(version, "a-b"));
    }

    [Theory]
    [InlineData(ZMachineVersion.V1)]
    [InlineData(ZMachineVersion.V2)]
    public void UsesAShiftLockForARunOfCharactersInVersions1And2(ZMachineVersion version)
    {
        // [zm 3.7.1] Two characters in a row from A2, so a shift lock:
        // forward two is Z-character 5, and it stays in force, so the
        // second needs nothing. Getting back to A0 from A2 is forward one,
        // and with two letters following that is a lock too, Z-character 4.
        Assert.Equal(ZChars.Words(5, 28, 28, 4, 6, 7), Encode(version, "--ab"));
    }

    [Fact]
    public void CutsOffAtTheFixedLengthLeavingConstructionsIncomplete()
    {
        // [zm 3.7] "abc@" is 3 letters and a 4 Z-character escape, one too
        // many for Version 3. The escape is left incomplete rather than
        // dropped: a, b, c, 5, 6, 2 and no room for the 0.
        Assert.Equal(ZChars.Words(6, 7, 8, 5, 6, 2), Encode(ZMachineVersion.V3, "abc@"));

        // Nine letters fit exactly in Version 5 and the tenth is lost.
        Assert.Equal(ZChars.Words(6, 7, 8, 9, 10, 11, 12, 13, 14), Encode(ZMachineVersion.V5, "abcdefghij"));
    }

    [Fact]
    public void PadsWithFives()
    {
        // [zm 3.7] The pad character, if needed, must be 5.
        Assert.Equal(ZChars.Words(6, 5, 5, 5, 5, 5, 5, 5, 5), Encode(ZMachineVersion.V5, "a"));
        Assert.Equal(ZChars.Words(5, 5, 5, 5, 5, 5), Encode(ZMachineVersion.V3, ""));
    }

    [Fact]
    public void UsesTheStorysOwnAlphabetTable()
    {
        // A Version 5 table where 'x' is the first letter of A0 and 'q'
        // is the first usable slot of A2.
        var table = new byte[AlphabetTable.LengthInBytes];
        Array.Fill(table, (byte)'x', 0, 26);
        Array.Fill(table, (byte)'y', 26, 26);
        Array.Fill(table, (byte)'q', 52, 26);

        var memory = new ZMemory(new byte[256]);
        for (var i = 0; i < table.Length; i++)
        {
            memory.WriteByte(64 + i, table[i]);
        }

        var encoder = new ZTextEncoder(ZMachineVersion.V5, AlphabetTable.Read(memory, 64));

        // 'x' is Z-character 6 in A0. 'q' is found at Z-character 8 of A2,
        // since 6 is the escape and 7 was overwritten with the newline.
        Assert.Equal(ZChars.Words(6, 5, 8, 5, 5, 5, 5, 5, 5), encoder.EncodeWord("xq"));

        // 'a' is in no alphabet at all now, so it takes the escape.
        Assert.Equal(ZChars.Words(5, 6, 'a' >> 5, 'a' & 31, 5, 5, 5, 5, 5), encoder.EncodeWord("a"));
    }

    [Fact]
    public void RejectsNonAsciiStrings()
    {
        Assert.Throws<ArgumentException>(() => Encode(ZMachineVersion.V5, "ä"));
    }

    [Fact]
    public void EncodesExtraCharactersGivenAsZscii()
    {
        // ZSCII 155 is a-diaeresis in the default table, which is in no
        // alphabet, so it is escaped: 155 >> 5 is 4 and 155 & 31 is 27.
        Assert.Equal(
            ZChars.Words(5, 6, 4, 27, 5, 5, 5, 5, 5),
            Encoder(ZMachineVersion.V5).EncodeWord(new byte[] { 155 }));
    }
}

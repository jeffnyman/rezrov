using Rezrov.Glulx;

namespace Rezrov.Tests;

public class GlulxHeaderTests
{
    private static GlulxHeader Header(byte[] file) => new(file);

    [Fact]
    public void ReadsTheNineFields()
    {
        var file = TestGlulx.File(
            ramStart: 0x200,
            extStart: 0x400,
            endMem: 0x600,
            stackSize: 0x800,
            startFunction: 0x3C,
            decodingTable: 0x24C,
            version: 0x00030102);

        var header = Header(file);

        // [glulx #the-header] Nine big-endian words, in this order.
        Assert.Equal(0x00030102u, header.Version);
        Assert.Equal(0x200u, header.RamStart);
        Assert.Equal(0x400u, header.ExtStart);
        Assert.Equal(0x600u, header.EndMem);
        Assert.Equal(0x800u, header.StackSize);
        Assert.Equal(0x3Cu, header.StartFunction);
        Assert.Equal(0x24Cu, header.DecodingTable);
        Assert.Equal(header.ComputeChecksum(file), header.Checksum);
    }

    [Fact]
    public void SplitsTheVersionIntoItsParts()
    {
        var header = Header(TestGlulx.File(version: 0x00030102));

        // [glulx #the-header] Major in the upper 16 bits, then a byte
        // each for minor and subminor.
        Assert.Equal((3, 1, 2), (header.MajorVersion, header.MinorVersion, header.SubminorVersion));
        Assert.Equal("3.1.2", header.VersionText);
    }

    [Fact]
    public void RejectsAFileTooShortForTheHeader()
    {
        var file = TestGlulx.File()[..35];

        var e = Assert.Throws<InvalidDataException>(() => Header(file));
        Assert.Contains("36 byte header", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTheWrongMagicNumber()
    {
        var file = TestGlulx.File();
        "Gulp"u8.CopyTo(file);

        // [glulx #the-header] The interpreter validates the magic number.
        var e = Assert.Throws<InvalidDataException>(() => Header(file));
        Assert.Contains("'Glul'", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0x00020000u)]
    [InlineData(0x00020001u)]
    [InlineData(0x00030000u)]
    [InlineData(0x00030103u)]
    [InlineData(0x000301FFu)]
    public void AcceptsVersions2Point0Through3Point1(uint version)
    {
        // [glulx #the-header] 2.0.0 to 3.1.255 inclusive: the major
        // version must match, the minor must not exceed the
        // specification's, the subminor does not matter, and 2.0 files
        // are accepted by exception since they only lack Unicode.
        Assert.Equal(version, Header(TestGlulx.File(version: version)).Version);
    }

    [Theory]
    [InlineData(0x0001FFFFu, "too old")]
    [InlineData(0x00010000u, "too old")]
    [InlineData(0x00030200u, "too new")]
    [InlineData(0x00040000u, "too new")]
    public void RejectsVersionsOutsideThatRange(uint version, string reason)
    {
        var e = Assert.Throws<InvalidDataException>(() => Header(TestGlulx.File(version: version)));
        Assert.Contains(reason, e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0x180u, 0x200u, 0x300u, 0x100u, "RAMSTART")]
    [InlineData(0x100u, 0x280u, 0x300u, 0x100u, "EXTSTART")]
    [InlineData(0x100u, 0x200u, 0x380u, 0x100u, "ENDMEM")]
    [InlineData(0x100u, 0x200u, 0x300u, 0x180u, "stack size")]
    public void RejectsUnalignedBoundaries(uint ramStart, uint extStart, uint endMem, uint stackSize, string field)
    {
        // [glulx #the-memory-map] The three boundaries must be aligned on
        // 256-byte boundaries, [glulx #stack] and the stack size must be
        // a multiple of 256.
        var e = Assert.Throws<InvalidDataException>(
            () => Header(TestGlulx.File(ramStart, extStart, endMem, stackSize, length: 0x400)));
        Assert.Contains(field, e.Message, StringComparison.Ordinal);
        Assert.Contains("not a multiple of 100", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresRomToHoldTheHeader()
    {
        // [glulx #the-memory-map] ROM must be at least 256 bytes long.
        var e = Assert.Throws<InvalidDataException>(() => Header(TestGlulx.File(ramStart: 0)));
        Assert.Contains("ROM must be at least 100 bytes", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0x300u, 0x200u, 0x300u)]
    [InlineData(0x100u, 0x300u, 0x200u)]
    public void RequiresTheBoundariesInOrder(uint ramStart, uint extStart, uint endMem)
    {
        // [glulx #the-memory-map] ROM, then RAM, then the extension.
        var e = Assert.Throws<InvalidDataException>(
            () => Header(TestGlulx.File(ramStart, extStart, endMem, length: 0x400)));
        Assert.Contains("out of order", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AllowsEmptySegments()
    {
        // [glulx #the-memory-map] Any segment but ROM may be zero-length.
        var header = Header(TestGlulx.File(ramStart: 0x200, extStart: 0x200, endMem: 0x200));

        Assert.Equal((0x200u, 0x200u, 0x200u), (header.RamStart, header.ExtStart, header.EndMem));
    }

    [Fact]
    public void RequiresAStack()
    {
        // [glulx #stack] Zero is the only aligned size that is too small.
        var e = Assert.Throws<InvalidDataException>(() => Header(TestGlulx.File(stackSize: 0)));
        Assert.Contains("stack must be at least 256 bytes", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAFileShorterThanExtStart()
    {
        // [glulx #the-memory-map] The file stores memory up to EXTSTART,
        // so one that stops short has been cut off.
        var e = Assert.Throws<InvalidDataException>(() => Header(TestGlulx.File(extStart: 0x200, length: 0x1FF)));
        Assert.Contains("should hold at least", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AllowsAFileLongerThanExtStart()
    {
        // The header says what to load; bytes past it are ignored, and
        // they do not enter the checksum either.
        var file = TestGlulx.File(extStart: 0x200, length: 0x210);
        file[0x205] = 0xFF;

        var header = Header(file);

        Assert.Equal(0x200u, header.ExtStart);
        Assert.True(header.VerifyChecksum(file));
    }

    [Fact]
    public void RequiresTheStartFunctionInsideMemory()
    {
        // [glulx #the-header] Execution begins by calling it.
        var e = Assert.Throws<InvalidDataException>(() => Header(TestGlulx.File(startFunction: 0x300)));
        Assert.Contains("start function", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresTheDecodingTableInsideMemoryOrAbsent()
    {
        // [glulx #the-header] Zero means there is no table.
        Assert.Equal(0u, Header(TestGlulx.File(decodingTable: 0)).DecodingTable);
        Assert.Equal(0x2FCu, Header(TestGlulx.File(decodingTable: 0x2FC)).DecodingTable);

        var e = Assert.Throws<InvalidDataException>(() => Header(TestGlulx.File(decodingTable: 0x300)));
        Assert.Contains("decoding table", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheChecksumIsTheSumOfWordsWithItsOwnFieldAsZero()
    {
        var file = TestGlulx.File(extStart: 0x200);
        file[0x150] = 0x12;
        file[0x1FF] = 0x34;
        TestGlulx.PutWord(file, 0x20, 0);
        var sum = TestGlulx.SumOfWords(file, 0x200);
        TestGlulx.PutWord(file, 0x20, sum);

        var header = Header(file);

        // [glulx #the-header] The sum of the initial contents as
        // big-endian 32-bit integers, computed with the field set to
        // zero. The field is part of the sum only as a zero, so the
        // computation subtracts whatever it holds.
        Assert.Equal(sum, header.Checksum);
        Assert.Equal(sum, header.ComputeChecksum(file));
        Assert.True(header.VerifyChecksum(file));

        file[0x151] ^= 0x01;
        Assert.False(header.VerifyChecksum(file));
    }

    [Fact]
    public void ReadsInformsLayoutAfterTheHeader()
    {
        var file = TestGlulx.File();
        "Info"u8.CopyTo(file.AsSpan(0x24));
        file[0x29] = 1;
        "6.43"u8.CopyTo(file.AsSpan(0x2C));
        "0.38"u8.CopyTo(file.AsSpan(0x30));
        file[0x35] = 13;
        "241202"u8.CopyTo(file.AsSpan(0x36));

        var header = Header(file);

        // [glulx #the-header] The word after the header is Inform's
        // 'Info', and what follows is the compiler's own layout.
        Assert.Equal("6.43", header.InformVersion);
        Assert.Equal(13, header.InformRelease);
        Assert.Equal("241202", header.InformSerial);
    }

    [Fact]
    public void AFileWithoutInformsLayoutHasNoInformFields()
    {
        var header = Header(TestGlulx.File());

        Assert.Null(header.InformVersion);
        Assert.Null(header.InformRelease);
        Assert.Null(header.InformSerial);
    }
}

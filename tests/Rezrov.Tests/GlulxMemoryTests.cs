using Rezrov.Glulx;

namespace Rezrov.Tests;

public class GlulxMemoryTests
{
    // ROM to $100, RAM to $200, the extension to $300.
    private static GlulxMemory Memory(byte[]? file = null) => new(file ?? TestGlulx.File());

    [Fact]
    public void LoadsTheFileAndZeroesTheExtension()
    {
        var file = TestGlulx.File(extStart: 0x200, endMem: 0x300);
        file[0x1FF] = 0xAB;

        var memory = Memory(file);

        // [glulx #the-memory-map] The file holds memory up to EXTSTART,
        // and everything above it up to ENDMEM starts as zeroes.
        Assert.Equal(0x300u, memory.Length);
        Assert.Equal(0x100u, memory.RamStart);
        Assert.Equal(0xAB, memory.ReadByte(0x1FF));
        Assert.Equal(0, memory.ReadByte(0x200));
        Assert.Equal(0, memory.ReadByte(0x2FF));
    }

    [Fact]
    public void ReadsBigEndianValuesOfEachSize()
    {
        var file = TestGlulx.File();
        file[0x180] = 0x12;
        file[0x181] = 0x34;
        file[0x182] = 0x56;
        file[0x183] = 0x78;

        var memory = Memory(file);

        // [glulx #the-machine] Most significant byte first, and no
        // alignment is required, so a word may start on an odd address.
        Assert.Equal(0x12, memory.ReadByte(0x180));
        Assert.Equal(0x1234, memory.ReadShort(0x180));
        Assert.Equal(0x3456, memory.ReadShort(0x181));
        Assert.Equal(0x12345678u, memory.ReadWord(0x180));
    }

    [Fact]
    public void WritesBigEndianValuesToRam()
    {
        var memory = Memory();

        memory.WriteWord(0x200, 0x12345678);
        memory.WriteShort(0x204, 0x9ABC);
        memory.WriteByte(0x206, 0xDE);

        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE }, memory.Slice(0x200, 7).ToArray());
    }

    [Theory]
    [InlineData(0x0FFu, 1u)]
    [InlineData(0x0FEu, 2u)]
    [InlineData(0x0FCu, 4u)]
    [InlineData(0x000u, 4u)]
    public void RefusesToWriteIntoRom(uint address, uint size)
    {
        var memory = Memory();

        // [glulx #the-memory-map] It is illegal to write to ROM, which
        // is everything below RAMSTART, and a value that begins there is
        // refused even if it would end in RAM.
        var e = Assert.Throws<GlulxException>(() => Write(memory, address, size));
        Assert.Contains("Write to ROM", e.Message, StringComparison.Ordinal);

        // The first RAM address is fine for every size.
        Write(memory, 0x100, size);
    }

    [Fact]
    public void RefusesAccessOutsideMemory()
    {
        var memory = Memory();

        // The last byte is readable, and the byte after it is not, at
        // every size.
        Assert.Equal(0, memory.ReadByte(0x2FF));
        Assert.Equal(0, memory.ReadShort(0x2FE));
        Assert.Equal(0u, memory.ReadWord(0x2FC));

        Assert.Throws<GlulxException>(() => memory.ReadByte(0x300));
        Assert.Throws<GlulxException>(() => memory.ReadShort(0x2FF));
        Assert.Throws<GlulxException>(() => memory.ReadWord(0x2FD));
        Assert.Throws<GlulxException>(() => memory.WriteByte(0x300, 0));

        // An address near the top of the 32-bit range must not wrap
        // around into looking valid.
        Assert.Throws<GlulxException>(() => memory.ReadWord(0xFFFFFFFE));
        Assert.Throws<GlulxException>(() => memory.Slice(0xFFFFFF00, 0x200));
    }

    [Fact]
    public void ResetRestoresRamFromTheFileAndClearsTheExtension()
    {
        var file = TestGlulx.File();
        file[0x150] = 0x42;
        var memory = Memory(file);

        memory.WriteByte(0x150, 0x99);
        memory.WriteByte(0x250, 0x77);

        memory.Reset();

        // [glulx op:restart] RAM is as the file had it and the extension
        // is zeroes again.
        Assert.Equal(0x42, memory.ReadByte(0x150));
        Assert.Equal(0, memory.ReadByte(0x250));
    }

    [Fact]
    public void TheChecksumIsOfTheFileNotOfLiveMemory()
    {
        var memory = Memory();
        Assert.True(memory.VerifyChecksum());

        memory.WriteWord(0x180, 0xDEADBEEF);

        // [glulx #the-header] The checksum covers the initial contents of
        // memory, so play cannot break it.
        Assert.True(memory.VerifyChecksum());
        Assert.Equal(memory.Header.Checksum, memory.ComputeChecksum());
    }

    private static void Write(GlulxMemory memory, uint address, uint size)
    {
        switch (size)
        {
            case 1:
                memory.WriteByte(address, 0);
                break;
            case 2:
                memory.WriteShort(address, 0);
                break;
            default:
                memory.WriteWord(address, 0);
                break;
        }
    }
}

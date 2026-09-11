using Rezrov.Glulx;
using Rezrov.Glulx.Instructions;

namespace Rezrov.Tests;

public class GlulxFunctionHeaderTests
{
    private const uint Function = 0x100;

    private static FunctionHeader Read(params byte[] bytes)
    {
        var file = TestGlulx.File();
        bytes.CopyTo(file, (int)Function);
        return FunctionHeader.Read(new GlulxMemory(file), Function);
    }

    [Fact]
    public void ReadsTheTypeAndTheLocalsFormat()
    {
        // [glulx #callframe] The specification's example: three 8-bit
        // locals then six 16-bit ones.
        var header = Read(0xC1, 1, 3, 2, 6, 0, 0, 0x10);

        Assert.Equal(FunctionType.LocalArguments, header.Type);
        Assert.Equal([new LocalsFormatEntry(1, 3), new LocalsFormatEntry(2, 6)], header.LocalsFormat);
        Assert.Equal(9, header.LocalCount);

        // [glulx #function] The code begins right after the zero pair.
        Assert.Equal(Function + 7, header.CodeAddress);
    }

    [Fact]
    public void AFunctionMayHaveNoLocals()
    {
        var header = Read(0xC0, 0, 0, 0x10);

        Assert.Equal(FunctionType.StackArguments, header.Type);
        Assert.Empty(header.LocalsFormat);
        Assert.Equal(0, header.LocalCount);
        Assert.Equal(Function + 3, header.CodeAddress);
    }

    [Fact]
    public void SeveralRunsOfOneSizeAreLegitimate()
    {
        // [glulx #function] More than 255 locals of one type take more
        // than one pair.
        var header = Read(0xC1, 4, 255, 4, 45, 0, 0);

        Assert.Equal(300, header.LocalCount);
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0xC2)]
    [InlineData(0xDF)]
    [InlineData(0xE0)]
    [InlineData(0x70)]
    public void RejectsAnythingButAFunctionType(byte type)
    {
        // [glulx #function] C2 to DF are reserved, 70 is an Inform
        // object, 00 is no object.
        var e = Assert.Throws<GlulxException>(() => Read(type, 0, 0));
        Assert.Contains($"Not a function: type byte {type:X2}", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(8, 1)]
    [InlineData(4, 0)]
    [InlineData(0, 5)]
    public void RejectsABadLocalsFormat(byte localType, byte localCount)
    {
        // [glulx #callframe] Sizes are 1, 2, or 4 and counts 1 to 255.
        var e = Assert.Throws<GlulxException>(() => Read(0xC1, localType, localCount, 0, 0));
        Assert.Contains("Bad locals format", e.Message, StringComparison.Ordinal);
    }
}

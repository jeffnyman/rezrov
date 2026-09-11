using System.Buffers.Binary;
using System.Text;
using Rezrov.Glulx;
using Rezrov.Glulx.Saves;

namespace Rezrov.Tests;

/// <summary>
/// [glulx #saveformat] The Glulx variant of Quetzal on its own: the
/// chunks it writes, the memory size and compression, the identifying
/// header, the stack, the heap chunk, and what a bad file does.
/// </summary>
public class GlulxQuetzalTests
{
    // ROM to $100, RAM to $200, the extension to $300, as TestGlulx
    // lays a file out by default.
    private const uint RamStart = 0x100;
    private const uint EndMem = 0x300;

    private static GlulxMemory Memory() => new(TestGlulx.File());

    /// <summary>
    /// A state of memory at its initial size with a few bytes changed
    /// and a stack of one stub.
    /// </summary>
    private static GlulxSavedState State(GlulxMemory memory, uint size = EndMem)
    {
        var ram = memory.InitialRam(size - RamStart);
        ram[5] ^= 0x42;
        ram[0x1FF] = 0x99;
        var stack = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(stack, 1);
        BinaryPrimitives.WriteUInt32BigEndian(stack.AsSpan(4), 0x104);
        BinaryPrimitives.WriteUInt32BigEndian(stack.AsSpan(8), 0x60);
        return new GlulxSavedState(size, ram, stack);
    }

    private static byte[] Chunk(string id, params byte[] data)
    {
        var chunk = new List<byte>(Encoding.ASCII.GetBytes(id));
        chunk.AddRange(Word((uint)data.Length));
        chunk.AddRange(data);
        if ((data.Length & 1) != 0)
        {
            chunk.Add(0);
        }

        return chunk.ToArray();
    }

    private static byte[] Form(params byte[][] chunks)
    {
        var body = new List<byte>(Encoding.ASCII.GetBytes("IFZS"));
        foreach (var chunk in chunks)
        {
            body.AddRange(chunk);
        }

        return [.. Encoding.ASCII.GetBytes("FORM"), .. Word((uint)body.Count), .. body];
    }

    private static byte[] Word(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Header(GlulxMemory memory) => memory.Slice(0, GlulxQuetzal.HeaderLength).ToArray();

    private static GlulxSavedState Read(byte[] file, GlulxMemory memory) => GlulxQuetzal.Read(new MemoryStream(file), memory);

    [Fact]
    public void IdenticalMemoryCompressesToNothing()
    {
        // [quetzal 3.4] Trailing zeros are dropped, so nothing changed
        // means nothing written.
        var initial = new byte[] { 1, 2, 3, 4 };

        Assert.Empty(GlulxQuetzal.Compress(initial, initial));
        Assert.Equal(initial, GlulxQuetzal.Decompress([], initial));
    }

    [Fact]
    public void CompressionRoundTripsChangesAndLongRuns()
    {
        // [quetzal 3.2] A changed byte is the exclusive-or; a run of
        // unchanged bytes is a zero and a count, at most 256 per pair.
        var initial = new byte[600];
        var current = (byte[])initial.Clone();
        current[0] = 0x55;
        current[300] = 0x01;
        current[599] = 0xFF;

        var compressed = GlulxQuetzal.Compress(current, initial);

        Assert.Equal(new byte[] { 0x55, 0, 255, 0, 42, 0x01, 0, 255, 0, 41, 0xFF }, compressed);
        Assert.Equal(current, GlulxQuetzal.Decompress(compressed, initial));
    }

    [Fact]
    public void DecompressionRejectsOverrunsAndIncompleteRuns()
    {
        // [quetzal 3.5]
        var initial = new byte[4];

        Assert.Throws<InvalidDataException>(() => GlulxQuetzal.Decompress([1, 2, 3, 4, 5], initial));
        Assert.Throws<InvalidDataException>(() => GlulxQuetzal.Decompress([0, 9], initial));
        Assert.Throws<InvalidDataException>(() => GlulxQuetzal.Decompress([1, 0], initial));
        Assert.Throws<ArgumentException>(() => GlulxQuetzal.Compress(new byte[3], initial));
    }

    [Fact]
    public void WritesTheChunksTheSpecificationNames()
    {
        var memory = Memory();
        var state = State(memory);
        var file = new MemoryStream();

        GlulxQuetzal.Write(state, memory, file);

        // [glulx #saveformat] IFhd is the first 128 bytes of memory,
        // CMem starts with the size of memory and then the compressed
        // RAM, which here is a run of five, one changed byte, a run of
        // 505 in two pairs, and the last byte, and Stks is the stack as
        // it is. No heap, so no MAll.
        var expected = Form(
            Chunk("IFhd", Header(memory)),
            Chunk("CMem", [.. Word(EndMem), 0, 4, 0x42, 0, 0xFF, 0, 0xF8, 0x99]),
            Chunk("Stks", state.Stack));

        Assert.Equal(expected, file.ToArray());
    }

    [Fact]
    public void ASavedGameRoundTrips()
    {
        var memory = Memory();
        var state = State(memory);
        var file = new MemoryStream();

        GlulxQuetzal.Write(state, memory, file);
        file.Position = 0;
        var read = GlulxQuetzal.Read(file, memory);

        Assert.Equal(state.MemorySize, read.MemorySize);
        Assert.Equal(state.Ram, read.Ram);
        Assert.Equal(state.Stack, read.Stack);
    }

    [Fact]
    public void GrownMemoryIsSavedAgainstTheFileExtendedWithZeroes()
    {
        // [glulx #saveformat] Memory may be larger than ENDMEM when
        // saved, and the data above EXTSTART is compressed as if the
        // game file were extended with zeroes.
        var memory = Memory();
        var state = State(memory, 0x400);
        state.Ram[0x2FF] = 0x11;
        var file = new MemoryStream();

        GlulxQuetzal.Write(state, memory, file);
        file.Position = 0;
        var read = GlulxQuetzal.Read(file, memory);

        Assert.Equal(0x400u, read.MemorySize);
        Assert.Equal(0x300, read.Ram.Length);
        Assert.Equal(0x11, read.Ram[0x2FF]);
        Assert.Equal(state.Ram, read.Ram);
    }

    [Fact]
    public void ReadsADumpAndSkipsWhatItDoesNotKnow()
    {
        var memory = Memory();
        var state = State(memory);

        // [quetzal 3.8] UMem is the memory as it is, and [quetzal 8.9]
        // an unknown chunk, of odd length and so padded, is skipped, as
        // is [glulx #saveformat] an MAll chunk with no blocks.
        var file = Form(
            Chunk("IFhd", Header(memory)),
            Chunk("ANNO", [1, 2, 3]),
            Chunk("MAll", [.. Word(0), .. Word(0)]),
            Chunk("UMem", [.. Word(EndMem), .. state.Ram]),
            Chunk("Stks", state.Stack));

        var read = Read(file, memory);

        Assert.Equal(state.Ram, read.Ram);
        Assert.Equal(state.Stack, read.Stack);
    }

    [Fact]
    public void RejectsWhatIsNotASavedGameOfThisGame()
    {
        var memory = Memory();
        var state = State(memory);
        var other = Header(memory);
        other[0x30] ^= 1;

        // Not an IFZS form at all, or one saved from a game whose first
        // 128 bytes differ.
        Assert.Throws<InvalidDataException>(() => Read([.. "FORM"u8, 0, 0, 0, 4, .. "IFRS"u8], memory));
        Assert.Throws<InvalidDataException>(() => Read(Form(Chunk("IFhd", other), Chunk("UMem", [.. Word(EndMem), .. state.Ram]), Chunk("Stks", state.Stack)), memory));
        Assert.Throws<InvalidDataException>(() => Read(Form(Chunk("IFhd", other[..100]), Chunk("UMem", [.. Word(EndMem), .. state.Ram]), Chunk("Stks", state.Stack)), memory));
    }

    [Fact]
    public void RejectsMissingAndMangledChunks()
    {
        var memory = Memory();
        var state = State(memory);
        var header = Chunk("IFhd", Header(memory));
        var umem = Chunk("UMem", [.. Word(EndMem), .. state.Ram]);
        var stacks = Chunk("Stks", state.Stack);

        // [quetzal 7.18] Each required chunk missing in turn, a chunk
        // running past the end, and a truncated file.
        Assert.Throws<InvalidDataException>(() => Read(Form(umem, stacks), memory));
        Assert.Throws<InvalidDataException>(() => Read(Form(header, stacks), memory));
        Assert.Throws<InvalidDataException>(() => Read(Form(header, umem), memory));
        Assert.Throws<InvalidDataException>(() => Read(Form(header, umem, Chunk("Stks", state.Stack)[..12]), memory));
        Assert.Throws<InvalidDataException>(() => Read(Form(header, umem, stacks)[..^4], memory));
    }

    [Fact]
    public void RejectsAMemorySizeTheGameCannotHave()
    {
        var memory = Memory();
        var state = State(memory);
        var header = Chunk("IFhd", Header(memory));
        var stacks = Chunk("Stks", state.Stack);

        // [glulx op:setmemsize] A multiple of 256 and at least ENDMEM,
        // and [quetzal 3.6] a dump exactly fills it.
        Assert.Throws<InvalidDataException>(() => Read(Form(header, Chunk("UMem", [.. Word(0x280), .. state.Ram[..0x180]]), stacks), memory));
        Assert.Throws<InvalidDataException>(() => Read(Form(header, Chunk("UMem", [.. Word(0x200), .. state.Ram[..0x100]]), stacks), memory));
        Assert.Throws<InvalidDataException>(() => Read(Form(header, Chunk("UMem", [.. Word(0x400), .. state.Ram]), stacks), memory));
        Assert.Throws<InvalidDataException>(() => Read(Form(header, Chunk("CMem", [.. Word(0x400), .. new byte[0x301]]), stacks), memory));
        Assert.Throws<InvalidDataException>(() => Read(Form(header, Chunk("CMem", [0, 0]), stacks), memory));
    }

    [Fact]
    public void RejectsAStackThatIsNotWhole()
    {
        var memory = Memory();
        var state = State(memory);
        var header = Chunk("IFhd", Header(memory));
        var umem = Chunk("UMem", [.. Word(EndMem), .. state.Ram]);

        // [glulx #saveformat] Whole values, and at least a stub.
        Assert.Throws<InvalidDataException>(() => Read(Form(header, umem, Chunk("Stks", state.Stack[..14])), memory));
        Assert.Throws<InvalidDataException>(() => Read(Form(header, umem, Chunk("Stks", state.Stack[..12])), memory));
    }

    [Fact]
    public void AHeapWithBlocksCannotBeRestoredYet()
    {
        var memory = Memory();
        var state = State(memory);
        var header = Chunk("IFhd", Header(memory));
        var umem = Chunk("UMem", [.. Word(EndMem), .. state.Ram]);
        var stacks = Chunk("Stks", state.Stack);

        // [glulx #saveformat] MAll with one block needs the heap, which
        // is not built, and a heap chunk too short to say is mangled.
        Assert.Throws<NotSupportedException>(() => Read(Form(header, Chunk("MAll", [.. Word(0x300), .. Word(1), .. Word(0x300), .. Word(0x10)]), umem, stacks), memory));
        Assert.Throws<InvalidDataException>(() => Read(Form(header, Chunk("MAll", [1, 2, 3, 4]), umem, stacks), memory));
    }

    [Fact]
    public void MemoryGivesItsInitialRamAndTakesARestoredOne()
    {
        var memory = Memory();
        memory.WriteByte(RamStart + 3, 0x77);

        // The file, not live memory, and zeroes above the file's end.
        var initial = memory.InitialRam(0x300);
        Assert.Equal(0x300, initial.Length);
        Assert.Equal(0, initial[3]);
        Assert.Equal(memory.Slice(RamStart, 0x100)[..3].ToArray(), initial[..3]);
        Assert.All(initial[0x100..], b => Assert.Equal(0, b));

        // A restore sets the size and the contents together.
        var ram = new byte[0x300];
        ram[0x2FF] = 0x11;
        memory.Restore(0x400, ram);
        Assert.Equal(0x400u, memory.Length);
        Assert.Equal(0, memory.ReadByte(RamStart + 3));
        Assert.Equal(0x11, memory.ReadByte(0x3FF));

        Assert.Throws<GlulxException>(() => memory.Restore(0x400, new byte[0x200]));
        Assert.Throws<GlulxException>(() => memory.Restore(0x280, new byte[0x180]));
    }
}

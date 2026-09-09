using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Saves;

namespace Rezrov.Tests;

/// <summary>
/// The Quetzal format on its own: the memory compression, the stack
/// chunk, the story check, and what a bad file does.
/// </summary>
public class QuetzalTests
{
    [Fact]
    public void IdenticalMemoryCompressesToNothing()
    {
        // [quetzal 3.4] Trailing zeros are dropped, so nothing changed
        // means nothing written.
        var original = new byte[] { 1, 2, 3, 4 };

        Assert.Empty(Quetzal.Compress(original, original));
        Assert.Equal(original, Quetzal.Decompress([], original));
    }

    [Fact]
    public void CompressionRoundTripsChangesAndLongRuns()
    {
        // [quetzal 3.2] A changed byte is the exclusive-or; a run of
        // unchanged bytes is a zero and a count, at most 256 per pair.
        var original = new byte[600];
        var current = (byte[])original.Clone();
        current[0] = 0x55;
        current[300] = 0x01;
        current[599] = 0xFF;

        var compressed = Quetzal.Compress(current, original);

        Assert.Equal(new byte[] { 0x55, 0, 255, 0, 42, 0x01, 0, 255, 0, 41, 0xFF }, compressed);
        Assert.Equal(current, Quetzal.Decompress(compressed, original));
    }

    [Fact]
    public void DecompressionRejectsOverrunsAndIncompleteRuns()
    {
        // [quetzal 3.5]
        var original = new byte[4];

        Assert.Throws<InvalidDataException>(() => Quetzal.Decompress([1, 2, 3, 4, 5], original));
        Assert.Throws<InvalidDataException>(() => Quetzal.Decompress([0, 9], original));
        Assert.Throws<InvalidDataException>(() => Quetzal.Decompress([1, 0], original));
    }

    [Fact]
    public void ASavedGameRoundTripsThroughAFile()
    {
        var (state, memory, header) = Play(ZMachineVersion.V5);
        var snapshot = state.Snapshot() with { ProgramCounter = 0x1234 };
        var file = new MemoryStream();

        Quetzal.Write(snapshot, header, state.OriginalDynamicMemory, file);
        file.Position = 0;
        var read = Quetzal.Read(file, header, state.OriginalDynamicMemory);

        // [quetzal 7.18] Three chunks and 36 bytes of overhead at least.
        Assert.True(file.Length >= 36);
        Assert.Equal(snapshot.DynamicMemory, read.DynamicMemory);
        Assert.Equal(0x1234, read.ProgramCounter);
        Assert.Equal(snapshot.Stack, read.Stack);
        Assert.Equal(snapshot.Frames.Count, read.Frames.Count);
        for (var i = 0; i < snapshot.Frames.Count; i++)
        {
            Assert.Equal(snapshot.Frames[i], read.Frames[i] with { Locals = snapshot.Frames[i].Locals });
            Assert.Equal(snapshot.Frames[i].Locals, read.Frames[i].Locals);
        }

        // And back into a machine.
        var fresh = new GameState(memory, header);
        fresh.Restore(read);
        Assert.Equal(0x1234, fresh.ProgramCounter);
        Assert.Equal(3, fresh.FrameNumber);
        Assert.Equal(8, fresh.ReadVariable(1));
        Assert.Equal(9, fresh.ReadVariable(2));
        Assert.Equal(0xBEEF, fresh.Pop());
        Assert.True(fresh.ArgumentWasSupplied(2));
        Assert.False(fresh.ArgumentWasSupplied(3));
        Assert.Equal(0x4242, fresh.ReadGlobal(0x10));
    }

    [Fact]
    public void TheDummyFrameCarriesTheTopLevelStack()
    {
        // [quetzal 4.11] Outside Version 6 the first frame is a dummy
        // holding whatever was pushed before any call.
        var (state, _, header) = Play(ZMachineVersion.V5);
        var file = new MemoryStream();
        Quetzal.Write(state.Snapshot(), header, state.OriginalDynamicMemory, file);

        var stks = Find(file.ToArray(), "Stks");
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 0x11, 0x11 }, file.ToArray().AsSpan(stks, 10).ToArray());
    }

    [Fact]
    public void AFrameRecordsItsDiscardedResultAndArguments()
    {
        // [quetzal 4.3.2] 000pvvvv with p set for call_xN, [quetzal 4.6]
        // a zero store variable then, and [quetzal 4.7] a bit per
        // argument.
        var (state, _, header) = Play(ZMachineVersion.V5);
        var file = new MemoryStream();
        Quetzal.Write(state.Snapshot(), header, state.OriginalDynamicMemory, file);
        var bytes = file.ToArray();

        // Dummy frame (8 bytes, 1 stack word), then the routine called
        // with a store variable and one argument, then the one called
        // for its effect with two.
        var second = Find(bytes, "Stks") + 10;
        Assert.Equal(0x02, bytes[second + 3]);
        Assert.Equal(5, bytes[second + 4]);
        Assert.Equal(0x01, bytes[second + 5]);

        var third = second + 8 + (2 * 2) + 0;
        Assert.Equal(0x12, bytes[third + 3]);
        Assert.Equal(0, bytes[third + 4]);
        Assert.Equal(0x03, bytes[third + 5]);
    }

    [Fact]
    public void AFileFromAnotherStoryIsRefused()
    {
        // [quetzal 5.3] and [zm 6.1.2.1]
        var (state, memory, header) = Play(ZMachineVersion.V5);
        var file = new MemoryStream();
        Quetzal.Write(state.Snapshot(), header, state.OriginalDynamicMemory, file);

        memory.WriteWord(0x02, 99);
        file.Position = 0;

        var error = Assert.Throws<InvalidDataException>(() => Quetzal.Read(file, header, state.OriginalDynamicMemory));
        Assert.Contains("release 99", error.Message);
    }

    [Fact]
    public void GarbageIsNotASavedGame()
    {
        var (state, _, header) = Play(ZMachineVersion.V5);

        Assert.Throws<InvalidDataException>(() => Quetzal.Read(new MemoryStream("FORM....IFRS"u8.ToArray()), header, state.OriginalDynamicMemory));
        Assert.Throws<InvalidDataException>(() => Quetzal.Read(new MemoryStream(new byte[3]), header, state.OriginalDynamicMemory));
    }

    [Fact]
    public void UncompressedMemoryAndUnknownChunksAreRead()
    {
        // [quetzal 3.8] UMem is a plain dump, [quetzal 8.9] anything
        // unknown is skipped, and [quetzal 8.4.1] odd chunks are padded.
        var (state, _, header) = Play(ZMachineVersion.V5);
        var snapshot = state.Snapshot();
        var file = new MemoryStream();
        Quetzal.Write(snapshot, header, state.OriginalDynamicMemory, file);
        var bytes = file.ToArray();

        // Rebuild the file with an ANNO chunk of odd length before an
        // UMem chunk in place of CMem.
        var cmem = Find(bytes, "CMem") - 8;
        var cmemLength = (bytes[cmem + 4] << 24) | (bytes[cmem + 5] << 16) | (bytes[cmem + 6] << 8) | bytes[cmem + 7];
        var after = cmem + 8 + cmemLength + (cmemLength & 1);

        var rebuilt = new MemoryStream();
        rebuilt.Write(bytes, 0, cmem);
        rebuilt.Write("ANNO"u8);
        rebuilt.Write(new byte[] { 0, 0, 0, 5 });
        rebuilt.Write("hello"u8);
        rebuilt.WriteByte(0);
        rebuilt.Write("UMem"u8);
        var dump = snapshot.DynamicMemory;
        rebuilt.Write(new byte[] { (byte)(dump.Length >> 24), (byte)(dump.Length >> 16), (byte)(dump.Length >> 8), (byte)dump.Length });
        rebuilt.Write(dump);
        rebuilt.Write(bytes, after, bytes.Length - after);
        var all = rebuilt.ToArray();
        var total = all.Length - 8;
        all[4] = (byte)(total >> 24);
        all[5] = (byte)(total >> 16);
        all[6] = (byte)(total >> 8);
        all[7] = (byte)total;

        var read = Quetzal.Read(new MemoryStream(all), header, state.OriginalDynamicMemory);

        Assert.Equal(snapshot.DynamicMemory, read.DynamicMemory);
        Assert.Equal(snapshot.Stack, read.Stack);
    }

    [Fact]
    public void AMissingChunkIsAnError()
    {
        // [quetzal 7.18]
        var (state, _, header) = Play(ZMachineVersion.V5);
        var file = new MemoryStream();
        Quetzal.Write(state.Snapshot(), header, state.OriginalDynamicMemory, file);
        var bytes = file.ToArray();

        var stks = Find(bytes, "Stks") - 8;
        var truncated = bytes[..stks];
        var total = truncated.Length - 8;
        truncated[4] = (byte)(total >> 24);
        truncated[5] = (byte)(total >> 16);
        truncated[6] = (byte)(total >> 8);
        truncated[7] = (byte)total;

        Assert.Throws<InvalidDataException>(() => Quetzal.Read(new MemoryStream(truncated), header, state.OriginalDynamicMemory));
    }

    [Fact]
    public void Version6HasNoDummyFrame()
    {
        // [quetzal 4.11] Execution starts in a routine there, so the
        // first frame is a real one.
        var (state, _, header) = Play(ZMachineVersion.V6);
        var file = new MemoryStream();
        Quetzal.Write(state.Snapshot(), header, state.OriginalDynamicMemory, file);
        var bytes = file.ToArray();

        // The main routine's frame: two locals and a discarded result.
        var stks = Find(bytes, "Stks");
        Assert.Equal(0x12, bytes[stks + 3]);

        file.Position = 0;
        var read = Quetzal.Read(file, header, state.OriginalDynamicMemory);
        Assert.Equal(state.FrameNumber, read.Frames.Count);
    }

    [Fact]
    public void RestoreRejectsAStateThatDoesNotFit()
    {
        var (state, _, _) = Play(ZMachineVersion.V5);
        var snapshot = state.Snapshot();

        Assert.Throws<InvalidDataException>(() => state.Restore(snapshot with { DynamicMemory = new byte[10] }));
        Assert.Throws<InvalidDataException>(() => state.Restore(snapshot with { Frames = [] }));
        Assert.Throws<InvalidDataException>(() => state.Restore(snapshot with { Frames = [new SavedFrame(0, null, [], 0, 99)] }));
    }

    [Fact]
    public void RestoreKeepsFlags2()
    {
        // [zm 6.1.2]
        var (state, memory, _) = Play(ZMachineVersion.V5);
        var snapshot = state.Snapshot();
        memory.WriteWord(0x10, 0x0003);

        state.Restore(snapshot);

        Assert.Equal(0x0003, memory.ReadWord(0x10));
    }

    /// <summary>
    /// A story with a routine at $500 taking two locals, played to a
    /// state with a word on the top-level stack, a call into the routine
    /// storing to variable 5 with one argument, another for its effect
    /// with two, and a word pushed inside.
    /// </summary>
    private static (GameState State, ZMemory Memory, StoryHeader Header) Play(ZMachineVersion version)
    {
        var bytes = new byte[2048];
        bytes[0] = (byte)version;
        PutWord(bytes, 0x02, 7);
        PutWord(bytes, 0x04, 0x0400);
        PutWord(bytes, 0x06, version == ZMachineVersion.V6 ? 0x0500 / 4 : 0x0400);
        PutWord(bytes, 0x08, 0x0380);
        PutWord(bytes, 0x0A, 0x0200);
        PutWord(bytes, 0x0C, 0x0100);
        PutWord(bytes, 0x0E, 0x0400);
        "AB1234"u8.CopyTo(bytes.AsSpan(0x12));
        PutWord(bytes, 0x1C, 0xABCD);
        bytes[0x0381] = 6;
        bytes[0x0500] = 2;

        var memory = new ZMemory(bytes);
        var header = new StoryHeader(memory);
        var state = new GameState(memory, header);

        if (version != ZMachineVersion.V6)
        {
            state.Push(0x1111);
        }

        state.CallRoutine(0x0500 / 4, [7], 5, 0x0450);
        state.CallRoutine(0x0500 / 4, [8, 9], null, 0x0460);
        state.Push(0xBEEF);
        state.WriteGlobal(0x10, 0x4242);

        return (state, memory, header);
    }

    private static void PutWord(byte[] bytes, int address, int value)
    {
        bytes[address] = (byte)(value >> 8);
        bytes[address + 1] = (byte)value;
    }

    /// <summary>The offset of a chunk's data, given its id.</summary>
    private static int Find(byte[] file, string id)
    {
        for (var i = 12; i + 4 <= file.Length; i++)
        {
            if (file[i] == id[0] && file[i + 1] == id[1] && file[i + 2] == id[2] && file[i + 3] == id[3])
            {
                return i + 8;
            }
        }

        throw new InvalidOperationException($"No {id} chunk.");
    }
}

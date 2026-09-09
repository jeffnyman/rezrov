using System.Buffers.Binary;
using System.Text;

namespace Rezrov.ZMachine.Saves;

/// <summary>
/// Reads and writes saved games in Quetzal, Martin Frost's common format
/// for Z-machine saved games.
/// </summary>
/// <remarks>
/// [zm 6.1.1.1] The standard does not specify the format of a saved game,
/// and attaches Quetzal as a highly recommended optional extra, which is
/// what every interpreter of the last thirty years has used. [quetzal 2]
/// A file is an IFF FORM of type IFZS holding three required chunks:
/// [quetzal 5.4] IFhd, which names the story the game was saved from;
/// [quetzal 3.7] CMem or [quetzal 3.8] UMem, the dynamic memory, either
/// compressed against the original story file or dumped; and
/// [quetzal 4.10] Stks, the stack and call chain. Anything else is
/// skipped, as [quetzal 8.9] says it should be.
/// </remarks>
public static class Quetzal
{
    private const uint Form = 0x464F524D;
    private const uint Ifzs = 0x49465A53;
    private const uint IFhd = 0x49466864;
    private const uint CMem = 0x434D656D;
    private const uint UMem = 0x554D656D;
    private const uint Stks = 0x53746B73;

    /// <summary>
    /// Writes a saved state as a Quetzal file.
    /// </summary>
    /// <param name="state">The state of play to save.</param>
    /// <param name="header">
    /// The story's header, for the release, serial, and checksum that
    /// [quetzal 5.3] identify it.
    /// </param>
    /// <param name="originalDynamicMemory">
    /// Dynamic memory as it was in the story file, which [quetzal 3.2]
    /// the compression works against.
    /// </param>
    /// <param name="output">Where the file goes.</param>
    public static void Write(SavedState state, StoryHeader header, ReadOnlySpan<byte> originalDynamicMemory, Stream output)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(output);

        var body = new MemoryStream();
        WriteId(body, Ifzs);

        // [quetzal 5.4] IFhd: release, serial, checksum, and a 3-byte
        // program counter. [quetzal 5.7] Thirteen bytes, so a pad byte
        // follows.
        var ifhd = new byte[13];
        BinaryPrimitives.WriteUInt16BigEndian(ifhd, header.Release);
        Encoding.ASCII.GetBytes(header.SerialCode.PadRight(6)[..6]).CopyTo(ifhd, 2);
        BinaryPrimitives.WriteUInt16BigEndian(ifhd.AsSpan(8), header.Checksum);
        ifhd[10] = (byte)(state.ProgramCounter >> 16);
        ifhd[11] = (byte)(state.ProgramCounter >> 8);
        ifhd[12] = (byte)state.ProgramCounter;
        WriteChunk(body, IFhd, ifhd);

        // [quetzal 3.7] CMem.
        WriteChunk(body, CMem, Compress(state.DynamicMemory, originalDynamicMemory));

        // [quetzal 4.10] Stks.
        WriteChunk(body, Stks, EncodeStacks(state, header.Version));

        // [quetzal 8.5] A single FORM chunk wraps it all.
        WriteId(output, Form);
        WriteLength(output, (uint)body.Length);
        body.Position = 0;
        body.CopyTo(output);
        output.Flush();
    }

    /// <summary>
    /// Reads a Quetzal file into a saved state.
    /// </summary>
    /// <param name="input">The file.</param>
    /// <param name="header">
    /// The story now playing, which [quetzal 5.3] the file must have
    /// been saved from.
    /// </param>
    /// <param name="originalDynamicMemory">
    /// Dynamic memory as it was in the story file, which [quetzal 3.2]
    /// decompression works against.
    /// </param>
    /// <exception cref="InvalidDataException">
    /// The file is not a Quetzal saved game, was saved from another
    /// story, or is missing or mangled in one of its chunks.
    /// </exception>
    public static SavedState Read(Stream input, StoryHeader header, ReadOnlySpan<byte> originalDynamicMemory)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(header);

        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        var file = buffer.ToArray();

        if (file.Length < 12 || ReadId(file, 0) != Form || ReadId(file, 8) != Ifzs)
        {
            throw new InvalidDataException("This is not a Quetzal saved game.");
        }

        var end = Math.Min(file.Length, 8 + (int)Math.Min(ReadLength(file, 4), int.MaxValue));
        int? programCounter = null;
        byte[]? memory = null;
        SavedState? stacks = null;

        // [quetzal 8.6] A concatenation of chunks after the sub-ID.
        // [quetzal 8.8] A second chunk of a kind expected once is
        // ignored, and [quetzal 8.9] an unknown kind is skipped.
        var at = 12;
        while (at + 8 <= end)
        {
            var id = ReadId(file, at);
            var length = ReadLength(file, at + 4);
            var start = at + 8;
            if (length > (uint)(end - start))
            {
                throw new InvalidDataException("A chunk of the saved game runs past the end of the file.");
            }

            var data = file.AsSpan(start, (int)length);

            switch (id)
            {
                case IFhd when programCounter is null:
                    programCounter = ReadHeaderChunk(data, header);
                    break;
                case CMem when memory is null:
                    memory = Decompress(data, originalDynamicMemory);
                    break;
                case UMem when memory is null:
                    // [quetzal 3.6] A dump must be exactly as long as
                    // dynamic memory.
                    if (data.Length != originalDynamicMemory.Length)
                    {
                        throw new InvalidDataException(
                            $"The saved game's memory dump is {data.Length} bytes, but dynamic memory is {originalDynamicMemory.Length}.");
                    }

                    memory = data.ToArray();
                    break;
                case Stks when stacks is null:
                    stacks = DecodeStacks(data, header.Version);
                    break;
                default:
                    break;
            }

            // [quetzal 8.4.1] An odd length is followed by a pad byte.
            at = start + (int)length + (int)(length & 1);
        }

        // [quetzal 7.18] IFhd, one of the memory chunks, and Stks are
        // required.
        if (programCounter is null || memory is null || stacks is null)
        {
            throw new InvalidDataException("The saved game is missing one of its required chunks.");
        }

        return stacks with { DynamicMemory = memory, ProgramCounter = programCounter.Value };
    }

    /// <summary>
    /// [quetzal 3.2] Compresses dynamic memory against the original:
    /// the two are exclusive-ored, and in the result a non-zero byte is
    /// itself while a zero byte is followed by a count, the pair meaning
    /// count+1 zeros.
    /// </summary>
    /// <remarks>
    /// [quetzal 3.4] A run of zeros at the end is left off, which is
    /// allowed and makes a barely-played game's save tiny.
    /// </remarks>
    public static byte[] Compress(ReadOnlySpan<byte> current, ReadOnlySpan<byte> original)
    {
        if (current.Length != original.Length)
        {
            throw new ArgumentException("The current and original dynamic memory must be the same length.", nameof(current));
        }

        var output = new List<byte>();
        var zeros = 0;

        for (var i = 0; i < current.Length; i++)
        {
            var b = (byte)(current[i] ^ original[i]);
            if (b == 0)
            {
                zeros++;
                continue;
            }

            FlushZeros();
            output.Add(b);
        }

        return output.ToArray();

        void FlushZeros()
        {
            while (zeros > 0)
            {
                var run = Math.Min(zeros, 256);
                output.Add(0);
                output.Add((byte)(run - 1));
                zeros -= run;
            }
        }
    }

    /// <summary>
    /// [quetzal 3.2] Decompresses a CMem chunk against the original
    /// dynamic memory.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// [quetzal 3.5] The data decodes to more than dynamic memory, or
    /// ends with a zero byte and no count.
    /// </exception>
    public static byte[] Decompress(ReadOnlySpan<byte> compressed, ReadOnlySpan<byte> original)
    {
        // [quetzal 3.4] Anything the data does not cover is a run of
        // zeros, so the original byte.
        var memory = original.ToArray();
        var at = 0;

        for (var i = 0; i < compressed.Length; i++)
        {
            if (compressed[i] != 0)
            {
                if (at >= memory.Length)
                {
                    throw new InvalidDataException("The saved game's memory is longer than dynamic memory.");
                }

                memory[at] ^= compressed[i];
                at++;
                continue;
            }

            if (i + 1 >= compressed.Length)
            {
                throw new InvalidDataException("The saved game's memory ends in an incomplete run.");
            }

            at += compressed[++i] + 1;
            if (at > memory.Length)
            {
                throw new InvalidDataException("The saved game's memory is longer than dynamic memory.");
            }
        }

        return memory;
    }

    private static int ReadHeaderChunk(ReadOnlySpan<byte> data, StoryHeader header)
    {
        if (data.Length < 13)
        {
            throw new InvalidDataException("The saved game's IFhd chunk is too short.");
        }

        // [quetzal 5.3] Release, serial, and checksum must all agree
        // with the story now playing.
        var release = BinaryPrimitives.ReadUInt16BigEndian(data);
        var serial = Encoding.ASCII.GetString(data.Slice(2, 6));
        var checksum = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(8));

        if (release != header.Release || serial != header.SerialCode || checksum != header.Checksum)
        {
            throw new InvalidDataException(
                $"The game was saved from release {release} serial {serial}, not from release {header.Release} serial {header.SerialCode}.");
        }

        return (data[10] << 16) | (data[11] << 8) | data[12];
    }

    // [quetzal 4.3] and [quetzal 4.11] Frames oldest first, with a dummy
    // frame first in every version but 6, since execution there starts
    // at an address rather than in a routine.
    private static byte[] EncodeStacks(SavedState state, ZMachineVersion version)
    {
        var output = new MemoryStream();
        var frames = state.Frames;

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var nextBase = i + 1 < frames.Count ? frames[i + 1].StackBase : state.Stack.Length;
            var words = nextBase - frame.StackBase;
            var dummy = i == 0 && version != ZMachineVersion.V6;

            if (dummy)
            {
                // [quetzal 4.11.1] All fields zero except the count.
                output.Write(new byte[6]);
            }
            else
            {
                output.WriteByte((byte)(frame.ReturnAddress >> 16));
                output.WriteByte((byte)(frame.ReturnAddress >> 8));
                output.WriteByte((byte)frame.ReturnAddress);

                // [quetzal 4.3.2] 000pvvvv: p for a discarded result, v
                // the number of locals.
                var discard = frame.StoreVariable is null;
                output.WriteByte((byte)((discard ? 0x10 : 0) | (frame.Locals.Length & 0x0F)));
                output.WriteByte(discard ? (byte)0 : frame.StoreVariable!.Value);

                // [quetzal 4.7] One bit per argument supplied.
                output.WriteByte((byte)((1 << Math.Min(frame.ArgumentCount, 7)) - 1));
            }

            WriteWord(output, (ushort)words);

            if (!dummy)
            {
                foreach (var local in frame.Locals)
                {
                    WriteWord(output, local);
                }
            }

            for (var w = 0; w < words; w++)
            {
                WriteWord(output, state.Stack[frame.StackBase + w]);
            }
        }

        return output.ToArray();
    }

    private static SavedState DecodeStacks(ReadOnlySpan<byte> data, ZMachineVersion version)
    {
        var frames = new List<SavedFrame>();
        var stack = new List<ushort>();
        var at = 0;

        while (at < data.Length)
        {
            if (at + 8 > data.Length)
            {
                throw new InvalidDataException("The saved game's stack chunk ends in the middle of a frame.");
            }

            var returnAddress = (data[at] << 16) | (data[at + 1] << 8) | data[at + 2];
            var flags = data[at + 3];
            var storeVariable = data[at + 4];
            var argumentBits = data[at + 5];
            int words = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at + 6));
            at += 8;

            var localCount = flags & 0x0F;
            var discard = (flags & 0x10) != 0;
            var dummy = frames.Count == 0 && version != ZMachineVersion.V6;

            if (at + (2 * (localCount + words)) > data.Length)
            {
                throw new InvalidDataException("The saved game's stack chunk ends in the middle of a frame.");
            }

            var locals = new ushort[localCount];
            for (var i = 0; i < localCount; i++)
            {
                locals[i] = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at));
                at += 2;
            }

            // [quetzal 4.7] Any bit pattern is accepted, counting the
            // bits set, though Frotz insists on a contiguous run.
            var argumentCount = 0;
            for (var bit = 0; bit < 7; bit++)
            {
                if ((argumentBits & (1 << bit)) != 0)
                {
                    argumentCount++;
                }
            }

            frames.Add(dummy
                ? new SavedFrame(0, null, [], 0, stack.Count)
                : new SavedFrame(returnAddress, discard ? null : storeVariable, locals, argumentCount, stack.Count));

            for (var w = 0; w < words; w++)
            {
                stack.Add(BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at)));
                at += 2;
            }
        }

        if (frames.Count == 0)
        {
            throw new InvalidDataException("The saved game has no stack frames at all.");
        }

        return new SavedState([], 0, frames, stack.ToArray());
    }

    private static void WriteChunk(Stream output, uint id, byte[] data)
    {
        WriteId(output, id);
        WriteLength(output, (uint)data.Length);
        output.Write(data);

        // [quetzal 8.4.1] A pad byte after an odd length, not counted in
        // the length.
        if ((data.Length & 1) != 0)
        {
            output.WriteByte(0);
        }
    }

    private static void WriteId(Stream output, uint id) => WriteLength(output, id);

    private static void WriteLength(Stream output, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        output.Write(bytes);
    }

    private static void WriteWord(Stream output, ushort value)
    {
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }

    private static uint ReadId(byte[] file, int at) => BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at));

    private static uint ReadLength(byte[] file, int at) => BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at));
}

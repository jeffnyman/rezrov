using System.Buffers.Binary;

namespace Rezrov.Glulx.Saves;

/// <summary>
/// Reads and writes Glulx saved games, which are Quetzal files with the
/// changes the Glulx specification makes for its larger machine.
/// </summary>
/// <remarks>
/// [glulx #saveformat] A saved game is an IFF FORM of type IFZS, as a
/// Z-machine one is, and everything in the Quetzal specification applies
/// except as follows. The IFhd chunk is the first 128 bytes of memory,
/// which are in ROM and so identify the game file. The memory chunk,
/// CMem or UMem, begins with a four-byte word giving the current size of
/// memory and covers RAMSTART to that size; the compressed form is
/// exclusive-ored against the game file, extended with zeroes above
/// EXTSTART. The Stks chunk is the whole stack, big-endian, with a call
/// stub pushed first. An MAll chunk describes the heap, and may be left
/// out or hold two zero words when the heap is not active. Anything
/// else is skipped, as [quetzal 8.9] says it should be.
/// </remarks>
public static class GlulxQuetzal
{
    /// <summary>
    /// [glulx #saveformat] How much of memory identifies the game.
    /// </summary>
    public const int HeaderLength = 128;

    private const uint Form = 0x464F524D;
    private const uint Ifzs = 0x49465A53;
    private const uint IFhd = 0x49466864;
    private const uint CMem = 0x434D656D;
    private const uint UMem = 0x554D656D;
    private const uint Stks = 0x53746B73;
    private const uint MAll = 0x4D416C6C;

    /// <summary>Writes a state as a saved game.</summary>
    /// <param name="state">The state of play to save.</param>
    /// <param name="memory">
    /// The game's memory, for the header that identifies it and for the
    /// initial RAM the compression works against.
    /// </param>
    /// <param name="output">Where the file goes.</param>
    public static void Write(GlulxSavedState state, GlulxMemory memory, Stream output)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(output);

        var body = new MemoryStream();
        WriteWord(body, Ifzs);

        // [glulx #saveformat] IFhd is the first 128 bytes of memory.
        WriteChunk(body, IFhd, memory.Slice(0, HeaderLength));

        // [glulx #saveformat] CMem: the size of memory, then RAM
        // compressed against the game file extended with zeroes.
        var compressed = new MemoryStream();
        WriteWord(compressed, state.MemorySize);
        compressed.Write(Compress(state.Ram, memory.InitialRam((uint)state.Ram.Length)));
        WriteChunk(body, CMem, compressed.ToArray());

        // [glulx #saveformat] The heap is not built, so it is never
        // active and its chunk is omitted, which is allowed.

        // [glulx #saveformat] Stks: the whole stack as it is.
        WriteChunk(body, Stks, state.Stack);

        // [quetzal 8.5] A single FORM chunk wraps it all.
        WriteWord(output, Form);
        WriteWord(output, (uint)body.Length);
        body.Position = 0;
        body.CopyTo(output);
        output.Flush();
    }

    /// <summary>Reads a saved game into a state.</summary>
    /// <param name="input">The file.</param>
    /// <param name="memory">
    /// The game now playing, which the file must have been saved from,
    /// and whose initial RAM the decompression works against.
    /// </param>
    /// <exception cref="InvalidDataException">
    /// The file is not a Quetzal saved game, was saved from another
    /// game, or is missing or mangled in one of its chunks.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The saved game has heap blocks, and the heap is not built.
    /// </exception>
    public static GlulxSavedState Read(Stream input, GlulxMemory memory)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(memory);

        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        var file = buffer.ToArray();

        if (file.Length < 12 || ReadWord(file, 0) != Form || ReadWord(file, 8) != Ifzs)
        {
            throw new InvalidDataException("This is not a Quetzal saved game.");
        }

        var end = Math.Min(file.Length, 8 + (int)Math.Min(ReadWord(file, 4), int.MaxValue));
        var identified = false;
        uint? memorySize = null;
        byte[]? ram = null;
        byte[]? stack = null;

        // [quetzal 8.6] A concatenation of chunks after the sub-ID.
        // [quetzal 8.8] A second chunk of a kind expected once is
        // ignored, and [quetzal 8.9] an unknown kind is skipped.
        var at = 12;
        while (at + 8 <= end)
        {
            var id = ReadWord(file, at);
            var length = ReadWord(file, at + 4);
            var start = at + 8;
            if (length > (uint)(end - start))
            {
                throw new InvalidDataException("A chunk of the saved game runs past the end of the file.");
            }

            var data = file.AsSpan(start, (int)length);

            switch (id)
            {
                case IFhd when !identified:
                    CheckHeaderChunk(data, memory);
                    identified = true;
                    break;
                case CMem when ram is null:
                    memorySize = ReadMemorySize(data, memory);
                    ram = Decompress(data[4..], memory.InitialRam(memorySize.Value - memory.RamStart));
                    break;
                case UMem when ram is null:
                    memorySize = ReadMemorySize(data, memory);

                    // [quetzal 3.6] A dump is exactly as long as memory.
                    if (data.Length - 4 != memorySize.Value - memory.RamStart)
                    {
                        throw new InvalidDataException(
                            $"The saved game's memory dump is {data.Length - 4} bytes, but its memory is {memorySize.Value - memory.RamStart} bytes above RAMSTART.");
                    }

                    ram = data[4..].ToArray();
                    break;
                case Stks when stack is null:
                    stack = ReadStackChunk(data);
                    break;
                case MAll:
                    CheckHeapChunk(data);
                    break;
                default:
                    break;
            }

            // [quetzal 8.4.1] An odd length is followed by a pad byte.
            at = start + (int)length + (int)(length & 1);
        }

        // [quetzal 7.18] IFhd, one of the memory chunks, and Stks are
        // required.
        if (!identified || ram is null || stack is null)
        {
            throw new InvalidDataException("The saved game is missing one of its required chunks.");
        }

        return new GlulxSavedState(memorySize!.Value, ram, stack);
    }

    /// <summary>
    /// [quetzal 3.2] Compresses RAM against its initial contents: the
    /// two are exclusive-ored, and in the result a non-zero byte is
    /// itself while a zero byte is followed by a count, the pair meaning
    /// count+1 zeros.
    /// </summary>
    /// <remarks>
    /// [quetzal 3.4] A run of zeros at the end is left off, so a game
    /// that has changed little saves small. [glulx #saveformat] The
    /// initial contents are the game file's, extended with zeroes, so
    /// <paramref name="initial"/> is as long as
    /// <paramref name="current"/> however memory has grown.
    /// </remarks>
    public static byte[] Compress(ReadOnlySpan<byte> current, ReadOnlySpan<byte> initial)
    {
        if (current.Length != initial.Length)
        {
            throw new ArgumentException("The current and initial RAM must be the same length.", nameof(current));
        }

        var output = new List<byte>();
        var zeros = 0;

        for (var i = 0; i < current.Length; i++)
        {
            var b = (byte)(current[i] ^ initial[i]);
            if (b == 0)
            {
                zeros++;
                continue;
            }

            while (zeros > 0)
            {
                var run = Math.Min(zeros, 256);
                output.Add(0);
                output.Add((byte)(run - 1));
                zeros -= run;
            }

            output.Add(b);
        }

        return output.ToArray();
    }

    /// <summary>
    /// [quetzal 3.2] Decompresses a CMem chunk's data against the
    /// initial RAM, whose length is the length of the result.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// [quetzal 3.5] The data decodes to more than there is RAM, or ends
    /// with a zero byte and no count.
    /// </exception>
    public static byte[] Decompress(ReadOnlySpan<byte> compressed, ReadOnlySpan<byte> initial)
    {
        // [quetzal 3.4] Anything the data does not cover is a run of
        // zeros, so the initial byte.
        var ram = initial.ToArray();
        var at = 0;

        for (var i = 0; i < compressed.Length; i++)
        {
            if (compressed[i] != 0)
            {
                if (at >= ram.Length)
                {
                    throw new InvalidDataException("The saved game's memory is longer than the game's RAM.");
                }

                ram[at] ^= compressed[i];
                at++;
                continue;
            }

            if (i + 1 >= compressed.Length)
            {
                throw new InvalidDataException("The saved game's memory ends in an incomplete run.");
            }

            at += compressed[++i] + 1;
            if (at > ram.Length)
            {
                throw new InvalidDataException("The saved game's memory is longer than the game's RAM.");
            }
        }

        return ram;
    }

    private static void CheckHeaderChunk(ReadOnlySpan<byte> data, GlulxMemory memory)
    {
        // [glulx #saveformat] The first 128 bytes of memory, which must
        // be the same bytes as the game now playing begins with.
        if (data.Length != HeaderLength || !data.SequenceEqual(memory.Slice(0, HeaderLength)))
        {
            throw new InvalidDataException("The game was saved from a different game file.");
        }
    }

    private static uint ReadMemorySize(ReadOnlySpan<byte> data, GlulxMemory memory)
    {
        if (data.Length < 4)
        {
            throw new InvalidDataException("The saved game's memory chunk has no size.");
        }

        // [glulx op:setmemsize] A size memory could have had: a multiple
        // of 256 and at least ENDMEM.
        var size = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (size % GlulxHeader.Alignment != 0 || size < memory.Header.EndMem)
        {
            throw new InvalidDataException($"The saved game's memory size {size:X8} is not one the game can have.");
        }

        return size;
    }

    private static byte[] ReadStackChunk(ReadOnlySpan<byte> data)
    {
        // [glulx #saveformat] Whole 32-bit values, and at least the stub
        // that says where to continue.
        if (data.Length % 4 != 0 || data.Length < GlulxSavedState.MinimumStackLength)
        {
            throw new InvalidDataException($"The saved game's stack of {data.Length} bytes is not a whole stack.");
        }

        return data.ToArray();
    }

    private static void CheckHeapChunk(ReadOnlySpan<byte> data)
    {
        // [glulx #saveformat] Two words, the heap start and the number
        // of blocks, then a pair per block. Without a heap there is
        // nothing to put the blocks in.
        if (data.Length == 0)
        {
            return;
        }

        if (data.Length < 8)
        {
            throw new InvalidDataException("The saved game's heap chunk is too short.");
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(data[4..]) != 0)
        {
            throw new NotSupportedException("The saved game has heap blocks, and the memory heap is not built yet.");
        }
    }

    private static void WriteChunk(Stream output, uint id, ReadOnlySpan<byte> data)
    {
        WriteWord(output, id);
        WriteWord(output, (uint)data.Length);
        output.Write(data);

        // [quetzal 8.4.1] A pad byte after an odd length, not counted in
        // the length.
        if ((data.Length & 1) != 0)
        {
            output.WriteByte(0);
        }
    }

    private static void WriteWord(Stream output, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        output.Write(bytes);
    }

    private static uint ReadWord(byte[] file, int at) => BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at));
}

using System.Buffers.Binary;

namespace Rezrov.Glulx;

/// <summary>
/// The Glulx machine's main memory: the game file laid out as the header
/// describes, with the extension above it that the file does not store.
/// </summary>
/// <remarks>
/// [glulx #the-machine] Main memory is a simple array of bytes numbered
/// from zero, big-endian for multibyte values, with no alignment
/// required. [glulx #the-memory-map] It is divided into ROM, which runs
/// from the header to RAMSTART and may never be written, RAM from there
/// to EXTSTART, which the game file stores, and the extension from
/// EXTSTART to ENDMEM, which starts as zeroes. Once execution begins the
/// last two are the same thing, so this type only distinguishes ROM from
/// the rest.
///
/// Addresses are unsigned 32-bit values throughout, as the machine
/// treats them, so callers never have to think about whether an address
/// above two gigabytes is negative. Every access is bounds checked, and
/// a read or write outside memory, or a write into ROM, stops the game
/// with a <see cref="GlulxException"/>.
/// </remarks>
public sealed class GlulxMemory
{
    // The game file as loaded, never changed. It is what memory is
    // restored from, and what the checksum is a checksum of.
    private readonly byte[] _file;

    // [glulx #opcodes_memory] The size of memory can change during play,
    // so the array is replaced rather than the field being final.
    private byte[] _bytes;

    /// <summary>
    /// Lays out memory for <paramref name="file"/>, the whole game file
    /// as loaded, keeping the array as the file's permanent image.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The header is not one this interpreter can run, as
    /// <see cref="GlulxHeader"/> explains.
    /// </exception>
    public GlulxMemory(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);

        Header = new GlulxHeader(file);
        _file = file;

        // [glulx #the-memory-map] The file holds memory up to EXTSTART;
        // the interpreter allocates up to ENDMEM and zeroes the rest.
        _bytes = new byte[Header.EndMem];
        file.AsSpan(0, (int)Header.ExtStart).CopyTo(_bytes);
    }

    /// <summary>The header the layout came from.</summary>
    public GlulxHeader Header { get; }

    /// <summary>
    /// A count that changes with every write, so that something built
    /// from the contents of memory, such as a cached string decoding
    /// table, can tell whether memory has changed under it.
    /// </summary>
    public uint Version { get; private set; }

    /// <summary>
    /// The current size of memory in bytes: [glulx #the-memory-map]
    /// ENDMEM to begin with, [glulx op:setmemsize] and whatever the game
    /// has set it to since.
    /// </summary>
    public uint Length => (uint)_bytes.Length;

    /// <summary>
    /// [glulx #the-memory-map] RAMSTART, the first address that may be
    /// written.
    /// </summary>
    public uint RamStart => Header.RamStart;

    public byte ReadByte(uint address)
    {
        CheckRead(address, 1);
        return _bytes[address];
    }

    /// <summary>
    /// Reads a 16-bit value, most significant byte first.
    /// </summary>
    public ushort ReadShort(uint address)
    {
        CheckRead(address, 2);
        return BinaryPrimitives.ReadUInt16BigEndian(_bytes.AsSpan((int)address, 2));
    }

    /// <summary>
    /// Reads a 32-bit value, most significant byte first.
    /// </summary>
    public uint ReadWord(uint address)
    {
        CheckRead(address, 4);
        return BinaryPrimitives.ReadUInt32BigEndian(_bytes.AsSpan((int)address, 4));
    }

    public void WriteByte(uint address, byte value)
    {
        CheckWrite(address, 1);
        _bytes[address] = value;
        Version++;
    }

    public void WriteShort(uint address, ushort value)
    {
        CheckWrite(address, 2);
        BinaryPrimitives.WriteUInt16BigEndian(_bytes.AsSpan((int)address, 2), value);
        Version++;
    }

    public void WriteWord(uint address, uint value)
    {
        CheckWrite(address, 4);
        BinaryPrimitives.WriteUInt32BigEndian(_bytes.AsSpan((int)address, 4), value);
        Version++;
    }

    /// <summary>
    /// A read-only view of <paramref name="length"/> bytes from
    /// <paramref name="address"/>, for callers that walk a table.
    /// </summary>
    public ReadOnlySpan<byte> Slice(uint address, uint length)
    {
        CheckRead(address, length);
        return _bytes.AsSpan((int)address, (int)length);
    }

    /// <summary>
    /// Writes <paramref name="length"/> zero bytes at
    /// <paramref name="address"/>.
    /// </summary>
    /// <remarks>
    /// [glulx op:mzero] A length of zero does nothing, and the operands
    /// are unsigned, so a negative length is a very large one and fails
    /// the bounds check as such.
    /// </remarks>
    public void Zero(uint address, uint length)
    {
        if (length == 0)
        {
            return;
        }

        CheckWrite(address, length);
        _bytes.AsSpan((int)address, (int)length).Clear();
        Version++;
    }

    /// <summary>
    /// Copies <paramref name="length"/> bytes from
    /// <paramref name="source"/> to <paramref name="destination"/>,
    /// safely when the two overlap.
    /// </summary>
    /// <remarks>
    /// [glulx op:mcopy] The specification spells out the overlap rule as
    /// copying upward when the destination is below the source and
    /// downward otherwise, which is what a memory move does.
    /// </remarks>
    public void Copy(uint source, uint destination, uint length)
    {
        if (length == 0)
        {
            return;
        }

        CheckRead(source, length);
        CheckWrite(destination, length);
        _bytes.AsSpan((int)source, (int)length).CopyTo(_bytes.AsSpan((int)destination, (int)length));
        Version++;
    }

    /// <summary>
    /// Changes the size of memory, keeping what fits and zeroing what is
    /// new.
    /// </summary>
    /// <remarks>
    /// [glulx op:setmemsize] The new size must be a multiple of 256 and
    /// at least ENDMEM, though it need not be larger than the current
    /// size: memory may grow and shrink over time. New space is zeroes
    /// and the contents of removed space are lost.
    /// </remarks>
    /// <exception cref="GlulxException">
    /// The size is not a multiple of 256 or is below ENDMEM.
    /// </exception>
    public void Resize(uint size)
    {
        if (size % GlulxHeader.Alignment != 0)
        {
            throw new GlulxException($"A memory size of {size:X8} is not a multiple of {GlulxHeader.Alignment:X}.");
        }

        if (size < Header.EndMem)
        {
            throw new GlulxException($"A memory size of {size:X8} is below ENDMEM at {Header.EndMem:X8}.");
        }

        if (size == Length)
        {
            return;
        }

        var resized = new byte[size];
        _bytes.AsSpan(0, (int)Math.Min(size, Length)).CopyTo(resized);
        _bytes = resized;
        Version++;
    }

    /// <summary>
    /// Puts memory back as it was when the file was loaded: RAM from the
    /// file again, the extension all zeroes, and the size ENDMEM.
    /// </summary>
    /// <remarks>
    /// [glulx op:restart] A restart restores the initial state of memory
    /// from the game file, and this is the memory half of that. The
    /// header is in ROM and cannot have changed, so ROM is left alone.
    /// [glulx op:setmemsize] The size is part of the state and is reset
    /// with the contents.
    /// </remarks>
    public void Reset()
    {
        if (Length != Header.EndMem)
        {
            _bytes = new byte[Header.EndMem];
        }

        // ROM is copied along with RAM: it cannot have changed, but a
        // fresh array after a resize needs it too.
        var extStart = (int)Header.ExtStart;
        _file.AsSpan(0, extStart).CopyTo(_bytes);
        _bytes.AsSpan(extStart).Clear();
        Version++;
    }

    /// <summary>
    /// RAM as it was in the game file: <paramref name="length"/> bytes
    /// from RAMSTART, zeroes above the file's end.
    /// </summary>
    /// <remarks>
    /// [glulx #saveformat] A saved game's compressed memory is taken
    /// against the game file extended with as many zeroes as necessary,
    /// which is what this gives for any length memory may have reached.
    /// </remarks>
    public byte[] InitialRam(uint length)
    {
        var initial = new byte[length];
        var fromFile = Math.Min(length, (uint)_file.Length - RamStart);
        _file.AsSpan((int)RamStart, (int)fromFile).CopyTo(initial);
        return initial;
    }

    /// <summary>
    /// Puts RAM back as a saved game or an undo state had it: memory at
    /// <paramref name="size"/> with <paramref name="ram"/> from RAMSTART
    /// up.
    /// </summary>
    /// <remarks>
    /// [glulx #saveformat] During a restore the size of memory is
    /// changed to the saved one, and RAM runs from RAMSTART to that
    /// size, so the two must agree.
    /// </remarks>
    /// <exception cref="GlulxException">
    /// The size is not one memory can have, or the RAM does not fill it.
    /// </exception>
    public void Restore(uint size, ReadOnlySpan<byte> ram)
    {
        if (size < RamStart || (uint)ram.Length != size - RamStart)
        {
            throw new GlulxException($"RAM of {ram.Length} bytes does not fill memory of {size:X8} bytes above RAMSTART.");
        }

        Resize(size);
        ram.CopyTo(_bytes.AsSpan((int)RamStart));
        Version++;
    }

    /// <summary>
    /// [glulx #the-header] The checksum of the game file as loaded. Play
    /// changes RAM, but the checksum is of the initial contents, so it
    /// is computed from the file and not from live memory.
    /// </summary>
    public uint ComputeChecksum() => Header.ComputeChecksum(_file);

    /// <summary>
    /// [glulx #the-header] Whether the header's checksum is the checksum
    /// of the file as loaded.
    /// </summary>
    public bool VerifyChecksum() => Header.VerifyChecksum(_file);

    private void CheckRead(uint address, uint length)
    {
        // Both comparisons are in unsigned arithmetic, so an address near
        // the top of the 32-bit range cannot wrap into looking valid.
        if (length > Length || address > Length - length)
        {
            throw new GlulxException(
                $"Memory access out of range: {length} bytes at {address:X8}, in memory of {Length:X8} bytes.");
        }
    }

    private void CheckWrite(uint address, uint length)
    {
        CheckRead(address, length);

        // [glulx #the-memory-map] It is illegal to write to ROM. The
        // check is on the first byte, and a value that would straddle
        // RAMSTART begins in ROM, so it is caught too.
        if (address < RamStart)
        {
            throw new GlulxException(
                $"Write to ROM: {length} bytes at {address:X8}, below RAMSTART at {RamStart:X8}.");
        }
    }
}

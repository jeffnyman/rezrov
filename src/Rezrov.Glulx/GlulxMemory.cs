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

    private readonly byte[] _bytes;

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
    /// [glulx #the-memory-map] ENDMEM, the size of memory in bytes.
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
    }

    public void WriteShort(uint address, ushort value)
    {
        CheckWrite(address, 2);
        BinaryPrimitives.WriteUInt16BigEndian(_bytes.AsSpan((int)address, 2), value);
    }

    public void WriteWord(uint address, uint value)
    {
        CheckWrite(address, 4);
        BinaryPrimitives.WriteUInt32BigEndian(_bytes.AsSpan((int)address, 4), value);
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
    /// Puts memory back as it was when the file was loaded: RAM from the
    /// file again, and the extension all zeroes.
    /// </summary>
    /// <remarks>
    /// [glulx op:restart] A restart restores the initial state of memory
    /// from the game file, and this is the memory half of that. The
    /// header is in ROM and cannot have changed, so ROM is left alone.
    /// </remarks>
    public void Reset()
    {
        var ramStart = (int)Header.RamStart;
        var extStart = (int)Header.ExtStart;
        _file.AsSpan(ramStart, extStart - ramStart).CopyTo(_bytes.AsSpan(ramStart));
        _bytes.AsSpan(extStart).Clear();
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

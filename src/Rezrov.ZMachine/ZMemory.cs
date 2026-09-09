using System.Buffers.Binary;

namespace Rezrov.ZMachine;

/// <summary>
/// The Z-Machine's memory: the story file loaded as one array of bytes,
/// addressed from zero.
/// </summary>
/// <remarks>
/// [zm 1.1] Memory is a single array of bytes with byte addresses running
/// from 0 upward, divided into dynamic, static, and high regions. The
/// boundaries between those regions come from the header, and the rules
/// about which region a game may write to belong to the opcodes that do
/// the writing, so they are not enforced here. This type only knows how
/// to read and write a byte or a word at an address.
///
/// [zm 2.1] Numbers are stored in two bytes, most significant byte first,
/// so every word access here is big-endian.
/// </remarks>
public sealed class ZMemory
{
    /// <summary>
    /// The size of the header, which is also the smallest possible story
    /// file.
    /// </summary>
    /// <remarks>
    /// [zm 1.1] Dynamic memory must contain at least 64 bytes, and
    /// [zm 1.1.1.1] those first 64 bytes are the header by tradition.
    /// </remarks>
    public const int HeaderLength = 64;

    private readonly byte[] _bytes;

    /// <summary>
    /// Wraps <paramref name="bytes"/> as the machine's memory. The array is
    /// used in place rather than copied, so it becomes the live memory of
    /// the machine and should not be touched by the caller afterward.
    /// </summary>
    public ZMemory(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length < HeaderLength)
        {
            throw new InvalidDataException(
                $"A story file must be at least {HeaderLength} bytes long, but this one is {bytes.Length}.");
        }

        _bytes = bytes;
    }

    public int Length => _bytes.Length;

    public byte ReadByte(int address) => _bytes[address];

    public void WriteByte(int address, byte value) => _bytes[address] = value;

    // [zm 2.1] Most significant byte first.
    public ushort ReadWord(int address) =>
        BinaryPrimitives.ReadUInt16BigEndian(_bytes.AsSpan(address, 2));

    public void WriteWord(int address, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(_bytes.AsSpan(address, 2), value);

    public ReadOnlySpan<byte> Slice(int address, int length) => _bytes.AsSpan(address, length);
}

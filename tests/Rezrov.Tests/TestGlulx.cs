using System.Buffers.Binary;

namespace Rezrov.Tests;

/// <summary>
/// Builds small Glulx game files for the header and memory tests: a
/// valid header over a memory map of a few hundred bytes, with the
/// checksum filled in, and any field changed on request.
/// </summary>
internal static class TestGlulx
{
    /// <summary>
    /// A game file whose header says what the arguments say. The file is
    /// EXTSTART bytes long unless <paramref name="length"/> says
    /// otherwise, and its checksum is correct for its contents.
    /// </summary>
    public static byte[] File(
        uint ramStart = 0x100,
        uint extStart = 0x200,
        uint endMem = 0x300,
        uint stackSize = 0x100,
        uint startFunction = 0x3C,
        uint decodingTable = 0,
        uint version = 0x00030103,
        int? length = null)
    {
        var bytes = new byte[length ?? (int)extStart];
        "Glul"u8.CopyTo(bytes);
        PutWord(bytes, 0x04, version);
        PutWord(bytes, 0x08, ramStart);
        PutWord(bytes, 0x0C, extStart);
        PutWord(bytes, 0x10, endMem);
        PutWord(bytes, 0x14, stackSize);
        PutWord(bytes, 0x18, startFunction);
        PutWord(bytes, 0x1C, decodingTable);
        PutWord(bytes, 0x20, SumOfWords(bytes, (int)Math.Min(extStart, (uint)bytes.Length)));
        return bytes;
    }

    public static void PutWord(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);

    /// <summary>
    /// The sum of the big-endian words in the first
    /// <paramref name="length"/> bytes, which is the checksum when the
    /// checksum field is zero.
    /// </summary>
    public static uint SumOfWords(byte[] bytes, int length)
    {
        uint sum = 0;
        for (var offset = 0; offset + 4 <= length; offset += 4)
        {
            sum += BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        }

        return sum;
    }
}

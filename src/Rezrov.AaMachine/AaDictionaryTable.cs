using System.Buffers.Binary;

namespace Rezrov.AaMachine;

/// <summary>
/// [aam story] The DICT chunk: every word the game knows, in the
/// game's own character set and always in lowercase.
/// </summary>
/// <remarks>
/// A word is a length and an offset, and the characters those offsets
/// point at may overlap, so "lantern" and "lamp" and "amp" can share
/// letters. Nothing terminates a word but its length.
///
/// The dictionary is not only for the parser. Compressed text reaches
/// for a whole word out of here whenever spelling it out a character at
/// a time would cost more bits.
/// </remarks>
public sealed class AaDictionaryTable
{
    private const int EntrySize = 3;

    private readonly byte[] _chunk;
    private readonly AaCharacterSet _characters;

    private AaDictionaryTable(byte[] chunk, AaCharacterSet characters, int count)
    {
        _chunk = chunk;
        _characters = characters;

        Count = count;
    }

    /// <summary>How many words the game knows.</summary>
    public int Count { get; }

    /// <summary>
    /// The characters of a word, in the game's own character set.
    /// </summary>
    public ReadOnlySpan<byte> Characters(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

        var entry = 2 + (index * EntrySize);
        var length = _chunk[entry];
        var offset = BinaryPrimitives.ReadUInt16BigEndian(_chunk.AsSpan(entry + 1));

        return _chunk.AsSpan(offset, length);
    }

    /// <summary>A word as text.</summary>
    public string Text(int index) => _characters.Text(Characters(index));

    internal static AaDictionaryTable Read(ReadOnlySpan<byte> chunk, AaCharacterSet characters)
    {
        if (chunk.Length < 2)
        {
            throw new InvalidDataException("The DICT chunk is too short to say how many words it holds.");
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(chunk);

        if (2 + (count * EntrySize) > chunk.Length)
        {
            throw new InvalidDataException(
                $"The dictionary says it holds {count} words but has room for fewer.");
        }

        var bytes = chunk.ToArray();

        // Every word has to lie inside the chunk, because a bad offset
        // here would otherwise surface much later as a strange word in
        // the middle of a sentence.
        for (var i = 0; i < count; i++)
        {
            var entry = 2 + (i * EntrySize);
            var length = bytes[entry];
            var offset = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(entry + 1));

            if (offset + length > bytes.Length)
            {
                throw new InvalidDataException($"Dictionary word {i} runs past the end of the chunk.");
            }
        }

        return new AaDictionaryTable(bytes, characters, count);
    }
}

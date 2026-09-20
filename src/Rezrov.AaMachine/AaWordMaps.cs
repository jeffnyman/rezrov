using System.Buffers.Binary;

namespace Rezrov.AaMachine;

/// <summary>
/// [aam story] The MAPS chunk: which objects a word could be talking
/// about.
/// </summary>
/// <remarks>
/// When the player types a noun, the machine has to narrow the field
/// before it starts trying objects one at a time. A word map answers
/// that in one lookup: the word is not in the map at all and matches
/// nothing, or it is a wildcard such as "the" and matches anything, or
/// it names a handful of objects and only those need be tried.
///
/// The entries are sorted by the word they are under, so a lookup is a
/// binary search.
/// </remarks>
public sealed class AaWordMaps
{
    private readonly byte[] _chunk;
    private readonly int[] _maps;

    private AaWordMaps(byte[] chunk, int[] maps)
    {
        _chunk = chunk;
        _maps = maps;
    }

    /// <summary>A story with no word maps.</summary>
    public static AaWordMaps None { get; } = new AaWordMaps([], []);

    /// <summary>How many maps the story carries.</summary>
    public int Count => _maps.Length;

    /// <summary>
    /// The objects a word maps to: null when the word is not in the
    /// map, an empty list when it is a wildcard, and otherwise the
    /// objects themselves.
    /// </summary>
    public ushort[]? Objects(int map, ushort word)
    {
        if (map < 0 || map >= _maps.Length)
        {
            return null;
        }

        var at = _maps[map];
        var count = BinaryPrimitives.ReadUInt16BigEndian(_chunk.AsSpan(at));
        var found = Find(at + 2, count, word);

        if (found < 0)
        {
            return null;
        }

        var payload = BinaryPrimitives.ReadUInt16BigEndian(_chunk.AsSpan(at + 2 + (found * 4) + 2));

        // [aam story] Nothing at all means a wildcard: the word is in
        // the map but says nothing about which object is meant.
        if (payload == 0)
        {
            return [];
        }

        // [aam story] The high bits mark one object named outright
        // rather than an offset to a list of them.
        if ((payload & 0xe000) == 0xe000)
        {
            return [(ushort)(payload & 0x1fff)];
        }

        return ReadPayload(payload);
    }

    internal static AaWordMaps Read(ReadOnlySpan<byte> chunk)
    {
        if (chunk.IsEmpty)
        {
            return None;
        }

        if (chunk.Length < 2)
        {
            throw new InvalidDataException("The MAPS chunk is too short to say how many maps it holds.");
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(chunk);

        if (2 + (count * 2) > chunk.Length)
        {
            throw new InvalidDataException($"The MAPS chunk says it holds {count} maps but has room for fewer.");
        }

        var maps = new int[count];

        for (var i = 0; i < count; i++)
        {
            var offset = BinaryPrimitives.ReadUInt16BigEndian(chunk[(2 + (i * 2))..]);
            var entries = offset + 2 <= chunk.Length
                ? BinaryPrimitives.ReadUInt16BigEndian(chunk[offset..])
                : throw new InvalidDataException($"Word map {i} lies past the end of the MAPS chunk.");

            if (offset + 2 + (entries * 4) > chunk.Length)
            {
                throw new InvalidDataException($"Word map {i} says it holds {entries} entries but has room for fewer.");
            }

            maps[i] = offset;
        }

        return new AaWordMaps(chunk.ToArray(), maps);
    }

    private int Find(int entries, int count, ushort word)
    {
        var low = 0;
        var high = count - 1;

        while (low <= high)
        {
            var middle = (low + high) / 2;
            var key = BinaryPrimitives.ReadUInt16BigEndian(_chunk.AsSpan(entries + (middle * 4)));

            if (key == word)
            {
                return middle;
            }

            if (key < word)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }

    private ushort[] ReadPayload(int at)
    {
        var objects = new List<ushort>();

        while (at < _chunk.Length)
        {
            var first = _chunk[at++];

            // [aam story] Nothing ends the list, an object up to $df
            // is itself, and anything above that carries on into a
            // second byte.
            if (first == 0)
            {
                break;
            }

            objects.Add(first < 0xe0
                ? first
                : (ushort)(((first & 0x1f) << 8) | _chunk[at++]));
        }

        return [.. objects];
    }
}

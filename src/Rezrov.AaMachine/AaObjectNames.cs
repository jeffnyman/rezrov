using System.Buffers.Binary;

namespace Rezrov.AaMachine;

/// <summary>
/// [aam story] The TAGS chunk: what the author called each object.
/// </summary>
/// <remarks>
/// A story need not carry this, and nothing in the game depends on it.
/// It is there so that an object printed as a value, which happens
/// when a game is being debugged or has gone wrong, can be printed as
/// "#mailbox" rather than as a number.
/// </remarks>
public sealed class AaObjectNames
{
    private readonly byte[] _chunk;
    private readonly AaCharacterSet _characters;
    private readonly int _count;

    private AaObjectNames(byte[] chunk, AaCharacterSet? characters, int count)
    {
        _chunk = chunk;
        _characters = characters!;
        _count = count;
    }

    /// <summary>
    /// A story that does not say what its objects are called.
    /// </summary>
    public static AaObjectNames None { get; } = new AaObjectNames([], null, 0);

    /// <summary>How many objects are named.</summary>
    public int Count => _count;

    /// <summary>
    /// What an object is called, or an empty string where it has no
    /// name. Objects are numbered from one.
    /// </summary>
    public string Name(int number)
    {
        var index = number - 1;

        if (index < 0 || index >= _count)
        {
            return string.Empty;
        }

        var offset = BinaryPrimitives.ReadUInt16BigEndian(_chunk.AsSpan(2 + (index * 2)));
        var end = _chunk.AsSpan(offset).IndexOf((byte)0);

        return _characters.Text(_chunk.AsSpan(offset, end < 0 ? _chunk.Length - offset : end));
    }

    internal static AaObjectNames Read(ReadOnlySpan<byte> chunk, AaCharacterSet characters)
    {
        if (chunk.IsEmpty)
        {
            return None;
        }

        if (chunk.Length < 2)
        {
            throw new InvalidDataException("The TAGS chunk is too short to say how many names it holds.");
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(chunk);

        if (2 + (count * 2) > chunk.Length)
        {
            throw new InvalidDataException($"The TAGS chunk says it holds {count} names but has room for fewer.");
        }

        return new AaObjectNames(chunk.ToArray(), characters, count);
    }
}

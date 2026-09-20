using System.Buffers.Binary;

namespace Rezrov.AaMachine;

/// <summary>
/// [aam story] The LANG chunk: everything about a game's own language
/// that the machine has to know before it can read a word of its text.
/// </summary>
/// <remarks>
/// Four things live here. The character set, which says what the bytes
/// above ASCII mean. The decoding table, a binary tree that turns a
/// bitstream back into characters. The word endings decoder, which is
/// how the parser strips a suffix and tries the dictionary again. And
/// the special characters, the punctuation that counts as a word of its
/// own and the punctuation that does not want a space beside it.
/// </remarks>
public sealed class AaLanguage
{
    // [aam story] A tree of at most 128 nodes, two bytes to a node: the
    // byte to act on if the next bit is 0, then the one for a 1.
    private const int MaximumNodes = 128;

    private readonly byte[] _decoding;
    private readonly byte[] _endings;

    private AaLanguage(
        byte[] decoding,
        AaCharacterSet characters,
        byte[] endings,
        byte[] stop,
        byte[] noSpaceBefore,
        byte[] noSpaceAfter)
    {
        _decoding = decoding;
        _endings = endings;

        Characters = characters;
        StopCharacters = stop;
        NoSpaceBefore = noSpaceBefore;
        NoSpaceAfter = noSpaceAfter;
    }

    /// <summary>The characters the game is written in.</summary>
    public AaCharacterSet Characters { get; }

    /// <summary>
    /// [aam story] The bitstream decoding tree, two bytes to a node.
    /// </summary>
    public ReadOnlySpan<byte> DecodingTable => _decoding;

    /// <summary>
    /// [aam story] The word endings decoder, a little program the
    /// parser runs over a word it did not recognize: 00 fails, 01 looks
    /// the word up as it stands, and any other byte pair moves a final
    /// character into the ending and jumps.
    /// </summary>
    public ReadOnlySpan<byte> WordEndings => _endings;

    /// <summary>
    /// [aam story] Punctuation that is a word of its own in input.
    /// </summary>
    public IReadOnlyList<byte> StopCharacters { get; }

    /// <summary>
    /// [aam story] Stop characters that want no space before them, such
    /// as a full stop. Empty in a story older than version 0.4.
    /// </summary>
    public IReadOnlyList<byte> NoSpaceBefore { get; }

    /// <summary>
    /// [aam story] Stop characters that want no space after them, such
    /// as an opening bracket.
    /// </summary>
    public IReadOnlyList<byte> NoSpaceAfter { get; }

    /// <summary>
    /// Reads the chunk, which needs the file format version because the
    /// special characters gained two fields in version 0.4.
    /// </summary>
    internal static AaLanguage Read(ReadOnlySpan<byte> chunk, int major, int minor)
    {
        if (chunk.Length < 8)
        {
            throw new InvalidDataException($"The LANG chunk is {chunk.Length} bytes, too short to be read.");
        }

        var decodingAt = Offset(chunk, 0, chunk.Length, "decoding table");
        var charactersAt = Offset(chunk, 2, chunk.Length, "extended character table");
        var endingsAt = Offset(chunk, 4, chunk.Length, "word endings decoder");
        var specialAt = Offset(chunk, 6, chunk.Length, "special characters");

        // The four parts follow one another, so each one reaches as far
        // as the next begins.
        var decoding = chunk[decodingAt..Math.Max(decodingAt, charactersAt)].ToArray();

        if (decoding.Length % 2 != 0 || decoding.Length / 2 > MaximumNodes)
        {
            throw new InvalidDataException(
                $"The decoding table is {decoding.Length} bytes, which is not up to {MaximumNodes} pairs.");
        }

        var characters = ReadCharacters(chunk[charactersAt..]);
        var endings = chunk[endingsAt..Math.Max(endingsAt, specialAt)].ToArray();

        var special = chunk[specialAt..];
        var stop = NullTerminated(ref special);

        // [aam story] The two sets that follow arrived in version 0.4,
        // and an older story simply does not carry them.
        var newer = major > 0 || minor >= 4;
        var noSpaceBefore = newer ? NullTerminated(ref special) : [];
        var noSpaceAfter = newer ? NullTerminated(ref special) : [];

        return new AaLanguage(decoding, characters, endings, stop, noSpaceBefore, noSpaceAfter);
    }

    private static AaCharacterSet ReadCharacters(ReadOnlySpan<byte> at)
    {
        if (at.IsEmpty)
        {
            throw new InvalidDataException("The LANG chunk has no extended character table.");
        }

        var count = at[0];

        if (1 + (count * 5) > at.Length)
        {
            throw new InvalidDataException(
                $"The extended character table says it holds {count} characters but has room for fewer.");
        }

        var characters = new AaCharacter[count];

        for (var i = 0; i < count; i++)
        {
            // [aam story] Lowercase, uppercase, then the codepoint in
            // three bytes, which is one more than Unicode has ever
            // needed and two more than it usually does.
            var entry = at.Slice(1 + (i * 5), 5);
            var codepoint = (entry[2] << 16) | (entry[3] << 8) | entry[4];

            characters[i] = new AaCharacter(codepoint, entry[0], entry[1]);
        }

        return new AaCharacterSet(characters);
    }

    private static int Offset(ReadOnlySpan<byte> chunk, int at, int length, string what)
    {
        var offset = BinaryPrimitives.ReadUInt16BigEndian(chunk[at..]);

        return offset <= length
            ? offset
            : throw new InvalidDataException($"The LANG chunk puts its {what} past its own end.");
    }

    private static byte[] NullTerminated(ref ReadOnlySpan<byte> at)
    {
        var end = at.IndexOf((byte)0);

        if (end < 0)
        {
            throw new InvalidDataException("A set of special characters in the LANG chunk is not terminated.");
        }

        var set = at[..end].ToArray();
        at = at[(end + 1)..];

        return set;
    }
}

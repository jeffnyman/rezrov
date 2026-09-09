using System.Text;

namespace Rezrov.ZMachine.Text;

/// <summary>
/// Translates Z-characters 6 to 31 into ZSCII, for each of the three
/// alphabets.
/// </summary>
/// <remarks>
/// [zm 3.5] Z-characters 0 to 5 have fixed meanings that depend on the
/// version, and everything from 6 upward goes through a table like this
/// one. [zm 3.5.3] Versions 2 to 4 use a fixed table, [zm 3.5.4] Version
/// 1 differs from it only in the A2 row, and [zm 3.5.5] from Version 5 a
/// story file may supply its own table through the header.
///
/// Z-character 6 in A2 is never looked up here. [zm 3.4] It is the ZSCII
/// escape, and the decoder acts on it before consulting the table. Its
/// slot holds 0 so that a stray lookup produces a null rather than a
/// letter.
/// </remarks>
public sealed class AlphabetTable
{
    /// <summary>The lowest Z-character the table covers.</summary>
    public const int FirstZCharacter = 6;

    /// <summary>The highest Z-character the table covers.</summary>
    public const int LastZCharacter = 31;

    /// <summary>
    /// Entries per alphabet, which is Z-characters 6 to 31.
    /// </summary>
    public const int EntriesPerAlphabet = LastZCharacter - FirstZCharacter + 1;

    /// <summary>
    /// [zm 3.5.5.1] The size of a table in memory: three blocks of 26.
    /// </summary>
    public const int LengthInBytes = 3 * EntriesPerAlphabet;

    private readonly byte[] _zscii;

    private AlphabetTable(byte[] zscii)
    {
        _zscii = zscii;
    }

    /// <summary>
    /// [zm 3.5.3] The table for Versions 2 to 4, which also serves any
    /// later version that does not supply its own.
    /// </summary>
    /// <remarks>
    /// The A2 row begins with the escape slot, then a newline for
    /// Z-character 7, then the 24 punctuation characters and digits.
    /// </remarks>
    public static AlphabetTable Default { get; } = new(
    [
        .. Ascii("abcdefghijklmnopqrstuvwxyz"),
        .. Ascii("ABCDEFGHIJKLMNOPQRSTUVWXYZ"),
        0, (byte)Zscii.Newline, .. Ascii("0123456789.,!?_#'\"/\\-:()"),
    ]);

    /// <summary>
    /// [zm 3.5.4] The table for Version 1, whose A2 row has no newline
    /// (Z-character 1 is the newline in that version) and uses the room
    /// for a less-than sign instead.
    /// </summary>
    public static AlphabetTable Version1 { get; } = new(
    [
        .. Ascii("abcdefghijklmnopqrstuvwxyz"),
        .. Ascii("ABCDEFGHIJKLMNOPQRSTUVWXYZ"),
        0, .. Ascii("0123456789.,!?_#'\"/\\<-:()"),
    ]);

    /// <summary>
    /// Picks the table a story file uses.
    /// </summary>
    /// <remarks>
    /// [zm 3.5.5] From Version 5, a non-zero word at $34 in the header is
    /// the byte address of a table specific to the story file. Before
    /// Version 5 that word is not consulted, whatever it holds.
    /// </remarks>
    public static AlphabetTable ForStory(StoryHeader header, ZMemory memory)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(memory);

        if (header.Version == ZMachineVersion.V1)
        {
            return Version1;
        }

        if (header.Version < ZMachineVersion.V5 || header.AlphabetTableAddress == 0)
        {
            return Default;
        }

        return Read(memory, header.AlphabetTableAddress);
    }

    /// <summary>
    /// Reads a story file's own table from memory.
    /// </summary>
    /// <remarks>
    /// [zm 3.5.5.1] The table is 78 bytes, three blocks of 26 ZSCII values
    /// for Z-characters 6 to 31 of A0, A1, and A2 in that order. Whatever
    /// the block for A2 says about its first two entries, Z-character 6 is
    /// still the escape and Z-character 7 is still a newline, so those two
    /// are overwritten here rather than trusted.
    /// </remarks>
    public static AlphabetTable Read(ZMemory memory, int address)
    {
        ArgumentNullException.ThrowIfNull(memory);

        var zscii = memory.Slice(address, LengthInBytes).ToArray();
        zscii[Index(Alphabet.A2, 6)] = 0;
        zscii[Index(Alphabet.A2, 7)] = (byte)Zscii.Newline;

        return new AlphabetTable(zscii);
    }

    /// <summary>
    /// The ZSCII value for <paramref name="zCharacter"/> in
    /// <paramref name="alphabet"/>.
    /// </summary>
    public byte this[Alphabet alphabet, int zCharacter]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(zCharacter, FirstZCharacter);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(zCharacter, LastZCharacter);

            return _zscii[Index(alphabet, zCharacter)];
        }
    }

    private static int Index(Alphabet alphabet, int zCharacter) =>
        ((int)alphabet * EntriesPerAlphabet) + (zCharacter - FirstZCharacter);

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
}

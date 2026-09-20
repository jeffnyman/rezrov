using System.Text;

namespace Rezrov.AaMachine;

/// <summary>
/// [aam story] One of the characters a game adds to ASCII: the Unicode
/// codepoint it stands for, and what it becomes in either case.
/// </summary>
public readonly record struct AaCharacter(int Codepoint, byte Lower, byte Upper);

/// <summary>
/// [aam text] The single-byte character set a story is written in.
/// </summary>
/// <remarks>
/// Characters $20 to $7e are ASCII and mean what they say. Characters
/// $80 upwards are the game's own, and the story file carries a table
/// saying which Unicode codepoint each one stands for and what it
/// becomes in either case. That is how a game can be written in Swedish
/// or French without the machine knowing anything about either.
///
/// Characters below $20 are reserved. A few of them stand for keys
/// rather than for letters, which is a matter for input rather than for
/// this, and $10 is a line feed in metadata and nowhere else.
/// </remarks>
public sealed class AaCharacterSet
{
    /// <summary>
    /// [aam story] The line feed that metadata, alone, may contain.
    /// </summary>
    public const byte LineFeed = 0x10;

    // [aam text] Everything from here up is the game's own, and the
    // table gives them in order with no gaps.
    private const byte FirstExtended = 0x80;

    private readonly AaCharacter[] _extended;

    internal AaCharacterSet(AaCharacter[] extended) => _extended = extended;

    /// <summary>How many characters the game adds to ASCII.</summary>
    public int Count => _extended.Length;

    /// <summary>
    /// What the game says about one of its own characters.
    /// </summary>
    public AaCharacter this[int index] => _extended[index];

    /// <summary>
    /// [aam text] The Unicode codepoint a character stands for, or the
    /// replacement character if the game never said.
    /// </summary>
    public int Codepoint(byte character)
    {
        if (character is >= 0x20 and <= 0x7e)
        {
            return character;
        }

        var index = character - FirstExtended;

        return character >= FirstExtended && index < _extended.Length
            ? _extended[index].Codepoint
            : 0xfffd;
    }

    /// <summary>
    /// [aam text] The same character in lowercase, which is what input
    /// is converted to and what dictionary words are written in.
    /// </summary>
    public byte ToLower(byte character) =>
        character is >= (byte)'A' and <= (byte)'Z' ? (byte)(character + 32) : Cased(character, lower: true);

    /// <summary>The same character in uppercase.</summary>
    public byte ToUpper(byte character) =>
        character is >= (byte)'a' and <= (byte)'z' ? (byte)(character - 32) : Cased(character, lower: false);

    /// <summary>
    /// A run of characters as text, which is the point of all of this.
    /// </summary>
    public string Text(ReadOnlySpan<byte> characters)
    {
        var text = new StringBuilder(characters.Length);

        foreach (var character in characters)
        {
            Append(text, character);
        }

        return text.ToString();
    }

    /// <summary>Adds one character to text being built.</summary>
    public void Append(StringBuilder text, byte character)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (character == LineFeed)
        {
            text.Append('\n');
            return;
        }

        var codepoint = Codepoint(character);

        // A codepoint is three bytes in the file, so it can name
        // something outside the range .NET keeps in a single char, and
        // it can also be nonsense.
        if (codepoint is >= 0 and <= 0x10ffff && codepoint is < 0xd800 or > 0xdfff)
        {
            text.Append(char.ConvertFromUtf32(codepoint));
        }
        else
        {
            text.Append('\ufffd');
        }
    }

    private byte Cased(byte character, bool lower)
    {
        var index = character - FirstExtended;

        if (character < FirstExtended || index >= _extended.Length)
        {
            return character;
        }

        return lower ? _extended[index].Lower : _extended[index].Upper;
    }
}

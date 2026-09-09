namespace Rezrov.ZMachine.Text;

/// <summary>
/// The Z-machine's character set, and the rules for turning one of its
/// codes into something displayable.
/// </summary>
/// <remarks>
/// [zm 3.8] ZSCII codes are 10-bit values, 0 to 1023, though [zm 3.8.1]
/// everything from 256 up is undefined so it is an 8-bit code in
/// practice. Some codes exist only for input and some only for output,
/// and both sides are here: what a code prints as, for text decoding,
/// and which codes a keyboard may produce, for the read opcodes.
/// </remarks>
public static class Zscii
{
    /// <summary>
    /// [zm 3.8.2.1] Output only, and has no effect on any stream.
    /// </summary>
    public const ushort Null = 0;

    /// <summary>[zm 3.8.2.2] Input only.</summary>
    public const ushort Delete = 8;

    /// <summary>[zm 3.8.2.3] Output only, and only in Version 6.</summary>
    public const ushort Tab = 9;

    /// <summary>[zm 3.8.2.4] Output only, and only in Version 6.</summary>
    public const ushort SentenceSpace = 11;

    /// <summary>[zm 3.8.2.5] Input and output.</summary>
    public const ushort Newline = 13;

    /// <summary>[zm 3.8.2.6] Input only.</summary>
    public const ushort Escape = 27;

    /// <summary>
    /// [zm 3.8.3] The first of the codes that agree with ASCII.
    /// </summary>
    public const ushort Space = 32;

    /// <summary>
    /// [zm 3.8.3] The last of the codes that agree with ASCII.
    /// </summary>
    public const ushort Tilde = 126;

    /// <summary>
    /// [zm 3.8.4] Input only. Cursor down, left, and right follow.
    /// </summary>
    public const ushort CursorUp = 129;

    /// <summary>[zm 3.8.4] Input only.</summary>
    public const ushort CursorDown = 130;

    /// <summary>[zm 3.8.4] Input only.</summary>
    public const ushort CursorLeft = 131;

    /// <summary>[zm 3.8.4] Input only.</summary>
    public const ushort CursorRight = 132;

    /// <summary>[zm 3.8.4] Input only. F2 to F12 follow in order.</summary>
    public const ushort F1 = 133;

    /// <summary>[zm 3.8.4] Input only.</summary>
    public const ushort F12 = 144;

    /// <summary>
    /// [zm 3.8.4] Input only. Keypad 1 to 9 follow in order.
    /// </summary>
    public const ushort Keypad0 = 145;

    /// <summary>[zm 3.8.4] Input only.</summary>
    public const ushort Keypad9 = 154;

    /// <summary>[zm 3.8.6] Input only, and only in Version 6.</summary>
    public const ushort MenuClick = 252;

    /// <summary>[zm 3.8.6] Input only, and only in Version 6.</summary>
    public const ushort DoubleClick = 253;

    /// <summary>[zm 3.8.6] Input only.</summary>
    public const ushort SingleClick = 254;

    /// <summary>
    /// [zm 10.5.2.1] Not a character at all: in a terminating characters
    /// table it means that any function key ends input.
    /// </summary>
    public const ushort AnyFunctionKey = 255;

    /// <summary>
    /// [zm 10.5.2.1] Whether a code is a function key, which the standard
    /// defines as 129 to 154 together with 252, 253, and 254. These are
    /// the only codes a terminating characters table may name.
    /// </summary>
    public static bool IsFunctionKey(int zscii) =>
        zscii is (>= CursorUp and <= Keypad9) or (>= MenuClick and <= SingleClick);

    /// <summary>
    /// [zm 3.8] Whether a code is defined for input: delete, newline,
    /// escape, the ASCII range, the function keys, the extra characters,
    /// and the clicks.
    /// </summary>
    public static bool IsDefinedForInput(int zscii) =>
        zscii is Delete or Newline or Escape
            or (>= Space and <= Tilde)
            or (>= UnicodeTranslationTable.FirstZscii and <= UnicodeTranslationTable.LastZscii)
            || IsFunctionKey(zscii);

    /// <summary>
    /// [zm 10.7.2] Whether a code is defined for both input and output,
    /// which is what the read opcode may store in its text buffer: the
    /// ASCII range and the extra characters.
    /// </summary>
    public static bool IsDefinedForInputAndOutput(int zscii) =>
        zscii is (>= Space and <= Tilde)
            or (>= UnicodeTranslationTable.FirstZscii and <= UnicodeTranslationTable.LastZscii);

    /// <summary>
    /// Converts a typed character into the ZSCII code a keyboard would
    /// produce, or null if no input code stands for it.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="ToUnicode"/> for the input side. Either
    /// line ending becomes [zm 3.8.2.5] newline, since the read opcode's
    /// text says the interpreter must return 13 whichever key the machine
    /// has. Backspace and the ASCII delete both become [zm 3.8.2.2]
    /// delete. Extra characters come back through the story's translation
    /// table, so a French game gets its accents and a game without a table
    /// entry for a character does not see it at all.
    /// </remarks>
    public static ushort? FromUnicode(char unicode, UnicodeTranslationTable extraCharacters)
    {
        ArgumentNullException.ThrowIfNull(extraCharacters);

        switch (unicode)
        {
            case (char)0x0A:
            case (char)0x0D:
                return Newline;

            case (char)0x08:
            case (char)0x7F:
                return Delete;

            case (char)0x1B:
                return Escape;

            case >= (char)Space and <= (char)Tilde:
                return unicode;

            default:
                return extraCharacters.TryGetZscii(unicode, out var zscii) ? zscii : null;
        }
    }

    /// <summary>
    /// [zm op:read] Reduces a typed character to lower case, which is how
    /// the read opcode stores text so that the game can print it back
    /// tidily.
    /// </summary>
    /// <remarks>
    /// ASCII letters are simple. An extra character is lowered through the
    /// translation table, to Unicode and back, and stays as it was when
    /// the table has no code for its lower case form, since there is then
    /// nothing the game could have matched it against anyway.
    /// </remarks>
    public static ushort ToLower(ushort zscii, UnicodeTranslationTable extraCharacters)
    {
        ArgumentNullException.ThrowIfNull(extraCharacters);

        if (zscii is >= 'A' and <= 'Z')
        {
            return (ushort)(zscii + ('a' - 'A'));
        }

        if (extraCharacters.TryGetUnicode(zscii, out var unicode))
        {
            var lowered = char.ToLowerInvariant(unicode);
            if (lowered != unicode && extraCharacters.TryGetZscii(lowered, out var lowerZscii))
            {
                return lowerZscii;
            }
        }

        return zscii;
    }

    /// <summary>
    /// Converts a ZSCII code into the character it prints as, or null if
    /// the code is not defined for output.
    /// </summary>
    /// <remarks>
    /// This is a plain text rendering, enough for tests, tools, and
    /// transcripts. The Version 6 tab and sentence space become an
    /// ordinary tab and space, which loses the typographic intent but is
    /// the least wrong thing a string can hold. A real screen model will
    /// want the ZSCII codes themselves rather than this.
    ///
    /// Undefined codes come back as null rather than as some substitute,
    /// because the standard's remarks warn that a corrupt string printed
    /// as text can emit control codes, and dropping them is the safe
    /// default.
    /// </remarks>
    public static char? ToUnicode(int zscii, ZMachineVersion version, UnicodeTranslationTable extraCharacters)
    {
        ArgumentNullException.ThrowIfNull(extraCharacters);

        switch (zscii)
        {
            case Null:
                // [zm 3.8.2.1] Defined for output, but prints nothing.
                return null;

            case Tab:
                // [zm 3.8.2.3] A paragraph indent at the start of a line,
                // a space elsewhere, and only in Version 6.
                return version == ZMachineVersion.V6 ? '\t' : null;

            case SentenceSpace:
                // [zm 3.8.2.4] A wider gap between sentences, Version 6.
                return version == ZMachineVersion.V6 ? ' ' : null;

            case Newline:
                // [zm 3.8.2.5]
                return '\n';

            case >= Space and <= Tilde:
                // [zm 3.8.3] These agree with ASCII, and so with Unicode.
                return (char)zscii;

            case >= UnicodeTranslationTable.FirstZscii and <= UnicodeTranslationTable.LastZscii:
                // [zm 3.8.5] Whatever the story's translation table says,
                // except that [zm 3.8.5.4.5] control codes must not be
                // used, so a table that names one is treated as if it had
                // left the entry undefined.
                if (!extraCharacters.TryGetUnicode(zscii, out var unicode))
                {
                    return null;
                }

                return IsControlCode(unicode) ? null : unicode;

            default:
                // [zm 3.8.2] Codes 1 to 31 other than those above,
                // [zm 3.8.3.1] 127 and 128, [zm 3.8.4] the input-only keys
                // from 129 to 154, [zm 3.8.6] the clicks at 252 to 254,
                // [zm 3.8.7] 255, and [zm 3.8.1] everything from 256 up.
                return null;
        }
    }

    // [zm 3.8.5.4.5] U+0000 to U+001F and U+007F to U+009F.
    private static bool IsControlCode(char c) => c < (char)0x20 || (c >= (char)0x7F && c <= (char)0x9F);
}

namespace Rezrov.ZMachine.Text;

/// <summary>
/// The Z-machine's character set, and the rules for turning one of its
/// codes into something displayable.
/// </summary>
/// <remarks>
/// [zm 3.8] ZSCII codes are 10-bit values, 0 to 1023, though [zm 3.8.1]
/// everything from 256 up is undefined so it is an 8-bit code in
/// practice. Some codes exist only for input and some only for output.
/// Only the output side is handled here, since this is what text decoding
/// needs; the input side arrives with the read opcodes.
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

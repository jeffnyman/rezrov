using System.Text;

namespace Rezrov.ZMachine.Text;

/// <summary>
/// Encodes a word the way the dictionary stores it, so that typed input
/// can be matched against dictionary entries.
/// </summary>
/// <remarks>
/// [zm 3.7] Encoding for the dictionary is not the reverse of printing.
/// Abbreviations are never used, the result is exactly 6 Z-characters in
/// Versions 1 to 3 and 9 from Version 4, anything longer is cut off even
/// in the middle of a multi-Z-character construction, and the padding
/// character must be 5. The editor's note on [zm 3.7.1] makes the stakes
/// clear: an encoder that is wrong here does not print anything odd, it
/// just never matches the words in the game's dictionary.
///
/// The encoder does not convert to lower case. [zm 3.7] says typed text
/// should be, and <see cref="Lexing.Lexer"/> does that before encoding.
/// Keeping it out of here means a dictionary entry can be decoded and
/// re-encoded to exactly its original bytes, which is how the encoder is
/// tested against real story files.
/// </remarks>
public sealed class ZTextEncoder
{
    private readonly ZMachineVersion _version;
    private readonly AlphabetTable _alphabets;

    public ZTextEncoder(ZMachineVersion version, AlphabetTable alphabets)
    {
        ArgumentNullException.ThrowIfNull(alphabets);

        _version = version;
        _alphabets = alphabets;
    }

    /// <summary>
    /// The encoder for a story file, using whatever alphabet table its
    /// text decoder uses.
    /// </summary>
    public static ZTextEncoder ForStory(StoryHeader header, ZMemory memory)
    {
        ArgumentNullException.ThrowIfNull(header);

        return new ZTextEncoder(header.Version, AlphabetTable.ForStory(header, memory));
    }

    /// <summary>
    /// [zm 3.7] The fixed number of Z-characters in an encoded word: 6 in
    /// Versions 1 to 3, 9 from Version 4.
    /// </summary>
    public int ZCharacterCount => _version <= ZMachineVersion.V3 ? 6 : 9;

    /// <summary>
    /// [zm 13.3] and [zm 13.4] The encoded word's size in bytes: 4 or 6.
    /// </summary>
    public int EncodedLength => ZCharacterCount / 3 * 2;

    /// <summary>
    /// Encodes ASCII text. Anything outside ASCII has no single ZSCII
    /// code without a translation table, so it is rejected here; pass
    /// ZSCII bytes to the other overload for that.
    /// </summary>
    public byte[] EncodeWord(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Any(c => c > 127))
        {
            throw new ArgumentException("Only ASCII can be encoded from a string; pass ZSCII bytes instead.", nameof(text));
        }

        return EncodeWord(Encoding.ASCII.GetBytes(text));
    }

    /// <summary>
    /// Encodes a word given as 8-bit ZSCII, which is what a text buffer
    /// holds.
    /// </summary>
    public byte[] EncodeWord(ReadOnlySpan<byte> zscii)
    {
        var wide = new ushort[zscii.Length];
        for (var i = 0; i < zscii.Length; i++)
        {
            wide[i] = zscii[i];
        }

        return EncodeWord((ReadOnlySpan<ushort>)wide);
    }

    /// <summary>
    /// Encodes a word given as ZSCII codes.
    /// </summary>
    /// <remarks>
    /// [zm 3.8] ZSCII codes are ten-bit values, and [zm 3.4] the escape
    /// carries all ten bits, even though [zm 3.8.1] nothing above 255 is
    /// defined. Inform has been known to put such codes in a dictionary
    /// anyway, so a decoded entry has to be re-encodable without loss,
    /// which is why this takes 16-bit values.
    /// </remarks>
    public byte[] EncodeWord(ReadOnlySpan<ushort> zscii)
    {
        var zCharacters = new List<int>(ZCharacterCount + 4);

        // [zm 3.2.2] In Versions 1 and 2 a shift lock changes which
        // alphabet is in force. From Version 3 nothing does, so this stays
        // A0 and every non-A0 character costs a shift.
        var locked = Alphabet.A0;

        for (var i = 0; i < zscii.Length && zCharacters.Count < ZCharacterCount; i++)
        {
            var (alphabet, zCharacter) = Classify(zscii[i]);

            if (alphabet != locked)
            {
                if (_version <= ZMachineVersion.V2)
                {
                    // [zm 3.7.1] Use a shift lock rather than a single
                    // shift when the next character comes from the same
                    // alphabet as this one. Both kinds are relative to the
                    // alphabet in force: forward one is 2 or 4, forward
                    // two is 3 or 5.
                    var forward = (((int)alphabet - (int)locked) + 3) % 3;
                    var nextIsSame = i + 1 < zscii.Length && Classify(zscii[i + 1]).Alphabet == alphabet;

                    if (nextIsSame)
                    {
                        zCharacters.Add(3 + forward);
                        locked = alphabet;
                    }
                    else
                    {
                        zCharacters.Add(1 + forward);
                    }
                }
                else
                {
                    // [zm 3.2.3] Absolute single shifts: 4 for A1 and 5
                    // for A2.
                    zCharacters.Add(alphabet == Alphabet.A1 ? 4 : 5);
                }
            }

            if (zCharacter < 0)
            {
                // [zm 3.4] Not in any alphabet, so the ZSCII escape: 6 in
                // A2, then the top five bits, then the bottom five.
                zCharacters.Add(6);
                zCharacters.Add(zscii[i] >> 5);
                zCharacters.Add(zscii[i] & 0x1F);
            }
            else
            {
                zCharacters.Add(zCharacter);
            }
        }

        // [zm 3.7] Cut to the fixed length, leaving any construction that
        // did not fit incomplete rather than omitting it, and pad with 5s.
        if (zCharacters.Count > ZCharacterCount)
        {
            zCharacters.RemoveRange(ZCharacterCount, zCharacters.Count - ZCharacterCount);
        }

        while (zCharacters.Count < ZCharacterCount)
        {
            zCharacters.Add(5);
        }

        return Pack(zCharacters);
    }

    // Finds which alphabet holds a ZSCII code, searching A0 first so that
    // a character present in more than one table gets the cheapest
    // encoding. Returns A2 with a Z-character of -1 for a code that is in
    // none of them and needs the escape.
    private (Alphabet Alphabet, int ZCharacter) Classify(ushort zscii)
    {
        foreach (var alphabet in new[] { Alphabet.A0, Alphabet.A1, Alphabet.A2 })
        {
            // [zm 3.4] Z-character 6 of A2 is the escape, not a character,
            // so its slot in the table is never a match.
            var first = alphabet == Alphabet.A2 ? 7 : AlphabetTable.FirstZCharacter;

            for (var z = first; z <= AlphabetTable.LastZCharacter; z++)
            {
                if (_alphabets[alphabet, z] == zscii)
                {
                    return (alphabet, z);
                }
            }
        }

        return (Alphabet.A2, -1);
    }

    // [zm 3.2] Three Z-characters to a word, most significant first, and
    // the top bit set on the last word.
    private byte[] Pack(List<int> zCharacters)
    {
        var bytes = new byte[EncodedLength];

        for (var i = 0; i < ZCharacterCount; i += 3)
        {
            var word = (zCharacters[i] << 10) | (zCharacters[i + 1] << 5) | zCharacters[i + 2];
            if (i + 3 == ZCharacterCount)
            {
                word |= 0x8000;
            }

            bytes[i / 3 * 2] = (byte)(word >> 8);
            bytes[(i / 3 * 2) + 1] = (byte)word;
        }

        return bytes;
    }
}

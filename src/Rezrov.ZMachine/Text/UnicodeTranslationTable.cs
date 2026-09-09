namespace Rezrov.ZMachine.Text;

/// <summary>
/// Says what the "extra characters", ZSCII 155 to 251, look like, by
/// giving each one a Unicode code point.
/// </summary>
/// <remarks>
/// [zm 3.8.5] Different story files want different things from this
/// range: accented Latin letters, other alphabets, symbols. [zm 3.8.5.1]
/// Each is named by a 16-bit Unicode code, and nothing outside the Basic
/// Multilingual Plane is reachable. [zm 3.8.5.2] Versions 1 to 4 always
/// use the default table; from Version 5 a story file may supply its own.
/// </remarks>
public sealed class UnicodeTranslationTable
{
    /// <summary>The first ZSCII code the table can define.</summary>
    public const int FirstZscii = 155;

    /// <summary>The last ZSCII code the table can define.</summary>
    public const int LastZscii = 251;

    /// <summary>
    /// [zm 3.8.5] The most entries a table can hold, since ZSCII 252 to
    /// 254 are input codes and 255 is reserved.
    /// </summary>
    public const int MaximumEntries = LastZscii - FirstZscii + 1;

    private readonly char[] _unicode;

    private UnicodeTranslationTable(char[] unicode)
    {
        _unicode = unicode;
    }

    /// <summary>
    /// [zm 3.8.5.3] The default table, which covers ZSCII 155 to 223 with
    /// the accented letters and punctuation of ISO 8859-1 plus the two
    /// oe ligatures.
    /// </summary>
    /// <remarks>
    /// These values were generated from the table in the standard rather
    /// than typed, so a typo here would have to be a typo there too.
    /// </remarks>
    public static UnicodeTranslationTable Default { get; } = new(
    [
        'ä', // 155 ä a-diaeresis
        'ö', // 156 ö o-diaeresis
        'ü', // 157 ü u-diaeresis
        'Ä', // 158 Ä A-diaeresis
        'Ö', // 159 Ö O-diaeresis
        'Ü', // 160 Ü U-diaeresis
        'ß', // 161 ß sz-ligature
        '»', // 162 » quotation marks, right
        '«', // 163 « quotation marks, left
        'ë', // 164 ë e-diaeresis
        'ï', // 165 ï i-diaeresis
        'ÿ', // 166 ÿ y-diaeresis
        'Ë', // 167 Ë E-diaeresis
        'Ï', // 168 Ï I-diaeresis
        'á', // 169 á a-acute
        'é', // 170 é e-acute
        'í', // 171 í i-acute
        'ó', // 172 ó o-acute
        'ú', // 173 ú u-acute
        'ý', // 174 ý y-acute
        'Á', // 175 Á A-acute
        'É', // 176 É E-acute
        'Í', // 177 Í I-acute
        'Ó', // 178 Ó O-acute
        'Ú', // 179 Ú U-acute
        'Ý', // 180 Ý Y-acute
        'à', // 181 à a-grave
        'è', // 182 è e-grave
        'ì', // 183 ì i-grave
        'ò', // 184 ò o-grave
        'ù', // 185 ù u-grave
        'À', // 186 À A-grave
        'È', // 187 È E-grave
        'Ì', // 188 Ì I-grave
        'Ò', // 189 Ò O-grave
        'Ù', // 190 Ù U-grave
        'â', // 191 â a-circumflex
        'ê', // 192 ê e-circumflex
        'î', // 193 î i-circumflex
        'ô', // 194 ô o-circumflex
        'û', // 195 û u-circumflex
        'Â', // 196 Â A-circumflex
        'Ê', // 197 Ê E-circumflex
        'Î', // 198 Î I-circumflex
        'Ô', // 199 Ô O-circumflex
        'Û', // 200 Û U-circumflex
        'å', // 201 å a-ring
        'Å', // 202 Å A-ring
        'ø', // 203 ø o-slash
        'Ø', // 204 Ø O-slash
        'ã', // 205 ã a-tilde
        'ñ', // 206 ñ n-tilde
        'õ', // 207 õ o-tilde
        'Ã', // 208 Ã A-tilde
        'Ñ', // 209 Ñ N-tilde
        'Õ', // 210 Õ O-tilde
        'æ', // 211 æ ae-ligature
        'Æ', // 212 Æ AE-ligature
        'ç', // 213 ç c-cedilla
        'Ç', // 214 Ç C-cedilla
        'þ', // 215 þ Icelandic thorn
        'ð', // 216 ð Icelandic eth
        'Þ', // 217 Þ Icelandic Thorn
        'Ð', // 218 Ð Icelandic Eth
        '£', // 219 £ pound symbol
        'œ', // 220 œ oe-ligature
        'Œ', // 221 Œ OE-ligature
        '¡', // 222 ¡ inverted exclamation mark
        '¿', // 223 ¿ inverted question mark
    ]);

    /// <summary>
    /// The number of ZSCII codes this table defines, starting at 155.
    /// </summary>
    public int Count => _unicode.Length;

    /// <summary>
    /// Picks the table a story file uses.
    /// </summary>
    /// <remarks>
    /// [zm 3.8.5.2] Under Versions 1 to 4 the default table is always used.
    /// From Version 5, if word 3 of the header extension table is present
    /// and non-zero, it is the byte address of the story's own table.
    /// </remarks>
    public static UnicodeTranslationTable ForStory(StoryHeader header, ZMemory memory)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(memory);

        if (header.Version < ZMachineVersion.V5)
        {
            return Default;
        }

        var address = header.UnicodeTranslationTableAddress;
        return address == 0 ? Default : Read(memory, address);
    }

    /// <summary>
    /// Reads a story file's own table from memory.
    /// </summary>
    /// <remarks>
    /// [zm 3.8.5.2.1] One byte giving a count N, then N words.
    /// [zm 3.8.5.2.2] That defines ZSCII 155 to 155+N-1, and N may be zero.
    /// [zm 3.8.5.2.3] Each word is the Unicode code for the next ZSCII
    /// value in turn.
    /// </remarks>
    /// <exception cref="InvalidDataException">
    /// The table claims more entries than the extra character range has
    /// room for.
    /// </exception>
    public static UnicodeTranslationTable Read(ZMemory memory, int address)
    {
        ArgumentNullException.ThrowIfNull(memory);

        int count = memory.ReadByte(address);
        if (count > MaximumEntries)
        {
            throw new InvalidDataException(
                $"A Unicode translation table can define at most {MaximumEntries} characters, but this one claims {count}.");
        }

        var unicode = new char[count];
        for (var i = 0; i < count; i++)
        {
            unicode[i] = (char)memory.ReadWord(address + 1 + (i * 2));
        }

        return new UnicodeTranslationTable(unicode);
    }

    /// <summary>
    /// Looks up the Unicode code for an extra character, returning false
    /// if the table does not define it.
    /// </summary>
    public bool TryGetUnicode(int zscii, out char unicode)
    {
        var index = zscii - FirstZscii;
        if (index < 0 || index >= _unicode.Length)
        {
            unicode = default;
            return false;
        }

        unicode = _unicode[index];
        return true;
    }

    /// <summary>
    /// Looks up the extra character that stands for a Unicode character,
    /// returning false if the table has none. The first entry wins if the
    /// table lists a character twice.
    /// </summary>
    /// <remarks>
    /// The table is defined in the ZSCII to Unicode direction, and the
    /// standard never asks for the reverse, but the keyboard needs it:
    /// [zm 10.7] only ZSCII characters can be read, so a typed accent has
    /// to be found in the table before the game can see it.
    /// </remarks>
    public bool TryGetZscii(char unicode, out ushort zscii)
    {
        var index = Array.IndexOf(_unicode, unicode);
        if (index < 0)
        {
            zscii = 0;
            return false;
        }

        zscii = (ushort)(FirstZscii + index);
        return true;
    }
}

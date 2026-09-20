using System.Runtime.InteropServices;

namespace Rezrov.AaMachine;

/// <summary>
/// [aam text] Turns the packed bitstreams in the WRIT chunk back into
/// characters.
/// </summary>
/// <remarks>
/// Text is packed with a Huffman-flavored code. The story file carries
/// a binary tree, and reading a string means walking that tree one bit
/// at a time: at every node the next bit says which of two bytes to
/// act on, and that byte is either a character, an instruction to
/// descend to another node, an escape, or the end of the string.
///
/// The escape is where a whole dictionary word can arrive at once.
/// Spelling "candlestick" out a letter at a time costs far more bits
/// than naming the dictionary entry, so common words are named, and the
/// machine puts a space in front of them.
/// </remarks>
public sealed class AaTextDecoder
{
    // [aam story] A byte in the tree reads as: a character, $20 higher
    // than the byte itself, for everything but these three cases.
    private const byte Escape = 0x5f;
    private const byte EndOfString = 0x80;
    private const byte FirstNode = 0x81;

    // A byte from $81 up names the node that many places past this.
    private const byte NodeBase = 0x80;

    // [aam story] The first 32 of a game's own characters are in the
    // tree like any other. Only the ones past those are escaped.
    private const int InlineExtended = 32;

    private readonly byte[] _writ;
    private readonly AaLanguage _language;
    private readonly AaDictionaryTable _dictionary;

    // Version 0.3 and older read a flat seven bits after an escape.
    private readonly bool _sevenBitEscape;

    // [aam story] Newer stories read exactly enough bits to name either
    // one of the escaped characters or one dictionary word, so the
    // width depends on how many of each the game has.
    private readonly int _escapedCharacters;
    private readonly int _escapeBits;

    internal AaTextDecoder(
        byte[] writ,
        AaLanguage language,
        AaDictionaryTable dictionary,
        int major,
        int minor)
    {
        _writ = writ;
        _language = language;
        _dictionary = dictionary;
        _sevenBitEscape = major == 0 && minor <= 3;

        _escapedCharacters = Math.Max(0, language.Characters.Count - InlineExtended);
        _escapeBits = BitsFor(_escapedCharacters + dictionary.Count);
    }

    /// <summary>
    /// The text of the string that begins at a byte address in the
    /// WRIT chunk.
    /// </summary>
    public string At(int address)
    {
        var characters = new List<byte>();

        Characters(address, characters);

        return _language.Characters.Text(CollectionsMarshal.AsSpan(characters));
    }

    /// <summary>
    /// The characters of that string, in the game's own character set.
    /// </summary>
    public void Characters(int address, List<byte> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        if (address < 0 || address >= _writ.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(address), $"There is no string at {address} in a chunk of {_writ.Length} bytes.");
        }

        var table = _language.DecodingTable;
        var reader = new BitReader(_writ, address);

        while (true)
        {
            var node = 0;
            byte action;

            // Walk down from the root until the tree says something
            // other than "keep going".
            while (true)
            {
                var at = (node * 2) + reader.Bit();

                if (at >= table.Length)
                {
                    throw new InvalidDataException($"The string at {address} left the decoding table.");
                }

                action = table[at];

                if (action < FirstNode)
                {
                    break;
                }

                node = action - NodeBase;
            }

            if (action == EndOfString)
            {
                return;
            }

            if (action != Escape)
            {
                into.Add((byte)(0x20 + action));
                continue;
            }

            Escaped(ref reader, into);
        }
    }

    private void Escaped(ref BitReader reader, List<byte> into)
    {
        if (_sevenBitEscape)
        {
            // [aam story] Seven bits naming a character directly, which
            // is all an early story could do.
            into.Add((byte)(0x80 + reader.Read(7)));
            return;
        }

        var value = reader.Read(_escapeBits);

        if (value < _escapedCharacters)
        {
            into.Add((byte)(0xa0 + value));
            return;
        }

        var word = value - _escapedCharacters;

        if (word >= _dictionary.Count)
        {
            throw new InvalidDataException($"A string named dictionary word {word}, which does not exist.");
        }

        // [aam story] A named word always brings its own space, which
        // is why the encoding can afford to name so many of them.
        into.Add((byte)' ');

        foreach (var character in _dictionary.Characters(word))
        {
            into.Add(character);
        }
    }

    // How many bits it takes to name one of that many things, which is
    // the base two logarithm rounded up.
    private static int BitsFor(int count)
    {
        var bits = 0;

        while (1 << bits < count)
        {
            bits++;
        }

        return bits;
    }

    /// <summary>
    /// [aam story] Bits out of a chunk, most significant first. A
    /// stream always starts on a byte boundary.
    /// </summary>
    private ref struct BitReader(byte[] bytes, int at)
    {
        private readonly byte[] _bytes = bytes;
        private int _at = at;
        private byte _mask = 0x80;

        public int Bit()
        {
            if (_at >= _bytes.Length)
            {
                throw new InvalidDataException("A string ran off the end of the WRIT chunk.");
            }

            var bit = (_bytes[_at] & _mask) != 0 ? 1 : 0;

            _mask >>= 1;

            if (_mask == 0)
            {
                _mask = 0x80;
                _at++;
            }

            return bit;
        }

        public int Read(int count)
        {
            var value = 0;

            for (var i = 0; i < count; i++)
            {
                value = (value << 1) | Bit();
            }

            return value;
        }
    }
}

using Rezrov.ZMachine.Text;

namespace Rezrov.ZMachine.Lexing;

/// <summary>
/// A dictionary table: the word separators, and the sorted list of
/// encoded words that typed input is matched against.
/// </summary>
/// <remarks>
/// [zm 13.1] The game's own dictionary is in static memory at the address
/// in header word $08, but [zm 13.6] the tokenise opcode may supply any
/// table in the same format, so this type reads one from any address.
///
/// The entries are read from memory on demand rather than copied, which
/// matters for the user dictionaries a game can build during play.
/// </remarks>
public sealed class DictionaryTable
{
    private readonly ZMemory _memory;
    private readonly ZTextDecoder _decoder;
    private readonly ZTextEncoder _encoder;

    /// <summary>
    /// Reads the dictionary at <paramref name="address"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The entry length is too short to hold an encoded word.
    /// </exception>
    public DictionaryTable(ZMemory memory, int address, ZTextDecoder decoder, ZTextEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(encoder);

        _memory = memory;
        _decoder = decoder;
        _encoder = encoder;
        Address = address;

        // [zm 13.2] A count, that many separator codes, the entry length,
        // and the number of entries as a word.
        var separatorCount = memory.ReadByte(address);
        WordSeparators = memory.Slice(address + 1, separatorCount).ToArray();
        EntryLength = memory.ReadByte(address + 1 + separatorCount);

        // [zm op:tokenise] A negative count means that many entries,
        // unsorted, which a game uses for a dictionary it edits in play.
        var count = (short)memory.ReadWord(address + 2 + separatorCount);
        IsSorted = count >= 0;
        Count = Math.Abs(count);

        FirstEntryAddress = address + 4 + separatorCount;

        // [zm 13.2] At least 4 bytes in Versions 1 to 3 and 6 from Version
        // 4, since [zm 13.3] and [zm 13.4] that is the encoded word alone.
        if (EntryLength < encoder.EncodedLength)
        {
            throw new InvalidDataException(
                $"Dictionary entries are {EntryLength} bytes, but an encoded word alone needs {encoder.EncodedLength}.");
        }
    }

    /// <summary>
    /// The game's own dictionary, from the header.
    /// </summary>
    public static DictionaryTable Standard(ZMemory memory, StoryHeader header, ZTextDecoder decoder, ZTextEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(header);

        // [zm 13.1]
        return new DictionaryTable(memory, header.DictionaryAddress, decoder, encoder);
    }

    /// <summary>The byte address of the table.</summary>
    public int Address { get; }

    /// <summary>
    /// The ZSCII codes that divide words and count as words themselves.
    /// </summary>
    /// <remarks>
    /// [zm 13.2] Typically the full stop, comma, and double quote, and
    /// never a space. [zm 13.2.1] Each must be defined in ZSCII for both
    /// input and output.
    /// </remarks>
    public byte[] WordSeparators { get; }

    /// <summary>[zm 13.2] The size of each entry in bytes.</summary>
    public int EntryLength { get; }

    /// <summary>The number of entries.</summary>
    public int Count { get; }

    /// <summary>
    /// Whether the entries are in order, which [zm 13.5] the game's own
    /// dictionary must be, and a user dictionary need not be.
    /// </summary>
    public bool IsSorted { get; }

    /// <summary>The byte address of the first entry.</summary>
    public int FirstEntryAddress { get; }

    /// <summary>
    /// [zm 13.3] and [zm 13.4] The size of the encoded word that begins
    /// each entry: 4 bytes in Versions 1 to 3 and 6 from Version 4. The
    /// rest of the entry is data the interpreter ignores.
    /// </summary>
    public int EncodedLength => _encoder.EncodedLength;

    /// <summary>
    /// The byte address of entry <paramref name="index"/>, counting from 0.
    /// </summary>
    public int EntryAddress(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

        return FirstEntryAddress + (index * EntryLength);
    }

    /// <summary>
    /// The encoded text of entry <paramref name="index"/>.
    /// </summary>
    public ReadOnlySpan<byte> EncodedText(int index) => _memory.Slice(EntryAddress(index), EncodedLength);

    /// <summary>Entry <paramref name="index"/>, decoded to text.</summary>
    public string Word(int index) => _decoder.Decode(EntryAddress(index));

    /// <summary>
    /// Finds the entry whose encoded text matches, returning its address
    /// or 0 if there is none.
    /// </summary>
    /// <remarks>
    /// [zm 13.5] Entries are in numerical order of their encoded text read
    /// as a big-endian number, which the remarks note is there so that a
    /// binary search works. Comparing the bytes as unsigned values from
    /// the left is the same ordering. An unsorted user dictionary is
    /// searched from end to end instead.
    /// </remarks>
    public int LookupEncoded(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length != EncodedLength)
        {
            throw new ArgumentException($"An encoded word is {EncodedLength} bytes.", nameof(encoded));
        }

        if (!IsSorted)
        {
            for (var i = 0; i < Count; i++)
            {
                if (EncodedText(i).SequenceEqual(encoded))
                {
                    return EntryAddress(i);
                }
            }

            return 0;
        }

        var low = 0;
        var high = Count - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = EncodedText(middle).SequenceCompareTo(encoded);

            if (comparison == 0)
            {
                return EntryAddress(middle);
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// Encodes a word given as ZSCII and looks it up. The word is used as
    /// given; lower-casing typed input is the lexer's job.
    /// </summary>
    public int Lookup(ReadOnlySpan<byte> zscii) => LookupEncoded(_encoder.EncodeWord(zscii));

    /// <summary>Encodes an ASCII word and looks it up.</summary>
    public int Lookup(string word) => LookupEncoded(_encoder.EncodeWord(word));
}

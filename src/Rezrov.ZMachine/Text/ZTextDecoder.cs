using System.Text;

namespace Rezrov.ZMachine.Text;

/// <summary>
/// Turns Z-encoded text in memory into ZSCII, and from there into
/// ordinary strings.
/// </summary>
/// <remarks>
/// [zm 3.1] Text is stored as Z-characters, five-bit values packed three
/// to a word, and those have to be turned into ZSCII codes before they
/// mean anything. The editor's note on that clause is worth keeping in
/// mind: there are three character sets here, not one. A Z-character is a
/// storage unit whose meaning depends on the current alphabet; ZSCII is
/// what the machine actually operates on; and Unicode only enters at the
/// end, to say what the extra characters look like.
///
/// The decoder follows that split. <see cref="DecodeZscii"/> produces
/// ZSCII and nothing else, and <see cref="Decode(int)"/> is a convenience
/// that runs the result through <see cref="Zscii.ToUnicode"/>.
///
/// Every version-dependent rule in section 3 is applied by the version
/// byte in the header, because the same bytes decode to different text
/// in different versions, and the failure mode for getting that wrong is
/// nonsense rather than an error.
/// </remarks>
public sealed class ZTextDecoder
{
    // [zm 3.2] The top bit of a word is set only on the last word of a
    // string.
    private const int EndOfTextBit = 0x8000;

    private readonly ZMemory _memory;
    private readonly ZMachineVersion _version;
    private readonly int _abbreviationsTableAddress;

    public ZTextDecoder(ZMemory memory, StoryHeader header)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(header);

        _memory = memory;
        _version = header.Version;
        _abbreviationsTableAddress = header.AbbreviationsTableAddress;

        Alphabets = AlphabetTable.ForStory(header, memory);
        ExtraCharacters = UnicodeTranslationTable.ForStory(header, memory);
    }

    /// <summary>The alphabet table this story decodes with.</summary>
    public AlphabetTable Alphabets { get; }

    /// <summary>
    /// The extra character translations this story decodes with.
    /// </summary>
    public UnicodeTranslationTable ExtraCharacters { get; }

    /// <summary>
    /// Decodes the string at <paramref name="address"/> into text.
    /// </summary>
    public string Decode(int address) => Decode(address, out _);

    /// <summary>
    /// Decodes the string at <paramref name="address"/> into text, and
    /// reports the address of the first byte after it. The print opcode
    /// keeps its string inline and has to continue executing from
    /// wherever the string stops, so the end address matters as much as
    /// the text.
    /// </summary>
    public string Decode(int address, out int endAddress)
    {
        var zscii = new List<ushort>();
        endAddress = DecodeZscii(address, zscii);

        var text = new StringBuilder(zscii.Count);
        foreach (var code in zscii)
        {
            if (Zscii.ToUnicode(code, _version, ExtraCharacters) is { } unicode)
            {
                text.Append(unicode);
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Finds where the string at <paramref name="address"/> ends without
    /// decoding it, returning the address of the first byte after it.
    /// </summary>
    /// <remarks>
    /// [zm 3.2] The top bit of a word is set only on the last word of a
    /// string, so the end can be found by scanning words for that bit.
    /// [zm 4.8] The print and print_ret opcodes keep their text inline,
    /// and the instruction decoder needs the end to know where the next
    /// instruction is, whether or not the text is ever printed.
    /// </remarks>
    public int SkipString(int address)
    {
        while ((_memory.ReadWord(address) & EndOfTextBit) == 0)
        {
            address += 2;
        }

        return address + 2;
    }

    /// <summary>
    /// Decodes the string at <paramref name="address"/> into ZSCII codes,
    /// appending them to <paramref name="output"/>, and returns the
    /// address of the first byte after the string.
    /// </summary>
    public int DecodeZscii(int address, List<ushort> output)
    {
        ArgumentNullException.ThrowIfNull(output);

        return new Run(this, output, insideAbbreviation: false).Decode(address);
    }

    // [zm 3.3] Abbreviation n is the nth word of the abbreviations table.
    // That word is a word address, [zm 1.2.2] the only place in the
    // machine that uses one, so it is doubled to get the byte address of
    // the string to print.
    private void DecodeAbbreviation(int number, List<ushort> output)
    {
        // Nothing in the standard says the table address can be zero, but
        // several Infocom files in the corpus have it that way, including
        // both releases of "generic" and every "ziptest", because they use
        // no abbreviations at all. In such a file an abbreviation
        // Z-character has nothing to refer to, and reading the header as
        // if it were the table would produce garbage, so it is an error.
        if (_abbreviationsTableAddress == 0)
        {
            throw new InvalidDataException(
                "The text uses an abbreviation, but the story file has no abbreviations table.");
        }

        var wordAddress = _memory.ReadWord(_abbreviationsTableAddress + (number * 2));

        new Run(this, output, insideAbbreviation: true).Decode(wordAddress * 2);
    }

    /// <summary>
    /// The state of one string being decoded. Abbreviations get a fresh
    /// one, since they start from A0 with nothing pending, exactly as a
    /// top-level string does.
    /// </summary>
    private sealed class Run
    {
        private readonly ZTextDecoder _decoder;
        private readonly List<ushort> _output;
        private readonly bool _insideAbbreviation;

        // [zm 3.2.1] The alphabet the next Z-character is read against.
        private Alphabet _current = Alphabet.A0;

        // [zm 3.2.2] In Versions 1 and 2, the alphabet to fall back to
        // after a temporary shift, which a shift lock changes. From
        // Version 3 there are no locks, so this stays A0 for good.
        private Alphabet _locked = Alphabet.A0;

        // When non-zero, the previous Z-character introduced an
        // abbreviation and this is which of the three banks it named.
        private int _abbreviationBank;

        // [zm 3.4] Progress through a ZSCII escape: 0 when none is in
        // flight, 1 when the next Z-character is the top five bits, 2
        // when it is the bottom five.
        private int _escapeStage;
        private int _escapeTop;

        public Run(ZTextDecoder decoder, List<ushort> output, bool insideAbbreviation)
        {
            _decoder = decoder;
            _output = output;
            _insideAbbreviation = insideAbbreviation;
        }

        public int Decode(int address)
        {
            while (true)
            {
                // [zm 3.2] Three five-bit Z-characters in each word, most
                // significant first, with the top bit marking the last
                // word of the string.
                var word = _decoder._memory.ReadWord(address);
                address += 2;

                Feed((word >> 10) & 0x1F);
                Feed((word >> 5) & 0x1F);
                Feed(word & 0x1F);

                if ((word & EndOfTextBit) != 0)
                {
                    break;
                }
            }

            // [zm 3.6.1] The string may end in the middle of an escape or
            // an abbreviation, and the partial construction is simply
            // ignored. Nothing to do: whatever was pending is dropped with
            // this object.
            return address;
        }

        private void Feed(int z)
        {
            // A construction already under way takes the next Z-character
            // whatever its value, before any of the rules below apply.
            if (_escapeStage == 1)
            {
                _escapeTop = z;
                _escapeStage = 2;
                return;
            }

            if (_escapeStage == 2)
            {
                // [zm 3.4] Top five bits, then bottom five, of a ten-bit
                // ZSCII code.
                _output.Add((ushort)((_escapeTop << 5) | z));
                _escapeStage = 0;
                _current = _locked;
                return;
            }

            if (_abbreviationBank != 0)
            {
                // [zm 3.3] Entry 32(z-1)+x, with z the introducing
                // Z-character and x this one.
                var bank = _abbreviationBank;
                _abbreviationBank = 0;
                _decoder.DecodeAbbreviation((32 * (bank - 1)) + z, _output);
                _current = _locked;
                return;
            }

            switch (z)
            {
                case 0:
                    // [zm 3.5.1] Always a space, in every alphabet and
                    // every version.
                    _output.Add(Zscii.Space);
                    _current = _locked;
                    return;

                case 1:
                    if (_decoder._version == ZMachineVersion.V1)
                    {
                        // [zm 3.5.2] Version 1 has no abbreviations, and
                        // uses this Z-character for a newline instead.
                        _output.Add(Zscii.Newline);
                        _current = _locked;
                        return;
                    }

                    // [zm 3.3] From Version 2, the first abbreviation bank.
                    BeginAbbreviation(1);
                    return;

                case 2 or 3:
                    if (_decoder._version <= ZMachineVersion.V2)
                    {
                        // [zm 3.2.2] A temporary shift, for one character
                        // only, moving one alphabet forward for 2 and two
                        // forward for 3. The standard says the new alphabet
                        // depends on the current one without saying what
                        // happens when shifts pile up, so this follows
                        // Frotz and measures from the locked alphabet, not
                        // from a shift already in effect.
                        _current = Shift(_locked, z - 1);
                        return;
                    }

                    // [zm 3.3] From Version 3, the second and third
                    // abbreviation banks.
                    BeginAbbreviation(z);
                    return;

                case 4 or 5:
                    if (_decoder._version <= ZMachineVersion.V2)
                    {
                        // [zm 3.2.2] A shift lock, using the same table as
                        // the temporary shifts but staying in force.
                        _locked = Shift(_locked, z - 3);
                        _current = _locked;
                        return;
                    }

                    // [zm 3.2.3] From Version 3, a shift for the next
                    // character only, and absolute: 4 selects A1 and 5
                    // selects A2. There are no locks.
                    _current = z == 4 ? Alphabet.A1 : Alphabet.A2;
                    return;

                default:
                    if (_current == Alphabet.A2 && z == 6)
                    {
                        // [zm 3.4] The ZSCII escape, which is Z-character
                        // 6 read in A2 and not the value 6 on its own.
                        _escapeStage = 1;
                        return;
                    }

                    // [zm 3.5] Everything else goes through the alphabet
                    // table, including the newline at Z-character 7 of A2.
                    _output.Add(_decoder.Alphabets[_current, z]);
                    _current = _locked;
                    return;
            }
        }

        private void BeginAbbreviation(int bank)
        {
            // [zm 3.3.1] An abbreviation must not itself use abbreviations.
            // A story file that does this is broken, and following it
            // would risk unbounded recursion, so it is an error rather
            // than something to work around.
            if (_insideAbbreviation)
            {
                throw new InvalidDataException(
                    "An abbreviation string uses an abbreviation, which the standard does not allow.");
            }

            _abbreviationBank = bank;
        }

        // [zm 3.2.2] The shift table in the standard, as arithmetic: from
        // any alphabet, moving forward one lands on the next and moving
        // forward two lands on the one after, wrapping from A2 to A0.
        private static Alphabet Shift(Alphabet from, int forward) =>
            (Alphabet)(((int)from + forward) % 3);
    }
}

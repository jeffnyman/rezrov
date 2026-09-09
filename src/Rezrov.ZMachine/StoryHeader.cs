using System.Diagnostics;
using System.Text;

namespace Rezrov.ZMachine;

/// <summary>
/// A typed view of the story file header, the first 64 bytes of memory.
/// </summary>
/// <remarks>
/// [zm 11.1] Every property here corresponds to a row of the header table,
/// and the offsets below are that table's "Hex" column. The view reads
/// through to memory on every access rather than copying values out at
/// load time, because [zm 11.1.1] some fields are legally changed by the
/// game during play and others by the interpreter, and a snapshot would
/// go stale.
///
/// Where the same bytes mean something different in different versions,
/// the accessor for the wrong version throws rather than returning a
/// number that looks plausible and is not. That is deliberate: the
/// editor's note on [zm 1.2.3] points out that a misread address fails as
/// a jump into the middle of nowhere, not as a clean error.
///
/// Fields the table marks "Con" are meaningful only by convention and an
/// interpreter need not act on them. They are exposed here anyway, since
/// they are useful for identifying a file.
/// </remarks>
public sealed class StoryHeader
{
    private const int VersionOffset = 0x00;
    private const int Flags1Offset = 0x01;
    private const int ReleaseOffset = 0x02;
    private const int HighMemoryBaseOffset = 0x04;
    private const int InitialProgramCounterOffset = 0x06;
    private const int DictionaryOffset = 0x08;
    private const int ObjectTableOffset = 0x0A;
    private const int GlobalVariablesOffset = 0x0C;
    private const int StaticMemoryBaseOffset = 0x0E;
    private const int Flags2Offset = 0x10;
    private const int SerialCodeOffset = 0x12;
    private const int SerialCodeLength = 6;
    private const int AbbreviationsTableOffset = 0x18;
    private const int FileLengthOffset = 0x1A;
    private const int ChecksumOffset = 0x1C;
    private const int InterpreterNumberOffset = 0x1E;
    private const int InterpreterVersionOffset = 0x1F;
    private const int ScreenHeightLinesOffset = 0x20;
    private const int ScreenWidthCharactersOffset = 0x21;
    private const int ScreenWidthUnitsOffset = 0x22;
    private const int ScreenHeightUnitsOffset = 0x24;
    private const int FontByte26Offset = 0x26;
    private const int FontByte27Offset = 0x27;
    private const int RoutinesOffsetOffset = 0x28;
    private const int StaticStringsOffsetOffset = 0x2A;
    private const int DefaultBackgroundColorOffset = 0x2C;
    private const int DefaultForegroundColorOffset = 0x2D;
    private const int TerminatingCharactersTableOffset = 0x2E;
    private const int OutputStream3WidthOffset = 0x30;
    private const int StandardRevisionMajorOffset = 0x32;
    private const int StandardRevisionMinorOffset = 0x33;
    private const int AlphabetTableOffset = 0x34;
    private const int HeaderExtensionTableOffset = 0x36;
    private const int UserNameOffset = 0x38;
    private const int UserNameLength = 8;
    private const int InformVersionOffset = 0x3C;
    private const int InformVersionLength = 4;

    private readonly ZMemory _memory;

    /// <summary>
    /// Creates a header view over <paramref name="memory"/>, checking the
    /// handful of things that must hold for the rest of the header to be
    /// trusted at all.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The version byte is outside 1 to 8, or the memory layout the header
    /// describes cannot fit in the bytes provided.
    /// </exception>
    public StoryHeader(ZMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        _memory = memory;

        // [zm 11.1] The version number is 1 to 8. Everything else in this
        // class switches on it, so an unknown version has to stop here.
        var version = memory.ReadByte(VersionOffset);
        if (version is < 1 or > 8)
        {
            throw new InvalidDataException(
                $"Byte $00 holds the version number, which must be 1 to 8, but it is {version}.");
        }

        // [zm 1.1] Dynamic memory runs from 0 up to the byte before the
        // static memory base, and must contain at least 64 bytes. Static
        // memory must end by the last byte of the file, so its base cannot
        // be past the end of the file either.
        var staticBase = memory.ReadWord(StaticMemoryBaseOffset);
        if (staticBase < ZMemory.HeaderLength)
        {
            throw new InvalidDataException(
                $"The static memory base at $0E is {staticBase:X4}, but dynamic memory must hold at least the {ZMemory.HeaderLength} byte header.");
        }

        if (staticBase > memory.Length)
        {
            throw new InvalidDataException(
                $"The static memory base at $0E is {staticBase:X4}, past the end of a {memory.Length} byte file.");
        }

        // [zm 1.1] High memory may overlap the top of static memory but
        // not dynamic memory, so it cannot begin below the static base.
        var highBase = memory.ReadWord(HighMemoryBaseOffset);
        if (highBase < staticBase)
        {
            throw new InvalidDataException(
                $"The high memory base at $04 is {highBase:X4}, which overlaps dynamic memory below the static base at {staticBase:X4}.");
        }

        // [zm op:verify] A file may be longer than the length its header
        // declares, but never shorter. A shorter file has been truncated.
        var fileLength = FileLength;
        if (fileLength > memory.Length)
        {
            throw new InvalidDataException(
                $"The header declares a file length of {fileLength} bytes, but only {memory.Length} were provided.");
        }
    }

    public ZMachineVersion Version => (ZMachineVersion)_memory.ReadByte(VersionOffset);

    /// <summary>
    /// Flags 1 as laid out for Versions 1 to 3.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The version is 4 or later.
    /// </exception>
    public Flags1Versions1To3 Flags1Versions1To3
    {
        get
        {
            // [zm 11.1.4] The same byte has a different layout from
            // Version 4 onward.
            if (Version >= ZMachineVersion.V4)
            {
                throw OnlyMeaningfulIn(nameof(Flags1Versions1To3), "Versions 1 to 3");
            }

            return (Flags1Versions1To3)_memory.ReadByte(Flags1Offset);
        }

        set
        {
            if (Version >= ZMachineVersion.V4)
            {
                throw OnlyMeaningfulIn(nameof(Flags1Versions1To3), "Versions 1 to 3");
            }

            _memory.WriteByte(Flags1Offset, (byte)value);
        }
    }

    /// <summary>
    /// Flags 1 as laid out from Version 4 onward. The interpreter writes
    /// it to say what it can do, which is why there is a setter.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The version is 3 or earlier.
    /// </exception>
    public Flags1FromVersion4 Flags1FromVersion4
    {
        get
        {
            if (Version <= ZMachineVersion.V3)
            {
                throw OnlyMeaningfulIn(nameof(Flags1FromVersion4), "Version 4 and later");
            }

            return (Flags1FromVersion4)_memory.ReadByte(Flags1Offset);
        }

        set
        {
            if (Version <= ZMachineVersion.V3)
            {
                throw OnlyMeaningfulIn(nameof(Flags1FromVersion4), "Version 4 and later");
            }

            _memory.WriteByte(Flags1Offset, (byte)value);
        }
    }

    /// <summary>The release number. Conventional.</summary>
    public ushort Release => _memory.ReadWord(ReleaseOffset);

    /// <summary>
    /// The byte address where high memory begins.
    /// </summary>
    /// <remarks>
    /// [zm 1.1] High memory runs from here to the end of the file. It may
    /// overlap the top of static memory. The original idea was that
    /// everything below this mark had to be in RAM while everything above
    /// it could be paged in from disc as needed.
    /// </remarks>
    public ushort HighMemoryBase => _memory.ReadWord(HighMemoryBaseOffset);

    /// <summary>
    /// The byte address of the first instruction to execute.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The version is 6.
    /// </exception>
    public ushort InitialProgramCounter
    {
        get
        {
            // [zm 11.1] In Version 6 this word is instead a packed address
            // of the main routine, which has to be called rather than
            // jumped to. [zm 1.2.4] Versions 7 and 8 follow Version 5, so
            // only Version 6 differs.
            if (Version == ZMachineVersion.V6)
            {
                throw OnlyMeaningfulIn(nameof(InitialProgramCounter), "every Version except 6");
            }

            return _memory.ReadWord(InitialProgramCounterOffset);
        }
    }

    /// <summary>
    /// The packed address of the main routine. Version 6 only.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The version is not 6.
    /// </exception>
    public ushort MainRoutinePackedAddress
    {
        get
        {
            if (Version != ZMachineVersion.V6)
            {
                throw OnlyMeaningfulIn(nameof(MainRoutinePackedAddress), "Version 6");
            }

            return _memory.ReadWord(InitialProgramCounterOffset);
        }
    }

    /// <summary>The byte address of the dictionary.</summary>
    public ushort DictionaryAddress => _memory.ReadWord(DictionaryOffset);

    /// <summary>The byte address of the object table.</summary>
    public ushort ObjectTableAddress => _memory.ReadWord(ObjectTableOffset);

    /// <summary>The byte address of the global variables table.</summary>
    public ushort GlobalVariablesAddress => _memory.ReadWord(GlobalVariablesOffset);

    /// <summary>
    /// The byte address where static memory begins, which is also the end
    /// of dynamic memory.
    /// </summary>
    /// <remarks>
    /// [zm 1.1] Dynamic memory is everything below this. Its extent is the
    /// only region boundary stored anywhere; where static memory ends is
    /// not recorded at all.
    /// </remarks>
    public ushort StaticMemoryBase => _memory.ReadWord(StaticMemoryBaseOffset);

    /// <summary>
    /// Flags 2. See <see cref="ZMachine.Flags2"/> for the bits. Both the
    /// game and the interpreter write to it: the game to ask for things,
    /// and the interpreter to clear the bits for things it cannot give.
    /// </summary>
    public Flags2 Flags2
    {
        get => (Flags2)_memory.ReadWord(Flags2Offset);
        set => _memory.WriteWord(Flags2Offset, (ushort)value);
    }

    /// <summary>
    /// Six characters of ASCII. Conventional.
    /// </summary>
    /// <remarks>
    /// [zm 11.1] In Version 2 this is just a serial code. From Version 3 it
    /// is conventionally the compilation date as YYMMDD, and the standard's
    /// remarks note that any date before 930000 is either Infocom or a
    /// fake, since Inform did not exist yet.
    /// </remarks>
    public string SerialCode => Encoding.ASCII.GetString(_memory.Slice(SerialCodeOffset, SerialCodeLength));

    /// <summary>
    /// The byte address of the abbreviations table. Version 2 and later.
    /// </summary>
    public ushort AbbreviationsTableAddress => _memory.ReadWord(AbbreviationsTableOffset);

    /// <summary>
    /// The length of the story file in bytes, or 0 if the file does not
    /// record it.
    /// </summary>
    /// <remarks>
    /// [zm 11.1.6] The header word holds the length divided by a constant
    /// so that it fits in 16 bits: 2 for Versions 1 to 3, 4 for Versions 4
    /// and 5, and 8 for Version 6 and later. This property multiplies it
    /// back out. [zm 11.1] Some early Version 3 files carry neither a
    /// length nor a checksum, and read as 0 here.
    /// </remarks>
    public int FileLength
    {
        get
        {
            var stored = _memory.ReadWord(FileLengthOffset);

            var scale = Version switch
            {
                ZMachineVersion.V1 or ZMachineVersion.V2 or ZMachineVersion.V3 => 2,
                ZMachineVersion.V4 or ZMachineVersion.V5 => 4,
                ZMachineVersion.V6 or ZMachineVersion.V7 or ZMachineVersion.V8 => 8,
                _ => throw new UnreachableException("The constructor rejects versions outside 1 to 8."),
            };

            return stored * scale;
        }
    }

    /// <summary>
    /// The checksum recorded in the header. Compare it against
    /// <see cref="ComputeChecksum"/> to verify the file.
    /// </summary>
    public ushort Checksum => _memory.ReadWord(ChecksumOffset);

    /// <summary>
    /// Whether this file declares its own length, which is what makes a
    /// checksum verifiable at all.
    /// </summary>
    /// <remarks>
    /// [zm 11.1] Some early Version 3 files record neither a length nor a
    /// checksum, and with no length there is nothing to sum to. A length
    /// with a zero checksum is a different case: at least one 1993 Inform
    /// file in the corpus, dejavu-r1-s930921.z3, declares a length but
    /// leaves the checksum as 0. That file can be verified and simply
    /// fails, which is also what the verify opcode would report for it.
    /// </remarks>
    public bool HasFileLength => FileLength != 0;

    /// <summary>
    /// Sums the file the way the verify opcode does, so the result can be
    /// compared with <see cref="Checksum"/>.
    /// </summary>
    /// <remarks>
    /// [zm op:verify] The checksum is the sum of every byte from $0040 up
    /// to the length declared in the header, modulo $10000. The
    /// calculation must stop at that declared length: files are often
    /// padded past it, and many Infocom files have non-zero bytes in the
    /// padding, so summing the whole file gives the wrong answer.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The file records no length, so there is nothing to sum to.
    /// </exception>
    public ushort ComputeChecksum()
    {
        if (!HasFileLength)
        {
            throw new InvalidOperationException(
                "This story file records no length, so there is nothing to sum to.");
        }

        var sum = 0;
        foreach (var b in _memory.Slice(ZMemory.HeaderLength, FileLength - ZMemory.HeaderLength))
        {
            sum += b;
        }

        // Modulo $10000 is exactly what truncating to 16 bits does.
        return (ushort)sum;
    }

    /// <summary>
    /// Whether the file's bytes add up to the checksum in its header.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The file records no length, so there is nothing to verify.
    /// </exception>
    public bool VerifyChecksum() => ComputeChecksum() == Checksum;

    /// <summary>
    /// Which of Infocom's platforms the interpreter claims to be. Version 4
    /// and later. Set by the interpreter.
    /// </summary>
    public InterpreterNumber InterpreterNumber
    {
        get => (InterpreterNumber)_memory.ReadByte(InterpreterNumberOffset);
        set => _memory.WriteByte(InterpreterNumberOffset, (byte)value);
    }

    /// <summary>
    /// The interpreter's version. Version 4 and later. Set by the
    /// interpreter.
    /// </summary>
    /// <remarks>
    /// [zm 11.1.3.1] Conventionally an ASCII upper-case letter in Versions
    /// 4 and 5, while Infocom's Version 6 interpreters stored a number.
    /// </remarks>
    public byte InterpreterVersion
    {
        get => _memory.ReadByte(InterpreterVersionOffset);
        set => _memory.WriteByte(InterpreterVersionOffset, value);
    }

    /// <summary>
    /// Screen height in lines. Version 4 and later. Set by the interpreter.
    /// </summary>
    public byte ScreenHeightLines
    {
        get => _memory.ReadByte(ScreenHeightLinesOffset);
        set => _memory.WriteByte(ScreenHeightLinesOffset, value);
    }

    /// <summary>
    /// Screen width in characters. Version 4 and later. Set by the
    /// interpreter.
    /// </summary>
    public byte ScreenWidthCharacters
    {
        get => _memory.ReadByte(ScreenWidthCharactersOffset);
        set => _memory.WriteByte(ScreenWidthCharactersOffset, value);
    }

    /// <summary>
    /// Screen width in units. Version 5 and later. Set by the interpreter.
    /// </summary>
    public ushort ScreenWidthUnits
    {
        get => _memory.ReadWord(ScreenWidthUnitsOffset);
        set => _memory.WriteWord(ScreenWidthUnitsOffset, value);
    }

    /// <summary>
    /// Screen height in units. Version 5 and later. Set by the interpreter.
    /// </summary>
    public ushort ScreenHeightUnits
    {
        get => _memory.ReadWord(ScreenHeightUnitsOffset);
        set => _memory.WriteWord(ScreenHeightUnitsOffset, value);
    }

    /// <summary>
    /// Font width in units, defined as the width of a '0'. Version 5 and
    /// later. Set by the interpreter.
    /// </summary>
    /// <remarks>
    /// [zm 11.1] Bytes $26 and $27 swap meaning between Version 5 and
    /// Version 6: in Version 5 the width is at $26 and the height at $27,
    /// and Version 6 has them the other way round. This property and
    /// <see cref="FontHeightUnits"/> resolve that, so callers never need to
    /// know which byte is which.
    /// </remarks>
    public byte FontWidthUnits
    {
        get => _memory.ReadByte(Version == ZMachineVersion.V6 ? FontByte27Offset : FontByte26Offset);
        set => _memory.WriteByte(Version == ZMachineVersion.V6 ? FontByte27Offset : FontByte26Offset, value);
    }

    /// <summary>
    /// Font height in units. Version 5 and later. Set by the interpreter.
    /// See <see cref="FontWidthUnits"/> for the Version 6 byte swap.
    /// </summary>
    public byte FontHeightUnits
    {
        get => _memory.ReadByte(Version == ZMachineVersion.V6 ? FontByte26Offset : FontByte27Offset);
        set => _memory.WriteByte(Version == ZMachineVersion.V6 ? FontByte26Offset : FontByte27Offset, value);
    }

    /// <summary>
    /// The routines offset, already divided by 8 as stored. Versions 6
    /// and 7.
    /// </summary>
    /// <remarks>
    /// [zm 1.2.3] This is R_O in the packed address formula. Use
    /// <see cref="UnpackRoutineAddress"/> rather than applying it by hand.
    /// </remarks>
    public ushort RoutinesOffset => _memory.ReadWord(RoutinesOffsetOffset);

    /// <summary>
    /// The static strings offset, already divided by 8 as stored. Versions
    /// 6 and 7.
    /// </summary>
    /// <remarks>
    /// [zm 1.2.3] This is S_O in the packed address formula. Use
    /// <see cref="UnpackStringAddress"/> rather than applying it by hand.
    /// </remarks>
    public ushort StaticStringsOffset => _memory.ReadWord(StaticStringsOffsetOffset);

    /// <summary>
    /// Default background color. Version 5 and later. Set by the
    /// interpreter.
    /// </summary>
    /// <remarks>
    /// [zm 11.1] This and the foreground color are single bytes at $2C and
    /// $2D. The revised standard's table lists both with a length of 2,
    /// which cannot be right since they would overlap, and the original
    /// 1.1 table has them at one byte each.
    /// </remarks>
    public byte DefaultBackgroundColor
    {
        get => _memory.ReadByte(DefaultBackgroundColorOffset);
        set => _memory.WriteByte(DefaultBackgroundColorOffset, value);
    }

    /// <summary>
    /// Default foreground color. Version 5 and later. Set by the
    /// interpreter. See <see cref="DefaultBackgroundColor"/>.
    /// </summary>
    public byte DefaultForegroundColor
    {
        get => _memory.ReadByte(DefaultForegroundColorOffset);
        set => _memory.WriteByte(DefaultForegroundColorOffset, value);
    }

    /// <summary>
    /// The byte address of the terminating characters table. Version 5 and
    /// later.
    /// </summary>
    public ushort TerminatingCharactersTableAddress => _memory.ReadWord(TerminatingCharactersTableOffset);

    /// <summary>
    /// Total width in pixels of text sent to output stream 3. Version 6.
    /// Set by the interpreter.
    /// </summary>
    public ushort OutputStream3Width => _memory.ReadWord(OutputStream3WidthOffset);

    /// <summary>
    /// The major part of the standard revision the interpreter follows.
    /// </summary>
    /// <remarks>
    /// [zm 11.1.5] An interpreter that obeys revision n.m of the standard
    /// writes n to $32 and m to $33. One that does not follow the standard
    /// leaves both as 0. These are the only two bytes Infocom never used.
    /// </remarks>
    public byte StandardRevisionMajor
    {
        get => _memory.ReadByte(StandardRevisionMajorOffset);
        set => _memory.WriteByte(StandardRevisionMajorOffset, value);
    }

    /// <summary>
    /// The minor part of the standard revision the interpreter follows.
    /// See <see cref="StandardRevisionMajor"/>.
    /// </summary>
    public byte StandardRevisionMinor
    {
        get => _memory.ReadByte(StandardRevisionMinorOffset);
        set => _memory.WriteByte(StandardRevisionMinorOffset, value);
    }

    /// <summary>
    /// The byte address of the alphabet table, or 0 to use the default
    /// alphabet. Version 5 and later.
    /// </summary>
    public ushort AlphabetTableAddress => _memory.ReadWord(AlphabetTableOffset);

    /// <summary>
    /// The byte address of the header extension table, or 0 if there is
    /// none. Version 5 and later.
    /// </summary>
    /// <remarks>
    /// [zm 11.1.7] The extension table is a table of words whose first
    /// word says how many more follow. Read it through
    /// <see cref="ReadExtensionWord"/>, which applies the rule that a
    /// missing word reads as 0.
    /// </remarks>
    public ushort HeaderExtensionTableAddress => _memory.ReadWord(HeaderExtensionTableOffset);

    /// <summary>
    /// Eight bytes of ASCII that Infocom used for the player's mainframe
    /// user name. Conventional, and all zero in shipped story files.
    /// </summary>
    /// <remarks>
    /// [zm 11.1] These eight bytes at $38 overlap the four at $3C that
    /// Inform uses for <see cref="InformVersion"/>. That is not a mistake
    /// in the table: the two fields come from different compilers and no
    /// file carries both, so a file compiled by Inform reads its version
    /// string back as the tail of this one.
    /// </remarks>
    public string UserName => Encoding.ASCII.GetString(_memory.Slice(UserNameOffset, UserNameLength));

    /// <summary>
    /// Four bytes of ASCII giving the Inform version that compiled the
    /// file, such as "6.11". Conventional, and empty for anything Inform
    /// did not compile.
    /// </summary>
    /// <remarks>
    /// [zm 11.1] The standard's remarks note this is the easiest way to
    /// tell an Inform 6 story file from every other kind.
    /// </remarks>
    public string InformVersion => Encoding.ASCII.GetString(_memory.Slice(InformVersionOffset, InformVersionLength));

    /// <summary>
    /// Reads word <paramref name="index"/> of the header extension table.
    /// Word 0 is the count of words that follow it.
    /// </summary>
    /// <remarks>
    /// [zm 11.1.7.1] A word beyond the table's length, or any word when
    /// there is no table, reads as 0. That rule is what lets the rest of
    /// the interpreter ask for extension fields without checking first.
    /// </remarks>
    public ushort ReadExtensionWord(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        var table = HeaderExtensionTableAddress;
        if (table == 0)
        {
            return 0;
        }

        var count = _memory.ReadWord(table);
        if (index > count)
        {
            return 0;
        }

        return _memory.ReadWord(table + (index * 2));
    }

    /// <summary>
    /// Writes word <paramref name="index"/> of the header extension table.
    /// </summary>
    /// <remarks>
    /// [zm 11.1.7.2] Writing beyond the table's length, or when there is
    /// no table, does nothing at all.
    /// </remarks>
    public void WriteExtensionWord(int index, ushort value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        var table = HeaderExtensionTableAddress;
        if (table == 0)
        {
            return;
        }

        var count = _memory.ReadWord(table);
        if (index > count)
        {
            return;
        }

        _memory.WriteWord(table + (index * 2), value);
    }

    // [zm 11.1.7.3] The words allocated so far in the extension table.

    /// <summary>
    /// X coordinate of the mouse after a click. Extension word 1. Set by
    /// the interpreter.
    /// </summary>
    public ushort MouseX => ReadExtensionWord(1);

    /// <summary>
    /// Y coordinate of the mouse after a click. Extension word 2. Set by
    /// the interpreter.
    /// </summary>
    public ushort MouseY => ReadExtensionWord(2);

    /// <summary>
    /// The byte address of the Unicode translation table, or 0. Extension
    /// word 3.
    /// </summary>
    public ushort UnicodeTranslationTableAddress => ReadExtensionWord(3);

    /// <summary>
    /// Flags 3. Extension word 4. See <see cref="ZMachine.Flags3"/> for the
    /// bits.
    /// </summary>
    public Flags3 Flags3 => (Flags3)ReadExtensionWord(4);

    /// <summary>
    /// True default foreground color. Extension word 5. Set by the
    /// interpreter.
    /// </summary>
    public ushort TrueDefaultForegroundColor => ReadExtensionWord(5);

    /// <summary>
    /// True default background color. Extension word 6. Set by the
    /// interpreter.
    /// </summary>
    public ushort TrueDefaultBackgroundColor => ReadExtensionWord(6);

    /// <summary>
    /// Converts a packed routine address into a byte address.
    /// </summary>
    /// <remarks>
    /// [zm 1.2.3] A packed address is scaled so that a routine anywhere in
    /// a large file can be named in 16 bits: doubled in Versions 1 to 3,
    /// quadrupled in Versions 4 and 5, and multiplied by 8 in Version 8.
    /// Versions 6 and 7 quadruple and then add 8 times the routines
    /// offset from the header, which is what lets them reach 512K while
    /// still scaling by 4.
    ///
    /// Routines and strings unpack differently in Versions 6 and 7, so
    /// there are two methods. The editor's note observes that a single
    /// shared unpack helper works until the first Version 6 file, at which
    /// point every string prints garbage while routines still call fine.
    /// </remarks>
    public int UnpackRoutineAddress(ushort packed) => Version switch
    {
        ZMachineVersion.V1 or ZMachineVersion.V2 or ZMachineVersion.V3 => packed * 2,
        ZMachineVersion.V4 or ZMachineVersion.V5 => packed * 4,
        ZMachineVersion.V6 or ZMachineVersion.V7 => (packed * 4) + (RoutinesOffset * 8),
        ZMachineVersion.V8 => packed * 8,
        _ => throw new UnreachableException("The constructor rejects versions outside 1 to 8."),
    };

    /// <summary>
    /// Converts a packed string address into a byte address. See
    /// <see cref="UnpackRoutineAddress"/> for the formula and for why this
    /// is a separate method.
    /// </summary>
    public int UnpackStringAddress(ushort packed) => Version switch
    {
        ZMachineVersion.V1 or ZMachineVersion.V2 or ZMachineVersion.V3 => packed * 2,
        ZMachineVersion.V4 or ZMachineVersion.V5 => packed * 4,
        ZMachineVersion.V6 or ZMachineVersion.V7 => (packed * 4) + (StaticStringsOffset * 8),
        ZMachineVersion.V8 => packed * 8,
        _ => throw new UnreachableException("The constructor rejects versions outside 1 to 8."),
    };

    private InvalidOperationException OnlyMeaningfulIn(string field, string versions) =>
        new($"{field} is only meaningful in {versions}, and this story file is Version {(int)Version}.");
}

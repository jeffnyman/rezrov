using System.Buffers.Binary;
using System.Text;

namespace Rezrov.Glulx;

/// <summary>
/// The first 36 bytes of a Glulx game file: nine big-endian words that
/// lay out memory and say where execution begins.
/// </summary>
/// <remarks>
/// [glulx #the-header] The header is always in ROM, so its contents
/// cannot change during execution. That is why this is a snapshot taken
/// when the file is read rather than a view through to live memory: no
/// field here can go stale, and the memory that the header describes
/// does not exist until the header has been read.
///
/// The constructor checks everything the specification says the
/// interpreter should validate, plus the few things that must hold for
/// memory to be laid out at all. A file that fails is refused with the
/// reason, since running it would fail later in a far less clear way.
/// </remarks>
public sealed class GlulxHeader
{
    /// <summary>[glulx #the-header] The header is 36 bytes.</summary>
    public const int Length = 36;

    /// <summary>
    /// [glulx #the-header] The version of the specification this
    /// interpreter is written to, as the header stores versions: the
    /// major number in the upper 16 bits, then a byte each for the minor
    /// and subminor numbers. [glulx #opcodes_misc] The GlulxVersion
    /// gestalt selector returns this value.
    /// </summary>
    public const uint SpecificationVersion = 0x00030103;

    /// <summary>
    /// [glulx #the-header] A 3.* interpreter accepts 2.0 files as well,
    /// since 2.0 only lacks Unicode, so the oldest acceptable version is
    /// 2.0.0.
    /// </summary>
    public const uint OldestAcceptedVersion = 0x00020000;

    /// <summary>
    /// [glulx #the-header] The minor version must be at most this
    /// specification's and the subminor version does not matter, so
    /// 3.1.255 is the newest acceptable version.
    /// </summary>
    public const uint NewestAcceptedVersion = 0x000301FF;

    /// <summary>
    /// [glulx #the-memory-map] RAMSTART, EXTSTART, and ENDMEM must be
    /// aligned on 256-byte boundaries, [glulx #stack] and so must the
    /// stack size.
    /// </summary>
    public const uint Alignment = 0x100;

    private const int MagicOffset = 0x00;
    private const int VersionOffset = 0x04;
    private const int RamStartOffset = 0x08;
    private const int ExtStartOffset = 0x0C;
    private const int EndMemOffset = 0x10;
    private const int StackSizeOffset = 0x14;
    private const int StartFunctionOffset = 0x18;
    private const int DecodingTableOffset = 0x1C;
    private const int ChecksumOffset = 0x20;

    // The word after the header is not part of the specification, but
    // Inform writes 'Info' there and follows it with fields of its own.
    private const int LayoutOffset = 0x24;
    private const int InformVersionOffset = 0x2C;
    private const int InformVersionLength = 4;
    private const int InformReleaseOffset = 0x34;
    private const int InformSerialOffset = 0x36;
    private const int InformSerialLength = 6;
    private const int InformLayoutLength = 0x3C;

    /// <summary>
    /// Reads and checks the header at the start of
    /// <paramref name="file"/>, the whole game file as loaded.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The file is not a Glulx file this interpreter can run: the magic
    /// number or the version is wrong, the memory boundaries are out of
    /// order or unaligned, or the file is shorter than its header says.
    /// </exception>
    public GlulxHeader(ReadOnlySpan<byte> file)
    {
        if (file.Length < Length)
        {
            throw new InvalidDataException(
                $"A Glulx file begins with a {Length} byte header, but this file is only {file.Length} bytes long.");
        }

        // [glulx #the-header] The magic number is 47 6C 75 6C, 'Glul'.
        if (!file.Slice(MagicOffset, 4).SequenceEqual("Glul"u8))
        {
            throw new InvalidDataException("The file does not begin with the Glulx magic number 'Glul'.");
        }

        // [glulx #the-header] The interpreter validates the version: the
        // major number must match and the minor number must not exceed
        // this specification's, with the exception that 2.0 files are
        // accepted by a 3.* interpreter.
        Version = Word(file, VersionOffset);
        if (Version < OldestAcceptedVersion)
        {
            throw new InvalidDataException(
                $"The file is Glulx version {VersionText}, which is too old: the oldest version this interpreter runs is {Describe(OldestAcceptedVersion)}.");
        }

        if (Version > NewestAcceptedVersion)
        {
            throw new InvalidDataException(
                $"The file is Glulx version {VersionText}, which is too new: this interpreter is written to version {Describe(SpecificationVersion)}.");
        }

        RamStart = Word(file, RamStartOffset);
        ExtStart = Word(file, ExtStartOffset);
        EndMem = Word(file, EndMemOffset);
        StackSize = Word(file, StackSizeOffset);
        StartFunction = Word(file, StartFunctionOffset);
        DecodingTable = Word(file, DecodingTableOffset);
        Checksum = Word(file, ChecksumOffset);

        // [glulx #the-memory-map] The three boundaries and [glulx #stack]
        // the stack size must be multiples of 256. The reference
        // interpreter only warns about this, but a file that breaks a
        // "must" of the specification is not one this interpreter
        // promises to run.
        CheckAligned(RamStart, "RAMSTART");
        CheckAligned(ExtStart, "EXTSTART");
        CheckAligned(EndMem, "ENDMEM");
        CheckAligned(StackSize, "The stack size");

        // [glulx #the-memory-map] ROM must be at least 256 bytes so the
        // header fits in it, and the segments follow one another, though
        // any of them may be empty.
        if (RamStart < Alignment)
        {
            throw new InvalidDataException(
                $"RAMSTART is {RamStart:X8}, but ROM must be at least {Alignment:X} bytes long to hold the header.");
        }

        if (ExtStart < RamStart || EndMem < ExtStart)
        {
            throw new InvalidDataException(
                $"The memory boundaries are out of order: RAMSTART {RamStart:X8}, EXTSTART {ExtStart:X8}, ENDMEM {EndMem:X8} must not decrease.");
        }

        // [glulx #stack] The stack size is a multiple of 256, so a stack
        // of zero bytes is the only aligned size too small to hold even
        // one call frame.
        if (StackSize < Alignment)
        {
            throw new InvalidDataException(
                $"The stack size is {StackSize}, but a stack must be at least {Alignment} bytes.");
        }

        // [glulx #the-memory-map] A game file stores the data from 0 to
        // EXTSTART, so a shorter file has been cut off. A longer one is
        // allowed: the header says what to load and the rest is ignored.
        if ((uint)file.Length < ExtStart)
        {
            throw new InvalidDataException(
                $"EXTSTART is {ExtStart:X8}, so the file should hold at least that many bytes, but it holds {file.Length:X8}.");
        }

        // [glulx #the-header] Execution begins by calling the start
        // function, and the decoding table may be zero for none, so both
        // have to be inside memory to be usable at all.
        if (StartFunction >= EndMem)
        {
            throw new InvalidDataException(
                $"The start function is at {StartFunction:X8}, beyond the end of memory at {EndMem:X8}.");
        }

        if (DecodingTable != 0 && DecodingTable >= EndMem)
        {
            throw new InvalidDataException(
                $"The string decoding table is at {DecodingTable:X8}, beyond the end of memory at {EndMem:X8}.");
        }

        // Memory is an array here, and an array cannot be longer than
        // two gigabytes. No real game comes within a hundredth of that,
        // so this is a limit of the interpreter and not a fault of the
        // file, and the message says so.
        if (EndMem > int.MaxValue)
        {
            throw new InvalidDataException(
                $"ENDMEM is {EndMem:X8}, more memory than this interpreter can allocate.");
        }

        ReadInformLayout(file);
    }

    /// <summary>
    /// [glulx #the-header] The version of the specification the file was
    /// generated to, packed as the header stores it: 00030103 for 3.1.3.
    /// </summary>
    public uint Version { get; }

    /// <summary>
    /// [glulx #the-header] The upper 16 bits of the version.
    /// </summary>
    public int MajorVersion => (int)(Version >> 16);

    /// <summary>
    /// [glulx #the-header] The next 8 bits of the version.
    /// </summary>
    public int MinorVersion => (int)((Version >> 8) & 0xFF);

    /// <summary>
    /// [glulx #the-header] The low 8 bits of the version.
    /// </summary>
    public int SubminorVersion => (int)(Version & 0xFF);

    /// <summary>
    /// The version in the usual dotted form, such as 3.1.3.
    /// </summary>
    public string VersionText => Describe(Version);

    /// <summary>
    /// [glulx #the-header] RAMSTART, the first address the program can
    /// write to. [glulx #the-memory-map] Everything below it is ROM.
    /// </summary>
    public uint RamStart { get; }

    /// <summary>
    /// [glulx #the-header] EXTSTART, the end of the game file's stored
    /// initial memory, and so the length of the game file.
    /// </summary>
    public uint ExtStart { get; }

    /// <summary>
    /// [glulx #the-header] ENDMEM, the end of the program's memory map.
    /// [glulx #the-memory-map] Memory between EXTSTART and here starts
    /// out as zeroes.
    /// </summary>
    public uint EndMem { get; }

    /// <summary>
    /// [glulx #the-header] The size of the stack the program needs, in
    /// bytes.
    /// </summary>
    public uint StackSize { get; }

    /// <summary>
    /// [glulx #the-header] The address of the function execution begins
    /// by calling.
    /// </summary>
    public uint StartFunction { get; }

    /// <summary>
    /// [glulx #the-header] The address of the string decoding table, or
    /// zero if the file has no compressed strings to decode. The game can
    /// change the table in use later with setstringtbl, so this is only
    /// the one it starts with.
    /// </summary>
    public uint DecodingTable { get; }

    /// <summary>
    /// [glulx #the-header] The checksum the file declares: a simple sum
    /// of the entire initial contents of memory as big-endian 32-bit
    /// integers, computed with this field as zero.
    /// </summary>
    public uint Checksum { get; }

    /// <summary>
    /// The version of the Inform compiler that produced the file, such as
    /// 6.43, or null if the file does not carry Inform's layout.
    /// </summary>
    /// <remarks>
    /// [glulx #the-header] The word after the header is not part of the
    /// specification, but the specification notes that Inform writes
    /// 'Info' there, followed by data of its own. Those bytes are a
    /// layout version, then the compiler version as four characters,
    /// four more characters of layout information, a two-byte release
    /// number, and a six-character serial number. The release and serial
    /// match the numbers in every corpus file's name, which is the
    /// evidence for the last two positions.
    /// </remarks>
    public string? InformVersion { get; private set; }

    /// <summary>
    /// The release number Inform wrote after the header, or null if the
    /// file does not carry Inform's layout.
    /// </summary>
    public int? InformRelease { get; private set; }

    /// <summary>
    /// The serial number Inform wrote after the header, six characters
    /// that are conventionally a date, or null if the file does not carry
    /// Inform's layout.
    /// </summary>
    public string? InformSerial { get; private set; }

    /// <summary>
    /// [glulx #the-header] Computes the checksum of
    /// <paramref name="file"/>: the sum of the initial contents of
    /// memory, which is the file up to EXTSTART, taken as big-endian
    /// 32-bit integers with the checksum field itself counted as zero.
    /// </summary>
    public uint ComputeChecksum(ReadOnlySpan<byte> file)
    {
        var initial = file[..(int)Math.Min(ExtStart, (uint)file.Length)];

        // EXTSTART is a multiple of 256, so the words come out even; a
        // stray tail that is not a whole word is left out rather than
        // read past the end.
        uint sum = 0;
        for (var address = 0; address + 4 <= initial.Length; address += 4)
        {
            sum += BinaryPrimitives.ReadUInt32BigEndian(initial.Slice(address, 4));
        }

        return sum - Checksum;
    }

    /// <summary>
    /// [glulx #the-header] Whether the checksum in the header is the
    /// checksum of <paramref name="file"/>.
    /// </summary>
    public bool VerifyChecksum(ReadOnlySpan<byte> file) => ComputeChecksum(file) == Checksum;

    private static uint Word(ReadOnlySpan<byte> file, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(file.Slice(offset, 4));

    private static string Describe(uint version) =>
        $"{version >> 16}.{(version >> 8) & 0xFF}.{version & 0xFF}";

    private static void CheckAligned(uint value, string name)
    {
        if (value % Alignment != 0)
        {
            throw new InvalidDataException(
                $"{name} is {value:X8}, which is not a multiple of {Alignment:X} as the memory map requires.");
        }
    }

    private void ReadInformLayout(ReadOnlySpan<byte> file)
    {
        if (file.Length < InformLayoutLength
            || !file.Slice(LayoutOffset, 4).SequenceEqual("Info"u8))
        {
            return;
        }

        InformVersion = Encoding.ASCII.GetString(file.Slice(InformVersionOffset, InformVersionLength));
        InformRelease = BinaryPrimitives.ReadUInt16BigEndian(file.Slice(InformReleaseOffset, 2));
        InformSerial = Encoding.ASCII.GetString(file.Slice(InformSerialOffset, InformSerialLength));
    }
}

using System.Buffers.Binary;
using System.Text;
using Rezrov.AaMachine.Instructions;

namespace Rezrov.AaMachine;

/// <summary>
/// [aam story] One chunk of a story file: what it is called, and where
/// its contents sit in the file.
/// </summary>
public readonly record struct AaChunk(string Name, int Offset, int Length);

/// <summary>
/// [aam story] A file the story carries inside itself: a picture, a
/// sound, or anything else a resource points at with the "file"
/// scheme.
/// </summary>
/// <param name="Name">The name the resource table refers to it by.</param>
/// <param name="Offset">Where its contents begin in the story file.</param>
/// <param name="Length">How many bytes of it there are.</param>
public readonly record struct AaFile(string Name, int Offset, int Length);

/// <summary>
/// [aam story] An Aa-machine story file, read as far as its header.
/// </summary>
/// <remarks>
/// The Aa-machine is what Dialog compiles to when it is not compiling to
/// the Z-Machine. Its story file is an IFF form of type AAVM, in which
/// HEAD comes first and the rest may come in any order: the bytecode,
/// the dictionary, the compressed text, the style sheet, the character
/// set, the word maps, the initial state, and whatever resources the
/// game carries.
///
/// The container, the header, and everything the machine needs in
/// order to read text are read here: the character set, the bitstream
/// decoding tree, the dictionary, and what the story says about
/// itself. That is enough to make a story speak, though not yet to
/// make it think.
/// </remarks>
public sealed class AaStory
{
    // [aam story] The chunks that go into the checksum, in the one order
    // the running total is taken in. The others are left out, which is
    // why a file can carry metadata and resources without disturbing it.
    private static readonly string[] Summed = ["LOOK", "LANG", "MAPS", "DICT", "INIT", "CODE", "WRIT"];

    private readonly byte[] _bytes;

    private AaStory(byte[] bytes, IReadOnlyList<AaChunk> chunks)
    {
        _bytes = bytes;
        Chunks = chunks;

        var head = Find("HEAD")
            ?? throw new InvalidDataException("The story file has no HEAD chunk.");

        if (head.Length < 22)
        {
            throw new InvalidDataException($"The HEAD chunk is {head.Length} bytes, too short to be a header.");
        }

        var at = bytes.AsSpan(head.Offset, head.Length);

        MajorVersion = at[0];
        MinorVersion = at[1];
        WordSize = at[2];
        Shift = at[3];
        Release = BinaryPrimitives.ReadUInt16BigEndian(at[4..]);
        Serial = Encoding.ASCII.GetString(at[6..12]);
        Checksum = BinaryPrimitives.ReadUInt32BigEndian(at[12..]);
        HeapSize = BinaryPrimitives.ReadUInt16BigEndian(at[16..]);
        AuxSize = BinaryPrimitives.ReadUInt16BigEndian(at[18..]);
        RamSize = BinaryPrimitives.ReadUInt16BigEndian(at[20..]);

        // [aam story] The identifier is optional, and when it is there it
        // is text ending at its first null.
        if (head.Length > 22)
        {
            var rest = at[22..];
            var end = rest.IndexOf((byte)0);
            Identifier = Encoding.ASCII.GetString(end < 0 ? rest : rest[..end]).Trim();
        }

        // The language comes first because everything else is written
        // in it: the dictionary, the compressed text, and the story's
        // own account of itself.
        Language = AaLanguage.Read(Required("LANG"), MajorVersion, MinorVersion);
        Dictionary = AaDictionaryTable.Read(Required("DICT"), Language.Characters);

        Text = new AaTextDecoder(
            Required("WRIT").ToArray(), Language, Dictionary, MajorVersion, MinorVersion);

        Instructions = new InstructionDecoder(
            Required("CODE").ToArray(), MajorVersion, MinorVersion, Shift);

        Files = ReadFiles(bytes, chunks);
        Styles = AaStyles.Read(Contents("LOOK"));
        WordMaps = AaWordMaps.Read(Contents("MAPS"));
        ObjectNames = AaObjectNames.Read(Contents("TAGS"), Language.Characters);
        Metadata = AaMetadata.Read(Contents("META"), Language.Characters);
        Resources = AaResource.Read(Contents("URLS"), Language.Characters, Shift);
    }

    /// <summary>
    /// [aam story] The file format version it was built for.
    /// </summary>
    public int MajorVersion { get; }

    public int MinorVersion { get; }

    /// <summary>
    /// [aam story] The size of a word in bytes, always two so far.
    /// </summary>
    public int WordSize { get; }

    /// <summary>
    /// [aam story] How far a short string pointer is shifted.
    /// </summary>
    public int Shift { get; }

    public int Release { get; }

    public string Serial { get; }

    /// <summary>
    /// [aam story] The checksum the file says it should have.
    /// </summary>
    public uint Checksum { get; }

    /// <summary>
    /// [aam story] Words of heap, for environments and choices.
    /// </summary>
    public int HeapSize { get; }

    /// <summary>
    /// [aam story] Words of auxiliary space, for the trail.
    /// </summary>
    public int AuxSize { get; }

    /// <summary>
    /// [aam story] Words of the area that can be written to.
    /// </summary>
    public int RamSize { get; }

    /// <summary>
    /// [aam story] The IFID, which a file need not carry.
    /// </summary>
    public string? Identifier { get; }

    /// <summary>Every chunk in the file, in the order it appears.</summary>
    public IReadOnlyList<AaChunk> Chunks { get; }

    /// <summary>
    /// [aam story] The character set, the decoding tree, and the rest
    /// of what the story says about its own language.
    /// </summary>
    public AaLanguage Language { get; }

    /// <summary>Every word the game knows.</summary>
    public AaDictionaryTable Dictionary { get; }

    /// <summary>Reads the compressed text.</summary>
    public AaTextDecoder Text { get; }

    /// <summary>Reads the bytecode.</summary>
    public InstructionDecoder Instructions { get; }

    /// <summary>The style sheet.</summary>
    public AaStyles Styles { get; }

    /// <summary>Which objects a word could be talking about.</summary>
    public AaWordMaps WordMaps { get; }

    /// <summary>What the author called each object.</summary>
    public AaObjectNames ObjectNames { get; }

    /// <summary>What the story says about itself.</summary>
    public AaMetadata Metadata { get; }

    /// <summary>
    /// [aam story] The pictures, sounds and links the game can reach
    /// for, which is empty unless the story carries a URLS chunk.
    /// </summary>
    public IReadOnlyList<AaResource> Resources { get; }

    /// <summary>
    /// [aam story] The files packaged inside the story, in the order
    /// they appear. A story may carry any number of them, or none.
    /// </summary>
    public IReadOnlyList<AaFile> Files { get; }

    /// <summary>
    /// [aam story] The bytes of a packaged file, or an empty span
    /// where the story carries no such file.
    /// </summary>
    public ReadOnlySpan<byte> File(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (var file in Files)
        {
            if (string.Equals(file.Name, name, StringComparison.Ordinal))
            {
                return _bytes.AsSpan(file.Offset, file.Length);
            }
        }

        return default;
    }

    /// <summary>
    /// [aam story] What a resource points at, where that is a file the
    /// story carries. A resource somewhere else on the web is not
    /// something an interpreter can go and fetch, so it comes back
    /// empty and the alternative text is what there is to show.
    /// </summary>
    public ReadOnlySpan<byte> Contents(AaResource resource)
    {
        const string Scheme = "file:";

        return resource.Url.StartsWith(Scheme, StringComparison.Ordinal)
            ? File(resource.Url[Scheme.Length..])
            : default;
    }

    /// <summary>
    /// The bytes of a chunk, or an empty span if it has none.
    /// </summary>
    public ReadOnlySpan<byte> Contents(string name) =>
        Find(name) is { } chunk ? _bytes.AsSpan(chunk.Offset, chunk.Length) : default;

    /// <summary>
    /// [aam story] Reads a story file, or says why it is not one.
    /// </summary>
    public static AaStory Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length < 12
            || !bytes.AsSpan(0, 4).SequenceEqual("FORM"u8)
            || !bytes.AsSpan(8, 4).SequenceEqual("AAVM"u8))
        {
            throw new InvalidDataException("This is not an Aa-machine story file.");
        }

        // [aam story] The form's own length counts everything after it,
        // which a truncated download will fail to live up to.
        var declared = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));
        if (declared + 8 > (uint)bytes.Length)
        {
            throw new InvalidDataException(
                $"The file says it holds {declared + 8} bytes but is {bytes.Length}.");
        }

        var chunks = new List<AaChunk>();
        var at = 12;

        while (at + 8 <= bytes.Length)
        {
            var name = Encoding.ASCII.GetString(bytes, at, 4);
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at + 4));

            if (length < 0 || at + 8 + length > bytes.Length)
            {
                throw new InvalidDataException($"The {name} chunk runs past the end of the file.");
            }

            chunks.Add(new AaChunk(name, at + 8, length));

            // [aam story] Chunks are padded to an even length, and the
            // padding is not counted in the length.
            at += 8 + length + (length & 1);
        }

        if (chunks.Count == 0 || chunks[0].Name != "HEAD")
        {
            throw new InvalidDataException("The first chunk of a story file must be HEAD.");
        }

        return new AaStory(bytes, chunks);
    }

    /// <summary>
    /// [aam story] Whether the file matches the checksum it carries: a
    /// running CRC-32 over seven of the chunks, taken in one fixed
    /// order rather than the order they appear in the file.
    /// </summary>
    public bool VerifyChecksum() => ComputeChecksum() == Checksum;

    /// <summary>The checksum the contents actually come to.</summary>
    public uint ComputeChecksum()
    {
        var running = 0xFFFFFFFFu;

        foreach (var name in Summed)
        {
            if (Find(name) is { } chunk)
            {
                running = Crc32(running, _bytes.AsSpan(chunk.Offset, chunk.Length));
            }
        }

        return running ^ 0xFFFFFFFFu;
    }

    // [aam story] Each FILE chunk is a name, a null, and then the
    // file itself. There may be several, which is why they are found
    // by walking the chunks rather than by asking for one by name.
    private static AaFile[] ReadFiles(byte[] bytes, IReadOnlyList<AaChunk> chunks)
    {
        var files = new List<AaFile>();

        foreach (var chunk in chunks)
        {
            if (chunk.Name != "FILE")
            {
                continue;
            }

            var end = bytes.AsSpan(chunk.Offset, chunk.Length).IndexOf((byte)0);

            if (end < 0)
            {
                throw new InvalidDataException("A FILE chunk does not say what it is called.");
            }

            files.Add(new AaFile(
                Encoding.ASCII.GetString(bytes, chunk.Offset, end),
                chunk.Offset + end + 1,
                chunk.Length - end - 1));
        }

        return [.. files];
    }

    private ReadOnlySpan<byte> Required(string name) =>
        Find(name) is { } chunk
            ? _bytes.AsSpan(chunk.Offset, chunk.Length)
            : throw new InvalidDataException($"The story file has no {name} chunk.");

    private AaChunk? Find(string name)
    {
        foreach (var chunk in Chunks)
        {
            if (chunk.Name == name)
            {
                return chunk;
            }
        }

        return null;
    }

    // The ordinary CRC-32, the one Ethernet and PNG and Blorb use, taken
    // a byte at a time with the bits reflected.
    private static uint Crc32(uint running, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            running ^= value;

            for (var bit = 0; bit < 8; bit++)
            {
                running = (running >> 1) ^ (0xEDB88320u & (uint)-(int)(running & 1));
            }
        }

        return running;
    }
}

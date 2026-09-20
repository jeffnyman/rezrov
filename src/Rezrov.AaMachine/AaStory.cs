using System.Buffers.Binary;
using System.Text;

namespace Rezrov.AaMachine;

/// <summary>
/// [aam story] One chunk of a story file: what it is called, and where
/// its contents sit in the file.
/// </summary>
public readonly record struct AaChunk(string Name, int Offset, int Length);

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
/// Only the container and the header are read here. That is enough to
/// say what a file is, which is where the other two machines started.
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

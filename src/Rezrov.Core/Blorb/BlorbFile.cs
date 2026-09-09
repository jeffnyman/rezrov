using System.Buffers.Binary;
using System.Text;

namespace Rezrov.Core.Blorb;

/// <summary>
/// A Blorb resource file: the pictures, sounds, data, and possibly the
/// game itself, packaged as one IFF file.
/// </summary>
/// <remarks>
/// [blorb 0] The file is an IFF FORM of type IFRS whose first chunk is
/// the resource index, followed by the resource chunks and any optional
/// chunks. This reads the whole thing up front, since a resource file
/// is a few hundred kilobytes at most and the interpreter wants random
/// access by resource number. Both virtual machines use Blorb, which is
/// why it lives in the core rather than beside either one.
/// </remarks>
public sealed class BlorbFile
{
    private readonly Dictionary<(ResourceUsage Usage, int Number), BlorbResource> _resources;
    private readonly List<BlorbChunk> _chunks;

    private BlorbFile(List<BlorbResource> resources, List<BlorbChunk> chunks)
    {
        Resources = resources;
        _resources = new Dictionary<(ResourceUsage, int), BlorbResource>();
        foreach (var resource in resources)
        {
            _resources.TryAdd((resource.Usage, resource.Number), resource);
        }

        _chunks = chunks;

        foreach (var chunk in chunks)
        {
            var data = chunk.Data.Span;
            switch (chunk.Id)
            {
                case "IFhd" when GameIdentifier is null:
                    // [blorb 6] For Z-code, the contents are those of the
                    // Quetzal IFhd chunk: release, serial, checksum, and a
                    // program counter that means nothing here.
                    if (data.Length >= 10)
                    {
                        GameIdentifier = new GameIdentifier(
                            BinaryPrimitives.ReadUInt16BigEndian(data),
                            Encoding.ASCII.GetString(data.Slice(2, 6)),
                            BinaryPrimitives.ReadUInt16BigEndian(data.Slice(8)));
                    }

                    break;
                case "RelN" when data.Length >= 2:
                    // [blorb 11.1]
                    ReleaseNumber = BinaryPrimitives.ReadUInt16BigEndian(data);
                    break;
                case "Fspc" when data.Length >= 4:
                    // [blorb 8]
                    Frontispiece = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
                    break;
                case "Loop":
                    // [blorb 11.4] Pairs of sound number and a flag: 1 to
                    // play once, 0 to repeat until stopped.
                    var loops = new Dictionary<int, bool>();
                    for (var at = 0; at + 8 <= data.Length; at += 8)
                    {
                        loops[(int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at))] =
                            BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at + 4)) == 0;
                    }

                    LoopingSounds = loops;
                    break;
                case "IFmd":
                    // [blorb 10] XML, in UTF-8.
                    Metadata = Encoding.UTF8.GetString(data);
                    break;
                case "AUTH":
                    Author = Encoding.ASCII.GetString(data);
                    break;
                case "(c) ":
                    Copyright = Encoding.ASCII.GetString(data);
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>[blorb 1] Every resource the index lists.</summary>
    public IReadOnlyList<BlorbResource> Resources { get; }

    /// <summary>
    /// Every chunk after the index, including the resources, in file
    /// order, for the chunks nothing here interprets yet.
    /// </summary>
    public IReadOnlyList<BlorbChunk> Chunks => _chunks;

    /// <summary>
    /// [blorb 5] The game itself, if the file carries one: resource 0
    /// of usage Exec, whose chunk type says which machine runs it.
    /// </summary>
    public BlorbResource? Executable => Find(ResourceUsage.Executable, 0);

    /// <summary>
    /// [blorb 6] Which game the resources belong to, if the file says.
    /// </summary>
    public GameIdentifier? GameIdentifier { get; }

    /// <summary>
    /// [blorb 11.1] The release number of the resource file, 0 if none
    /// is given.
    /// </summary>
    public ushort ReleaseNumber { get; }

    /// <summary>
    /// [blorb 8] The picture number of the cover art, if any.
    /// </summary>
    public int? Frontispiece { get; }

    /// <summary>
    /// [blorb 11.4] For a Version 3 game, which sounds repeat until
    /// stopped: true means forever, false means once, and an absent
    /// sound plays once.
    /// </summary>
    public IReadOnlyDictionary<int, bool> LoopingSounds { get; } = new Dictionary<int, bool>();

    /// <summary>[blorb 10] The metadata document, if any.</summary>
    public string? Metadata { get; }

    /// <summary>[blorb 12] The AUTH chunk's text, if any.</summary>
    public string? Author { get; }

    /// <summary>[blorb 12] The copyright chunk's text, if any.</summary>
    public string? Copyright { get; }

    /// <summary>
    /// Reads a Blorb file from its bytes.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The bytes are not an IFRS form, the index is missing or not
    /// first, or an index entry points outside the file.
    /// </exception>
    public static BlorbFile Read(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);

        // [blorb 15] FORM, a length, and IFRS.
        if (file.Length < 12 || !file.AsSpan(0, 4).SequenceEqual("FORM"u8) || !file.AsSpan(8, 4).SequenceEqual("IFRS"u8))
        {
            throw new InvalidDataException("This is not a Blorb resource file.");
        }

        var end = (int)Math.Min((long)file.Length, 8 + BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(4)));
        var chunks = new List<BlorbChunk>();
        var at = 12;

        while (at + 8 <= end)
        {
            var id = Encoding.ASCII.GetString(file, at, 4);
            var length = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at + 4));
            if (length > (uint)(end - at - 8))
            {
                throw new InvalidDataException($"The {id} chunk runs past the end of the file.");
            }

            chunks.Add(new BlorbChunk(id, at, new ReadOnlyMemory<byte>(file, at + 8, (int)length)));

            // [blorb 15] An odd length is followed by a pad byte.
            at += 8 + (int)length + (int)(length & 1);
        }

        // [blorb 0] The first chunk must be the resource index.
        if (chunks.Count == 0 || chunks[0].Id != "RIdx")
        {
            throw new InvalidDataException("A Blorb file must begin with a resource index.");
        }

        var resources = ReadIndex(file, chunks);
        return new BlorbFile(resources, chunks.Skip(1).ToList());
    }

    /// <summary>
    /// The resource of a usage and number, or null if there is none.
    /// </summary>
    public BlorbResource? Find(ResourceUsage usage, int number) =>
        _resources.TryGetValue((usage, number), out var resource) ? resource : null;

    // [blorb 1] A count, then twelve bytes per resource: usage, number,
    // and the offset of its chunk from the start of the file.
    private static List<BlorbResource> ReadIndex(byte[] file, List<BlorbChunk> chunks)
    {
        var index = chunks[0].Data.Span;
        if (index.Length < 4)
        {
            throw new InvalidDataException("The resource index is too short.");
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(index);
        if (count > (uint)((index.Length - 4) / 12))
        {
            throw new InvalidDataException("The resource index claims more entries than it holds.");
        }

        var byOffset = chunks.ToDictionary(c => c.Offset, c => c);
        var resources = new List<BlorbResource>((int)count);

        for (var i = 0; i < count; i++)
        {
            var entry = index.Slice(4 + (i * 12));
            var usage = Encoding.ASCII.GetString(entry[..4]);
            var number = (int)BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(4));
            var start = (int)BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(8));

            if (!byOffset.TryGetValue(start, out var chunk))
            {
                throw new InvalidDataException($"The index points {usage.Trim()} resource {number} at offset {start}, where no chunk begins.");
            }

            // [blorb 16] An AIFF sound is itself an IFF form, and the
            // whole form, header included, is the sound file; every
            // other kind of resource is the chunk's contents alone.
            var type = chunk.Id;
            var data = chunk.Data;
            if (type == "FORM")
            {
                type = chunk.Data.Length >= 4 ? Encoding.ASCII.GetString(chunk.Data.Span[..4]) : "FORM";
                data = new ReadOnlyMemory<byte>(file, chunk.Offset, chunk.Data.Length + 8);
            }

            resources.Add(new BlorbResource(ParseUsage(usage), number, type, data));
        }

        return resources;
    }

    private static ResourceUsage ParseUsage(string usage) => usage switch
    {
        "Pict" => ResourceUsage.Picture,
        "Snd " => ResourceUsage.Sound,
        "Data" => ResourceUsage.Data,
        "Exec" => ResourceUsage.Executable,
        _ => ResourceUsage.Unknown,
    };
}

/// <summary>[blorb 1] What a resource is for.</summary>
public enum ResourceUsage
{
    Unknown,

    Picture,

    Sound,

    Data,

    Executable,
}

/// <summary>
/// One resource: what it is for, its number, the chunk type that says
/// its format, and its bytes.
/// </summary>
/// <param name="Usage">
/// [blorb 1] Picture, sound, data, or executable.
/// </param>
/// <param name="Number">
/// [blorb 1] The number the game refers to it by. An executable is 0.
/// </param>
/// <param name="ChunkType">
/// The format: PNG or JPEG for pictures, AIFF, OGGV, MOD, or SONG for
/// sounds, TEXT or BINA for data, ZCOD or GLUL for executables.
/// </param>
/// <param name="Data">
/// The resource's bytes: a complete file for an AIFF, the chunk's
/// contents for everything else.
/// </param>
public sealed record BlorbResource(ResourceUsage Usage, int Number, string ChunkType, ReadOnlyMemory<byte> Data)
{
    /// <summary>
    /// [blorb 3] and [blorb 14.3] Whether this sound is music, which
    /// plays in its own channel, rather than a sample: MOD and SONG are
    /// music, AIFF and OGGV are samples.
    /// </summary>
    public bool IsMusic => ChunkType is "MOD " or "SONG";
}

/// <summary>Any chunk of the file, by id, offset, and contents.</summary>
public sealed record BlorbChunk(string Id, int Offset, ReadOnlyMemory<byte> Data);

/// <summary>
/// [blorb 6] Which Z-code game a resource file belongs to: the release,
/// serial, and checksum from the story's header.
/// </summary>
public sealed record GameIdentifier(ushort Release, string Serial, ushort Checksum);

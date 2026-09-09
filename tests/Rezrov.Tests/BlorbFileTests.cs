using System.Buffers.Binary;
using System.Text;
using Rezrov.Core.Blorb;

namespace Rezrov.Tests;

/// <summary>
/// The Blorb reader on files built by hand: the index, the resources
/// and their formats, the optional chunks, and the ways a file can be
/// wrong.
/// </summary>
public class BlorbFileTests
{
    [Fact]
    public void ReadsResourcesOfEveryUsageAndFormat()
    {
        var file = Build(
            [("Exec", 0, "ZCOD", [5, 0, 0, 1]), ("Pict", 1, "PNG ", [1, 2, 3]), ("Snd ", 3, "AIFF", [9, 9]), ("Snd ", 4, "OGGV", [7]), ("Snd ", 5, "MOD ", [6, 6])],
            []);

        var blorb = BlorbFile.Read(file);

        // [blorb 1] Five entries; [blorb 5] the executable is number 0.
        Assert.Equal(5, blorb.Resources.Count);
        Assert.Equal("ZCOD", blorb.Executable!.ChunkType);
        Assert.Equal(new byte[] { 5, 0, 0, 1 }, blorb.Executable.Data.ToArray());
        Assert.Equal("PNG ", blorb.Find(ResourceUsage.Picture, 1)!.ChunkType);

        // [blorb 16] An AIFF resource is the whole form, header included.
        var aiff = blorb.Find(ResourceUsage.Sound, 3)!;
        Assert.Equal("AIFF", aiff.ChunkType);
        Assert.Equal("FORM", Encoding.ASCII.GetString(aiff.Data.Span[..4]));
        Assert.Equal("AIFF", Encoding.ASCII.GetString(aiff.Data.Span[8..12]));
        Assert.False(aiff.IsMusic);

        // [blorb 14.3] MOD is music; Ogg is a sample.
        Assert.True(blorb.Find(ResourceUsage.Sound, 5)!.IsMusic);
        Assert.False(blorb.Find(ResourceUsage.Sound, 4)!.IsMusic);
        Assert.Null(blorb.Find(ResourceUsage.Sound, 6));
    }

    [Fact]
    public void ReadsTheOptionalChunks()
    {
        var ifhd = new byte[13];
        BinaryPrimitives.WriteUInt16BigEndian(ifhd, 88);
        Encoding.ASCII.GetBytes("840726").CopyTo(ifhd, 2);
        BinaryPrimitives.WriteUInt16BigEndian(ifhd.AsSpan(8), 0xA129);

        var file = Build(
            [("Snd ", 3, "AIFF", [1]), ("Snd ", 4, "AIFF", [2])],
            [
                ("IFhd", ifhd),
                ("RelN", [0, 7]),
                ("Fspc", [0, 0, 0, 3]),
                ("Loop", [0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 4, 0, 0, 0, 1]),
                ("IFmd", "<ifindex/>"u8.ToArray()),
                ("AUTH", "Infocom"u8.ToArray()),
                ("(c) ", "1987"u8.ToArray()),
                ("ANNO", "odd"u8.ToArray()),
            ]);

        var blorb = BlorbFile.Read(file);

        // [blorb 6], [blorb 11.1], [blorb 8], [blorb 11.4], [blorb 10],
        // [blorb 12], and [blorb 15] an odd chunk padded.
        Assert.Equal(new GameIdentifier(88, "840726", 0xA129), blorb.GameIdentifier);
        Assert.Equal(7, blorb.ReleaseNumber);
        Assert.Equal(3, blorb.Frontispiece);
        Assert.True(blorb.LoopingSounds[3]);
        Assert.False(blorb.LoopingSounds[4]);
        Assert.Equal("<ifindex/>", blorb.Metadata);
        Assert.Equal("Infocom", blorb.Author);
        Assert.Equal("1987", blorb.Copyright);
        Assert.Contains(blorb.Chunks, c => c.Id == "ANNO");
        Assert.Equal(2, blorb.Resources.Count);
    }

    [Fact]
    public void AFileWithoutTheOptionalChunksHasDefaults()
    {
        var blorb = BlorbFile.Read(Build([("Snd ", 3, "OGGV", [1])], []));

        Assert.Null(blorb.GameIdentifier);
        Assert.Equal(0, blorb.ReleaseNumber);
        Assert.Null(blorb.Frontispiece);
        Assert.Empty(blorb.LoopingSounds);
        Assert.Null(blorb.Executable);
    }

    [Fact]
    public void RejectsWhatIsNotABlorbFile()
    {
        Assert.Throws<InvalidDataException>(() => BlorbFile.Read("FORM....IFZS"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => BlorbFile.Read(new byte[5]));

        // [blorb 0] The index must come first.
        var noIndex = Build([], [("ANNO", "x"u8.ToArray())], indexFirst: false);
        Assert.Throws<InvalidDataException>(() => BlorbFile.Read(noIndex));
    }

    [Fact]
    public void RejectsAnIndexEntryThatPointsNowhere()
    {
        var file = Build([("Snd ", 3, "OGGV", [1])], []);

        // Point the one entry into the middle of its chunk.
        var start = 12 + 8 + 4 + 8;
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(start), BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(start)) + 2);

        Assert.Throws<InvalidDataException>(() => BlorbFile.Read(file));
    }

    /// <summary>
    /// A resource file that names a game, for the interpreter's check.
    /// </summary>
    internal static BlorbFile BuildForTest(ushort release, string serial, ushort checksum)
    {
        var ifhd = new byte[13];
        BinaryPrimitives.WriteUInt16BigEndian(ifhd, release);
        Encoding.ASCII.GetBytes(serial).CopyTo(ifhd, 2);
        BinaryPrimitives.WriteUInt16BigEndian(ifhd.AsSpan(8), checksum);
        return BlorbFile.Read(Build([("Snd ", 3, "AIFF", [1])], [("IFhd", ifhd)]));
    }

    /// <summary>
    /// Builds a Blorb file: the index, then one chunk per resource in
    /// order, then the extra chunks. An AIFF resource is wrapped in a
    /// FORM as [blorb 3.1] requires.
    /// </summary>
    private static byte[] Build(
        (string Usage, int Number, string Type, byte[] Data)[] resources,
        (string Id, byte[] Data)[] extras,
        bool indexFirst = true)
    {
        var chunks = new List<byte[]>();
        var offsets = new List<int>();

        // Offsets start after FORM, length, IFRS, and the index chunk.
        var indexLength = 4 + (resources.Length * 12);
        var at = 12 + 8 + indexLength + (indexLength & 1);

        foreach (var (_, _, type, data) in resources)
        {
            byte[] chunk;
            if (type == "AIFF")
            {
                var body = "AIFF"u8.ToArray().Concat(data).ToArray();
                chunk = Chunk("FORM", body);
            }
            else
            {
                chunk = Chunk(type, data);
            }

            offsets.Add(at);
            chunks.Add(chunk);
            at += chunk.Length;
        }

        foreach (var (id, data) in extras)
        {
            chunks.Add(Chunk(id, data));
        }

        var index = new MemoryStream();
        Write32(index, (uint)resources.Length);
        for (var i = 0; i < resources.Length; i++)
        {
            index.Write(Encoding.ASCII.GetBytes(resources[i].Usage));
            Write32(index, (uint)resources[i].Number);
            Write32(index, (uint)offsets[i]);
        }

        var body2 = new MemoryStream();
        body2.Write("IFRS"u8);
        if (indexFirst)
        {
            body2.Write(Chunk("RIdx", index.ToArray()));
        }

        foreach (var chunk in chunks)
        {
            body2.Write(chunk);
        }

        if (!indexFirst)
        {
            body2.Write(Chunk("RIdx", index.ToArray()));
        }

        var file = new MemoryStream();
        file.Write("FORM"u8);
        Write32(file, (uint)body2.Length);
        body2.Position = 0;
        body2.CopyTo(file);
        return file.ToArray();
    }

    private static byte[] Chunk(string id, byte[] data)
    {
        var chunk = new MemoryStream();
        chunk.Write(Encoding.ASCII.GetBytes(id));
        Write32(chunk, (uint)data.Length);
        chunk.Write(data);
        if ((data.Length & 1) != 0)
        {
            chunk.WriteByte(0);
        }

        return chunk.ToArray();
    }

    private static void Write32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }
}

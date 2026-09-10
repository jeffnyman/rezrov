using System.Text;

namespace Rezrov.Tests;

/// <summary>
/// Builds small Blorb files in memory, with pictures of any size and
/// format, for tests that need a resource file without shipping one.
/// </summary>
internal static class TestBlorb
{
    /// <summary>
    /// A Blorb with the given pictures, and optionally a resolution
    /// chunk and a release number.
    /// </summary>
    public static byte[] Build(IReadOnlyList<(int Number, string Type, byte[] Data)> pictures, byte[]? resolution = null, int release = 0)
    {
        var others = new List<(string Id, byte[] Data)>();
        if (release != 0)
        {
            others.Add(("RelN", [(byte)(release >> 8), (byte)release]));
        }

        if (resolution is not null)
        {
            others.Add(("Reso", resolution));
        }

        // [blorb 1] The form header, then the resource index, then the
        // other chunks, then the pictures, each entry in the index
        // pointing at its chunk's header.
        var indexLength = 4 + (12 * pictures.Count);
        var cursor = 12 + 8 + indexLength;
        foreach (var (_, data) in others)
        {
            cursor += 8 + data.Length + (data.Length & 1);
        }

        var starts = new Dictionary<int, int>();
        foreach (var (number, _, data) in pictures)
        {
            starts[number] = cursor;
            cursor += 8 + data.Length + (data.Length & 1);
        }

        var output = new List<byte>();
        output.AddRange("FORM"u8.ToArray());
        Word(output, cursor - 8);
        output.AddRange("IFRS"u8.ToArray());

        var index = new List<byte>();
        Word(index, pictures.Count);
        foreach (var (number, _, _) in pictures)
        {
            index.AddRange("Pict"u8.ToArray());
            Word(index, number);
            Word(index, starts[number]);
        }

        Chunk(output, "RIdx", index.ToArray());
        foreach (var (id, data) in others)
        {
            Chunk(output, id, data);
        }

        foreach (var (_, type, data) in pictures)
        {
            Chunk(output, type, data);
        }

        return output.ToArray();
    }

    /// <summary>The start of a PNG file: the signature and an IHDR chunk.</summary>
    public static byte[] Png(int width, int height)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Word(bytes, 13);
        bytes.AddRange("IHDR"u8.ToArray());
        Word(bytes, width);
        Word(bytes, height);
        bytes.AddRange([8, 3, 0, 0, 0]);
        Word(bytes, 0);
        return bytes.ToArray();
    }

    /// <summary>A JPEG file reduced to its frame header.</summary>
    public static byte[] Jpeg(int width, int height) =>
    [
        0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x0B, 0x08,
        (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width,
        0x01, 0x01, 0x11, 0x00, 0xFF, 0xD9,
    ];

    /// <summary>[blorb 2.3] A placeholder rectangle.</summary>
    public static byte[] Rect(int width, int height)
    {
        var bytes = new List<byte>();
        Word(bytes, width);
        Word(bytes, height);
        return bytes.ToArray();
    }

    /// <summary>
    /// [blorb 11.2] A resolution chunk: the standard window size, no
    /// limits, and an entry for each picture that scales.
    /// </summary>
    public static byte[] Resolution(int width, int height, params (int Number, int StandardNumerator, int StandardDenominator, int MinimumNumerator, int MinimumDenominator, int MaximumNumerator, int MaximumDenominator)[] entries)
    {
        var bytes = new List<byte>();
        Word(bytes, width);
        Word(bytes, height);
        for (var i = 0; i < 4; i++)
        {
            Word(bytes, 0);
        }

        foreach (var entry in entries)
        {
            Word(bytes, entry.Number);
            Word(bytes, entry.StandardNumerator);
            Word(bytes, entry.StandardDenominator);
            Word(bytes, entry.MinimumNumerator);
            Word(bytes, entry.MinimumDenominator);
            Word(bytes, entry.MaximumNumerator);
            Word(bytes, entry.MaximumDenominator);
        }

        return bytes.ToArray();
    }

    private static void Word(List<byte> bytes, int value) =>
        bytes.AddRange([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);

    private static void Chunk(List<byte> output, string id, byte[] data)
    {
        output.AddRange(Encoding.ASCII.GetBytes(id));
        Word(output, data.Length);
        output.AddRange(data);
        if ((data.Length & 1) == 1)
        {
            output.Add(0);
        }
    }
}

using System.Buffers.Binary;
using System.IO.Compression;

namespace Rezrov.Tests;

/// <summary>
/// Builds small PNG files in memory, for tests that want a picture
/// rather than a picture format.
/// </summary>
/// <remarks>
/// [png 4.1.1] Eight bit red, green, blue, and alpha, one row after
/// another with no filtering, which is the plainest thing a PNG can be
/// and the one a reader has least to do with.
/// <see cref="PngReaderTests"/> builds its own, since what it is testing
/// is everything this leaves out.
/// </remarks>
internal static class TestPng
{
    /// <summary>A picture of one color all over.</summary>
    public static byte[] Solid(int width, int height, byte red, byte green, byte blue, byte alpha = 255)
    {
        var rows = new byte[height][];
        for (var y = 0; y < height; y++)
        {
            rows[y] = new byte[width * 4];
            for (var x = 0; x < width; x++)
            {
                rows[y][(x * 4) + 0] = red;
                rows[y][(x * 4) + 1] = green;
                rows[y][(x * 4) + 2] = blue;
                rows[y][(x * 4) + 3] = alpha;
            }
        }

        return Build(width, height, rows);
    }

    /// <summary>
    /// A picture whose pixels are whatever the caller says, each row
    /// four bytes to a pixel.
    /// </summary>
    public static byte[] Build(int width, int height, byte[][] rows)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8;
        header[9] = 6;

        var raw = new List<byte>();
        foreach (var row in rows)
        {
            raw.Add(0);
            raw.AddRange(row);
        }

        var squeezed = new MemoryStream();
        using (var deflating = new ZLibStream(squeezed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflating.Write(raw.ToArray());
        }

        var file = new List<byte> { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };
        Chunk(file, "IHDR", header);
        Chunk(file, "IDAT", squeezed.ToArray());
        Chunk(file, "IEND", []);
        return [.. file];
    }

    private static void Chunk(List<byte> file, string name, byte[] body)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
        file.AddRange(length);
        file.AddRange(System.Text.Encoding.ASCII.GetBytes(name));
        file.AddRange(body);

        // The reader does not check them, so they are left at zero.
        file.AddRange([0, 0, 0, 0]);
    }
}

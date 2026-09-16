using System.Buffers.Binary;
using System.IO.Compression;
using Rezrov.Core.Blorb;
using Rezrov.Core.Graphics;

namespace Rezrov.Tests;

/// <summary>
/// [blorb 2.1] Reading the pictures of a resource file: the kinds of
/// pixel a PNG can hold, the filters each row is written with, the
/// seven passes of an interlaced picture, and transparency.
/// </summary>
public class PngReaderTests
{
    [Fact]
    public void AGrayPictureIsReadAtAnyWidthOfSample()
    {
        // Two pixels of one bit: black then white, spread over the
        // whole range so that the largest value a sample can hold is
        // white whatever its width.
        var oneBit = PngReader.Read(Png(2, 1, depth: 1, color: 0, rows: [[0b01000000]]))!;
        Assert.Equal((0, 0, 0, 255), oneBit.At(0, 0));
        Assert.Equal((255, 255, 255, 255), oneBit.At(1, 0));

        var fourBit = PngReader.Read(Png(2, 1, depth: 4, color: 0, rows: [[0x0F]]))!;
        Assert.Equal((0, 0, 0, 255), fourBit.At(0, 0));
        Assert.Equal((255, 255, 255, 255), fourBit.At(1, 0));

        var eightBit = PngReader.Read(Png(2, 1, depth: 8, color: 0, rows: [[0x20, 0x80]]))!;
        Assert.Equal((0x20, 0x20, 0x20, 255), eightBit.At(0, 0));

        // A sixteen bit sample is kept to its high byte.
        var sixteenBit = PngReader.Read(Png(1, 1, depth: 16, color: 0, rows: [[0x12, 0x34]]))!;
        Assert.Equal((0x12, 0x12, 0x12, 255), sixteenBit.At(0, 0));
    }

    [Fact]
    public void APaletteIsLookedUpAndItsAlphasApplied()
    {
        // [png 11.2.3] Three colors, of which the first is see-through
        // and the third has no alpha given, so it is opaque.
        byte[] palette = [10, 20, 30, 40, 50, 60, 70, 80, 90];
        byte[] alphas = [0, 128];
        var picture = PngReader.Read(Png(3, 1, depth: 2, color: 3, rows: [[0b00_01_10_00]], palette: palette, transparency: alphas))!;

        Assert.Equal((10, 20, 30, 0), picture.At(0, 0));
        Assert.Equal((40, 50, 60, 128), picture.At(1, 0));
        Assert.Equal((70, 80, 90, 255), picture.At(2, 0));
    }

    [Fact]
    public void TrueColorAndAlphaComeThroughAsTheyAre()
    {
        var colors = PngReader.Read(Png(2, 1, depth: 8, color: 2, rows: [[1, 2, 3, 4, 5, 6]]))!;
        Assert.Equal((1, 2, 3, 255), colors.At(0, 0));
        Assert.Equal((4, 5, 6, 255), colors.At(1, 0));

        var withAlpha = PngReader.Read(Png(1, 1, depth: 8, color: 6, rows: [[9, 8, 7, 64]]))!;
        Assert.Equal((9, 8, 7, 64), withAlpha.At(0, 0));

        // Gray with alpha of its own.
        var gray = PngReader.Read(Png(1, 1, depth: 8, color: 4, rows: [[200, 32]]))!;
        Assert.Equal((200, 200, 200, 32), gray.At(0, 0));
    }

    [Fact]
    public void TheOneColorAPictureCallsSeeThroughIsTransparent()
    {
        // [png 11.3.2] A true color picture may name one color as
        // transparent rather than carrying an alpha for every pixel.
        byte[] transparency = [0, 1, 0, 2, 0, 3];
        var picture = PngReader.Read(Png(2, 1, depth: 8, color: 2, rows: [[1, 2, 3, 4, 5, 6]], transparency: transparency))!;

        Assert.Equal((1, 2, 3, 0), picture.At(0, 0));
        Assert.Equal((4, 5, 6, 255), picture.At(1, 0));
    }

    [Fact]
    public void EveryFilterIsUndone()
    {
        // Each row of this picture is written with a different filter,
        // and every one of them must come back as what it started as.
        var pixels = new byte[5][];
        for (var y = 0; y < 5; y++)
        {
            pixels[y] = new byte[4 * 3];
            for (var i = 0; i < pixels[y].Length; i++)
            {
                pixels[y][i] = (byte)((y * 40) + (i * 7) + 3);
            }
        }

        var filtered = new byte[5][];
        for (var y = 0; y < 5; y++)
        {
            filtered[y] = Filter((byte)y, pixels[y], y == 0 ? new byte[pixels[0].Length] : pixels[y - 1], 3);
        }

        var picture = PngReader.Read(Png(4, 5, depth: 8, color: 2, rows: filtered, filters: [0, 1, 2, 3, 4]))!;

        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var at = x * 3;
                Assert.Equal((pixels[y][at], pixels[y][at + 1], pixels[y][at + 2], (byte)255), picture.At(x, y));
            }
        }
    }

    [Fact]
    public void AnInterlacedPictureIsPutBackTogether()
    {
        // [png 4.1.1] Eight rows of eight is the smallest picture that
        // has something in all seven passes, and every pixel of it is
        // its own value, so a pixel put in the wrong place shows.
        const int Size = 8;
        var rows = new List<byte[]>();
        int[] columnStart = [0, 4, 0, 2, 0, 1, 0];
        int[] rowStart = [0, 0, 4, 0, 2, 0, 1];
        int[] columnStep = [8, 8, 4, 4, 2, 2, 1];
        int[] rowStep = [8, 8, 8, 4, 4, 2, 2];

        for (var pass = 0; pass < 7; pass++)
        {
            for (var y = rowStart[pass]; y < Size; y += rowStep[pass])
            {
                var row = new List<byte>();
                for (var x = columnStart[pass]; x < Size; x += columnStep[pass])
                {
                    row.Add((byte)((y * Size) + x));
                }

                if (row.Count > 0)
                {
                    rows.Add(row.ToArray());
                }
            }
        }

        var picture = PngReader.Read(Png(Size, Size, depth: 8, color: 0, rows: [.. rows], interlaced: true))!;

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var expected = (byte)((y * Size) + x);
                Assert.Equal((expected, expected, expected, (byte)255), picture.At(x, y));
            }
        }
    }

    [Fact]
    public void WhatIsNotAPictureIsRefused()
    {
        Assert.Null(PngReader.Read([]));
        Assert.Null(PngReader.Read("not a picture at all"u8));

        // A header saying something no picture says, and a picture with
        // a palette missing.
        Assert.Null(PngReader.Read(Png(1, 1, depth: 3, color: 0, rows: [[0]])));
        Assert.Null(PngReader.Read(Png(1, 1, depth: 8, color: 3, rows: [[0]])));

        // A point outside a picture is transparent rather than a fault.
        var picture = PngReader.Read(Png(1, 1, depth: 8, color: 0, rows: [[7]]))!;
        Assert.Equal((0, 0, 0, 0), picture.At(5, 5));
    }

    [Fact]
    public void EveryPictureInTheCorpusDecodes()
    {
        var files = Corpus.BlorbFiles();
        Assert.SkipUnless(files.Count > 0, "The entharion submodule is not populated.");

        var decoded = 0;
        foreach (var path in files)
        {
            var blorb = BlorbFile.Read(File.ReadAllBytes(path));
            foreach (var picture in BlorbPictures.From(blorb).Pictures.Where(p => p.Kind == PictureKind.Png))
            {
                var pixels = PngReader.Read(picture.Data.Span);

                // The catalog reads the size out of the header; the
                // decoder must produce exactly that many pixels.
                Assert.NotNull(pixels);
                Assert.Equal((picture.Width, picture.Height), (pixels.Width, pixels.Height));
                Assert.Equal(picture.Width * picture.Height * 4, pixels.Rgba.Length);
                decoded++;
            }
        }

        Assert.True(decoded > 2000, $"Only {decoded} pictures were decoded.");
    }

    /// <summary>
    /// A PNG file of the given shape, whose rows are the sample bytes
    /// as they should arrive after unfiltering, unless filters says
    /// otherwise. The chunk checksums are left at zero, which the
    /// reader does not look at.
    /// </summary>
    private static byte[] Png(int width, int height, int depth, int color, byte[][] rows, byte[]? palette = null, byte[]? transparency = null, bool interlaced = false, byte[]? filters = null)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = (byte)depth;
        header[9] = (byte)color;
        header[12] = interlaced ? (byte)1 : (byte)0;

        var raw = new List<byte>();
        for (var y = 0; y < rows.Length; y++)
        {
            raw.Add(filters is null ? (byte)0 : filters[y]);
            raw.AddRange(rows[y]);
        }

        var squeezed = new MemoryStream();
        using (var deflating = new ZLibStream(squeezed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflating.Write(raw.ToArray());
        }

        var file = new List<byte> { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };
        Chunk(file, "IHDR", header);
        if (palette is not null)
        {
            Chunk(file, "PLTE", palette);
        }

        if (transparency is not null)
        {
            Chunk(file, "tRNS", transparency);
        }

        Chunk(file, "IDAT", squeezed.ToArray());
        Chunk(file, "IEND", []);
        return file.ToArray();
    }

    private static void Chunk(List<byte> file, string name, byte[] body)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
        file.AddRange(length);
        file.AddRange(System.Text.Encoding.ASCII.GetBytes(name));
        file.AddRange(body);
        file.AddRange([0, 0, 0, 0]);
    }

    /// <summary>
    /// [png 9.2] Writes a row the way a filter would, which is what the
    /// reader has to undo.
    /// </summary>
    private static byte[] Filter(byte filter, byte[] row, byte[] previous, int pixelBytes)
    {
        var written = new byte[row.Length];
        for (var i = 0; i < row.Length; i++)
        {
            var left = i >= pixelBytes ? row[i - pixelBytes] : 0;
            var above = previous[i];
            var corner = i >= pixelBytes ? previous[i - pixelBytes] : 0;

            written[i] = filter switch
            {
                1 => (byte)(row[i] - left),
                2 => (byte)(row[i] - above),
                3 => (byte)(row[i] - ((left + above) / 2)),
                4 => (byte)(row[i] - Paeth(left, above, corner)),
                _ => row[i],
            };
        }

        return written;
    }

    private static int Paeth(int left, int above, int corner)
    {
        var estimate = left + above - corner;
        var fromLeft = Math.Abs(estimate - left);
        var fromAbove = Math.Abs(estimate - above);
        var fromCorner = Math.Abs(estimate - corner);

        if (fromLeft <= fromAbove && fromLeft <= fromCorner)
        {
            return left;
        }

        return fromAbove <= fromCorner ? above : corner;
    }
}

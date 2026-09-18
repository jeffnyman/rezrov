using System.Buffers.Binary;
using System.IO.Compression;

namespace Rezrov.Core.Graphics;

/// <summary>
/// Reads a PNG file into pixels a frontend can draw.
/// </summary>
/// <remarks>
/// [blorb 2.1] PNG is the picture format of a resource file, and the
/// games in the corpus use nearly all of it: palettes of one, two,
/// four, and eight bits, grays, true colors, both kinds of alpha, and
/// transparency given as a palette of alphas or as the one color that
/// is see-through. All of that is here, interlacing included.
///
/// The compressed data is zlib, which the runtime can inflate, so what
/// is left is the part PNG does itself: undoing the filter each row was
/// written with, unpacking samples narrower than a byte, and turning
/// whatever the file says a pixel is into red, green, blue, and alpha.
/// Sixteen bit samples are kept to their high byte, which is what a
/// screen can show.
///
/// The checksum on each chunk is not verified. A game's own resources
/// are not corrupt, and a picture that is slightly wrong is better
/// shown than refused, which is what the reference decoder does with a
/// warning it has nowhere to put here.
/// </remarks>
public static class PngReader
{
    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    // [png 4.1.1] The starting column and row of each of the seven
    // passes of an interlaced picture, and how far apart their pixels
    // are.
    private static ReadOnlySpan<int> PassColumn => [0, 4, 0, 2, 0, 1, 0];

    private static ReadOnlySpan<int> PassRow => [0, 0, 4, 0, 2, 0, 1];

    private static ReadOnlySpan<int> PassColumnStep => [8, 8, 4, 4, 2, 2, 1];

    private static ReadOnlySpan<int> PassRowStep => [8, 8, 8, 4, 4, 2, 2];

    /// <summary>
    /// Decodes a PNG, or returns null if the bytes are not one, or are
    /// one this cannot decode.
    /// </summary>
    public static Pixels? Read(ReadOnlySpan<byte> file) => Read(file, default);

    /// <summary>
    /// Decodes a PNG with a palette of the caller's choosing in place
    /// of the one the file carries.
    /// </summary>
    /// <remarks>
    /// [blorb 11.3] A resource file may say that a picture takes its
    /// colors from whatever was plotted before it rather than from the
    /// palette it carries, which is how two of the Infocom Version 6
    /// games shade the same artwork differently as the game goes on.
    /// Only an indexed picture has a palette to replace; every other
    /// kind ignores it.
    /// </remarks>
    public static Pixels? Read(ReadOnlySpan<byte> file, ReadOnlySpan<byte> replacement)
    {
        if (file.Length < 8 || !file[..8].SequenceEqual(Signature))
        {
            return null;
        }

        var header = default(Header);
        byte[]? palette = null;
        byte[]? alphas = null;
        var transparent = (-1, -1, -1);
        var data = new MemoryStream();
        var seenHeader = false;

        for (var at = 8; at + 8 <= file.Length;)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(file[at..]);
            if (length > int.MaxValue || at + 12 + (int)length > file.Length)
            {
                break;
            }

            var name = file.Slice(at + 4, 4);
            var body = file.Slice(at + 8, (int)length);

            if (name.SequenceEqual("IHDR"u8))
            {
                if (body.Length < 13 || !Header.TryRead(body, out header))
                {
                    return null;
                }

                seenHeader = true;
            }
            else if (name.SequenceEqual("PLTE"u8))
            {
                palette = body.ToArray();
            }
            else if (name.SequenceEqual("tRNS"u8))
            {
                // [png 11.3.2] For a palette, an alpha for each entry;
                // for a gray or a true color, the one value that is
                // see-through.
                switch (header.ColorType)
                {
                    case 0 when body.Length >= 2:
                        var gray = Scale(BinaryPrimitives.ReadUInt16BigEndian(body), header.BitDepth);
                        transparent = (gray, gray, gray);
                        break;
                    case 2 when body.Length >= 6:
                        transparent = (
                            Scale(BinaryPrimitives.ReadUInt16BigEndian(body), header.BitDepth),
                            Scale(BinaryPrimitives.ReadUInt16BigEndian(body[2..]), header.BitDepth),
                            Scale(BinaryPrimitives.ReadUInt16BigEndian(body[4..]), header.BitDepth));
                        break;
                    case 3:
                        alphas = body.ToArray();
                        break;
                    default:
                        break;
                }
            }
            else if (name.SequenceEqual("IDAT"u8))
            {
                data.Write(body);
            }
            else if (name.SequenceEqual("IEND"u8))
            {
                break;
            }

            at += 12 + (int)length;
        }

        if (!seenHeader || data.Length == 0 || (header.ColorType == 3 && palette is null))
        {
            return null;
        }

        if (header.ColorType == 3 && !replacement.IsEmpty)
        {
            palette = replacement.ToArray();
        }

        byte[] samples;
        try
        {
            data.Position = 0;
            using var inflating = new ZLibStream(data, CompressionMode.Decompress);
            using var plain = new MemoryStream();
            inflating.CopyTo(plain);
            samples = plain.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }

        var picture = new Pixels(header.Width, header.Height, new byte[header.Width * header.Height * 4]);
        var colors = new Colors(header, palette, alphas, transparent);

        return header.Interlaced
            ? Interlaced(header, samples, colors, picture)
            : Straight(header, samples, colors, picture);
    }

    /// <summary>
    /// [png 11.2.3] The palette of an indexed picture, three bytes to
    /// an entry, or null where the file has none to read.
    /// </summary>
    /// <remarks>
    /// [blorb 11.3] This is the half of the adaptive palette rule that
    /// does not need the picture decoded: an ordinary picture hands its
    /// colors on to the adaptive pictures that follow it, and this is
    /// where they are read from.
    /// </remarks>
    public static byte[]? Palette(ReadOnlySpan<byte> file)
    {
        if (file.Length < 8 || !file[..8].SequenceEqual(Signature))
        {
            return null;
        }

        for (var at = 8; at + 8 <= file.Length;)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(file[at..]);
            if (length > int.MaxValue || at + 12 + (int)length > file.Length)
            {
                break;
            }

            var name = file.Slice(at + 4, 4);
            if (name.SequenceEqual("PLTE"u8))
            {
                return file.Slice(at + 8, (int)length).ToArray();
            }

            // [png 5.6] The palette comes before the picture's data, so
            // there is no reason to read past it.
            if (name.SequenceEqual("IDAT"u8) || name.SequenceEqual("IEND"u8))
            {
                break;
            }

            at += 12 + (int)length;
        }

        return null;
    }

    // [png 4.1.1] A picture written in one piece: every row in order,
    // each with its filter in front.
    private static Pixels? Straight(Header header, byte[] samples, Colors colors, Pixels picture)
    {
        var rowBytes = header.RowBytes(header.Width);
        if (rowBytes == 0 || samples.Length < (rowBytes + 1) * (long)header.Height)
        {
            return null;
        }

        var previous = new byte[rowBytes];
        var row = new byte[rowBytes];

        for (var y = 0; y < header.Height; y++)
        {
            var start = y * (rowBytes + 1);
            samples.AsSpan(start + 1, rowBytes).CopyTo(row);
            Unfilter(samples[start], row, previous, header.PixelBytes);
            colors.Write(row, picture, header.Width, 0, 1, y);
            (previous, row) = (row, previous);
        }

        return picture;
    }

    // [png 4.1.1] A picture written as seven passes, each a sparse grid
    // of the whole, so that something of it can be shown before all of
    // it has arrived.
    private static Pixels? Interlaced(Header header, byte[] samples, Colors colors, Pixels picture)
    {
        var at = 0;

        for (var pass = 0; pass < 7; pass++)
        {
            var width = Spread(header.Width - PassColumn[pass], PassColumnStep[pass]);
            var height = Spread(header.Height - PassRow[pass], PassRowStep[pass]);
            if (width == 0 || height == 0)
            {
                continue;
            }

            var rowBytes = header.RowBytes(width);
            if (samples.Length < at + ((rowBytes + 1) * (long)height))
            {
                return null;
            }

            var previous = new byte[rowBytes];
            var row = new byte[rowBytes];

            for (var y = 0; y < height; y++)
            {
                samples.AsSpan(at + 1, rowBytes).CopyTo(row);
                Unfilter(samples[at], row, previous, header.PixelBytes);
                colors.Write(row, picture, width, PassColumn[pass], PassColumnStep[pass], PassRow[pass] + (y * PassRowStep[pass]));
                (previous, row) = (row, previous);
                at += rowBytes + 1;
            }
        }

        return picture;
    }

    private static int Spread(int span, int step) => span <= 0 ? 0 : ((span - 1) / step) + 1;

    // [png 9.2] Each row was written as the difference from what came
    // before it, one of five ways, and is put back the same way.
    private static void Unfilter(byte filter, Span<byte> row, ReadOnlySpan<byte> previous, int pixelBytes)
    {
        switch (filter)
        {
            case 1:
                for (var i = pixelBytes; i < row.Length; i++)
                {
                    row[i] += row[i - pixelBytes];
                }

                break;
            case 2:
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] += previous[i];
                }

                break;
            case 3:
                for (var i = 0; i < row.Length; i++)
                {
                    var left = i >= pixelBytes ? row[i - pixelBytes] : 0;
                    row[i] += (byte)((left + previous[i]) / 2);
                }

                break;
            case 4:
                for (var i = 0; i < row.Length; i++)
                {
                    var left = i >= pixelBytes ? row[i - pixelBytes] : (byte)0;
                    var above = previous[i];
                    var corner = i >= pixelBytes ? previous[i - pixelBytes] : (byte)0;
                    row[i] += Paeth(left, above, corner);
                }

                break;
            default:
                // Filter 0 is no filter, and anything else is a file
                // that says something this does not know, which is left
                // as it stands rather than refused.
                break;
        }
    }

    // [png 9.4] The neighbor the value is nearest to.
    private static byte Paeth(byte left, byte above, byte corner)
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

    // A sample of any width, as a byte: the high byte of a wide one,
    // and a narrow one spread across the whole range so that the
    // largest value it can hold is white.
    private static byte Scale(int value, int depth) => depth switch
    {
        16 => (byte)(value >> 8),
        8 => (byte)value,
        4 => (byte)(value * 17),
        2 => (byte)(value * 85),
        _ => (byte)(value == 0 ? 0 : 255),
    };

    /// <summary>
    /// [png 11.2.2] What a picture says about its pixels.
    /// </summary>
    private readonly record struct Header(int Width, int Height, int BitDepth, int ColorType, bool Interlaced)
    {
        /// <summary>How many samples make one pixel.</summary>
        public int Channels => ColorType switch
        {
            2 => 3,
            4 => 2,
            6 => 4,
            _ => 1,
        };

        /// <summary>
        /// How far apart two pixels are in a row, in whole bytes, which
        /// is what a filter steps back by; samples narrower than a byte
        /// step by one.
        /// </summary>
        public int PixelBytes => Math.Max(Channels * BitDepth / 8, 1);

        /// <summary>The bytes one row of this many pixels takes.</summary>
        public int RowBytes(int width) => ((width * Channels * BitDepth) + 7) / 8;

        public static bool TryRead(ReadOnlySpan<byte> body, out Header header)
        {
            header = default;

            var width = BinaryPrimitives.ReadUInt32BigEndian(body);
            var height = BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
            int depth = body[8];
            int color = body[9];

            // [png 11.2.2] The compression and filter methods have one
            // value each, and a size beyond reason is not a picture.
            if (width == 0 || height == 0 || width > 0xFFFF || height > 0xFFFF || body[10] != 0 || body[11] != 0 || body[12] > 1)
            {
                return false;
            }

            var allowed = color switch
            {
                0 => depth is 1 or 2 or 4 or 8 or 16,
                3 => depth is 1 or 2 or 4 or 8,
                2 or 4 or 6 => depth is 8 or 16,
                _ => false,
            };

            if (!allowed)
            {
                return false;
            }

            header = new Header((int)width, (int)height, depth, color, body[12] == 1);
            return true;
        }
    }

    /// <summary>
    /// Turns the samples of a row into the red, green, blue, and alpha
    /// of a picture, whatever the file said a pixel was.
    /// </summary>
    private sealed class Colors(Header header, byte[]? palette, byte[]? alphas, (int Red, int Green, int Blue) transparent)
    {
        public void Write(ReadOnlySpan<byte> row, Pixels picture, int count, int firstColumn, int columnStep, int y)
        {
            if (y < 0 || y >= picture.Height)
            {
                return;
            }

            for (var i = 0; i < count; i++)
            {
                var x = firstColumn + (i * columnStep);
                if (x >= picture.Width)
                {
                    break;
                }

                var at = ((y * picture.Width) + x) * 4;
                Pixel(row, i, picture.Rgba.AsSpan(at, 4));
            }
        }

        private void Pixel(ReadOnlySpan<byte> row, int index, Span<byte> pixel)
        {
            var depth = header.BitDepth;
            var channels = header.Channels;

            if (header.ColorType == 3)
            {
                // [png 11.2.3] The sample is a place in the palette, and
                // the transparency chunk, where there is one, gives that
                // entry its alpha.
                var entry = Sample(row, index, depth);
                var at = entry * 3;
                var known = palette is not null && at + 2 < palette.Length;
                pixel[0] = known ? palette![at] : (byte)0;
                pixel[1] = known ? palette![at + 1] : (byte)0;
                pixel[2] = known ? palette![at + 2] : (byte)0;
                pixel[3] = alphas is not null && entry < alphas.Length ? alphas[entry] : (byte)255;
                return;
            }

            // Every other kind is one, two, three, or four samples of
            // the same width, in the order gray, gray and alpha, red
            // green and blue, or those three and alpha.
            Span<int> values = stackalloc int[4];
            for (var c = 0; c < channels; c++)
            {
                values[c] = Sample(row, (index * channels) + c, depth);
            }

            switch (header.ColorType)
            {
                case 0:
                    pixel[0] = pixel[1] = pixel[2] = Scale(values[0], depth);
                    pixel[3] = Opaque(values[0], values[0], values[0]);
                    break;
                case 4:
                    pixel[0] = pixel[1] = pixel[2] = Scale(values[0], depth);
                    pixel[3] = Scale(values[1], depth);
                    break;
                case 2:
                    pixel[0] = Scale(values[0], depth);
                    pixel[1] = Scale(values[1], depth);
                    pixel[2] = Scale(values[2], depth);
                    pixel[3] = Opaque(values[0], values[1], values[2]);
                    break;
                default:
                    pixel[0] = Scale(values[0], depth);
                    pixel[1] = Scale(values[1], depth);
                    pixel[2] = Scale(values[2], depth);
                    pixel[3] = Scale(values[3], depth);
                    break;
            }
        }

        // [png 11.3.2] The one color a picture may declare see-through.
        private byte Opaque(int red, int green, int blue)
        {
            var depth = header.BitDepth;
            return transparent.Red >= 0
                && Scale(red, depth) == transparent.Red
                && Scale(green, depth) == transparent.Green
                && Scale(blue, depth) == transparent.Blue
                    ? (byte)0
                    : (byte)255;
        }

        // One sample of a row, counting samples rather than pixels,
        // whether it is packed several to a byte or spread over two.
        private static int Sample(ReadOnlySpan<byte> row, int index, int depth)
        {
            switch (depth)
            {
                case 16:
                {
                    var at = index * 2;
                    return at + 1 < row.Length ? (row[at] << 8) | row[at + 1] : 0;
                }

                case 8:
                    return index < row.Length ? row[index] : 0;

                default:
                {
                    var perByte = 8 / depth;
                    var at = index / perByte;
                    if (at >= row.Length)
                    {
                        return 0;
                    }

                    var shift = 8 - depth - (index % perByte * depth);
                    return (row[at] >> shift) & ((1 << depth) - 1);
                }
            }
        }
    }
}

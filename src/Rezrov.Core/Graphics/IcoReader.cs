using System.Buffers.Binary;

namespace Rezrov.Core.Graphics;

/// <summary>
/// Reads a Windows icon file into pixels a frontend can draw.
/// </summary>
/// <remarks>
/// An icon file is several pictures of the same thing at different
/// sizes, so that whoever shows one can take the size they want rather
/// than scale another. Each is either a PNG, which the larger modern
/// sizes use, or a device independent bitmap of the old kind: a header,
/// a palette where the colors are few enough to need one, the picture
/// upside down, and then a second picture of one bit a pixel saying
/// which pixels are not there at all.
///
/// That last part is the reason this is written out rather than handed
/// to whatever the system offers. An icon of sixteen colors carries its
/// transparency in that mask and nowhere else, and at least one system
/// converter reads the picture without it and washes white lettering on
/// navy out to very nearly nothing.
/// </remarks>
public static class IcoReader
{
    /// <summary>
    /// The pictures an icon file holds, largest first, or an empty list
    /// where the bytes are not an icon file.
    /// </summary>
    public static IReadOnlyList<Pixels> Read(ReadOnlySpan<byte> file)
    {
        // The first six bytes are a reserved word that must be zero, a
        // type of 1 for an icon, and how many pictures follow.
        if (file.Length < 6
            || BinaryPrimitives.ReadUInt16LittleEndian(file) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(file[2..]) != 1)
        {
            return [];
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(file[4..]);
        var pictures = new List<Pixels>();

        for (var i = 0; i < count; i++)
        {
            var entry = 6 + (i * 16);

            if (entry + 16 > file.Length)
            {
                break;
            }

            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(file[(entry + 8)..]);
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(file[(entry + 12)..]);

            if (length <= 0 || offset < 0 || offset + length > file.Length)
            {
                continue;
            }

            if (One(file.Slice(offset, length)) is { } picture)
            {
                pictures.Add(picture);
            }
        }

        return [.. pictures.OrderByDescending(picture => picture.Width * picture.Height)];
    }

    /// <summary>
    /// The picture of the size asked for, the nearest larger one, or
    /// the largest there is. Null where the file holds none at all.
    /// </summary>
    public static Pixels? Read(ReadOnlySpan<byte> file, int size)
    {
        var pictures = Read(file);

        if (pictures.Count == 0)
        {
            return null;
        }

        // Largest first, so the last one at least as large as what was
        // asked for is the nearest above it.
        var chosen = pictures[0];

        foreach (var picture in pictures)
        {
            if (picture.Width >= size && picture.Height >= size)
            {
                chosen = picture;
            }
        }

        return chosen;
    }

    /// <summary>
    /// The first bytes of a PNG, which the larger modern sizes are
    /// stored as rather than as a bitmap.
    /// </summary>
    private static ReadOnlySpan<byte> Png => [0x89, (byte)'P', (byte)'N', (byte)'G'];

    /// <summary>One picture out of the file, whichever kind it is.</summary>
    private static Pixels? One(ReadOnlySpan<byte> image) =>
        image.Length > 8 && image[..4].SequenceEqual(Png)
            ? PngReader.Read(image)
            : Bitmap(image);

    /// <summary>
    /// A device independent bitmap, which is the old kind of icon: the
    /// colors upside down, and then a mask saying which pixels to leave
    /// alone.
    /// </summary>
    private static Pixels? Bitmap(ReadOnlySpan<byte> image)
    {
        if (image.Length < 40)
        {
            return null;
        }

        var header = (int)BinaryPrimitives.ReadUInt32LittleEndian(image);
        var width = BinaryPrimitives.ReadInt32LittleEndian(image[4..]);

        // The declared height counts the colors and the mask together,
        // so the picture itself is half of it.
        var height = BinaryPrimitives.ReadInt32LittleEndian(image[8..]) / 2;
        var depth = BinaryPrimitives.ReadUInt16LittleEndian(image[14..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(image[16..]);

        if (header < 40 || width <= 0 || height <= 0 || compression != 0)
        {
            return null;
        }

        if (depth is not (1 or 4 or 8 or 24 or 32))
        {
            return null;
        }

        var declared = (int)BinaryPrimitives.ReadUInt32LittleEndian(image[32..]);
        var colors = depth <= 8 ? (declared == 0 ? 1 << depth : declared) : 0;
        var palette = header;
        var pixels = palette + (colors * 4);

        if (pixels > image.Length)
        {
            return null;
        }

        // Every row of either picture is padded out to a whole number
        // of four byte words.
        var stride = ((width * depth) + 31) / 32 * 4;
        var maskStride = (width + 31) / 32 * 4;
        var mask = pixels + (stride * height);

        var rgba = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            // Upside down: the first row in the file is the bottom one.
            var row = pixels + (stride * (height - 1 - y));
            var maskRow = mask + (maskStride * (height - 1 - y));

            for (var x = 0; x < width; x++)
            {
                var at = ((y * width) + x) * 4;
                var (red, green, blue, alpha) = Pixel(image, row, palette, depth, x);

                // The mask is one bit a pixel, set where the picture is
                // not there. A picture with an alpha of its own that
                // says something everywhere is trusted over the mask,
                // which such files often leave empty.
                if (depth != 32 || alpha == 0)
                {
                    alpha = Hidden(image, maskRow, x) ? (byte)0 : (byte)255;
                }

                rgba[at] = red;
                rgba[at + 1] = green;
                rgba[at + 2] = blue;
                rgba[at + 3] = alpha;
            }
        }

        return new Pixels(width, height, rgba);
    }

    /// <summary>
    /// One pixel of a row, whether it names a color in the palette or
    /// carries the color itself.
    /// </summary>
    private static (byte Red, byte Green, byte Blue, byte Alpha) Pixel(
        ReadOnlySpan<byte> image, int row, int palette, int depth, int x)
    {
        if (depth >= 24)
        {
            var at = row + (x * (depth / 8));

            if (at + (depth / 8) > image.Length)
            {
                return ((byte)0, (byte)0, (byte)0, (byte)0);
            }

            // Blue, green, red, and for the deeper kind an alpha.
            return (image[at + 2], image[at + 1], image[at], depth == 32 ? image[at + 3] : (byte)255);
        }

        var index = Index(image, row, depth, x);
        var entry = palette + (index * 4);

        return entry + 4 > image.Length
            ? ((byte)0, (byte)0, (byte)0, (byte)0)
            : (image[entry + 2], image[entry + 1], image[entry], (byte)0);
    }

    /// <summary>
    /// Which color of the palette a pixel names, out of a row where
    /// several of them share a byte.
    /// </summary>
    private static int Index(ReadOnlySpan<byte> image, int row, int depth, int x)
    {
        var at = row + (x * depth / 8);

        if (at >= image.Length)
        {
            return 0;
        }

        return depth switch
        {
            8 => image[at],
            4 => (image[at] >> (x % 2 == 0 ? 4 : 0)) & 0x0F,
            _ => (image[at] >> (7 - (x % 8))) & 1,
        };
    }

    /// <summary>
    /// Whether the mask says a pixel is not part of the picture.
    /// </summary>
    private static bool Hidden(ReadOnlySpan<byte> image, int row, int x)
    {
        var at = row + (x / 8);

        return at < image.Length && ((image[at] >> (7 - (x % 8))) & 1) != 0;
    }
}

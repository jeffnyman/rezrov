using System.Buffers.Binary;

namespace Rezrov.Core.Blorb;

/// <summary>
/// What kind of picture a resource holds.
/// </summary>
public enum PictureKind
{
    /// <summary>[blorb 2.1] A PNG file.</summary>
    Png,

    /// <summary>[blorb 2.2] A JPEG file.</summary>
    Jpeg,

    /// <summary>
    /// [blorb 2.3] A placeholder rectangle with a size and no contents,
    /// which exists for the Infocom games and can be measured and
    /// erased but not drawn.
    /// </summary>
    Rectangle,
}

/// <summary>
/// A picture as the catalog knows it: its number, kind, and size in
/// its own pixels.
/// </summary>
public sealed record PictureInfo(int Number, PictureKind Kind, int Width, int Height, ReadOnlyMemory<byte> Data);

/// <summary>
/// [blorb 11.2] How a picture scales with the window: a standard ratio
/// and the least and greatest ratios allowed, each as a fraction. A
/// zero fraction means no limit in that direction.
/// </summary>
public sealed record PictureScaling(
    int StandardNumerator, int StandardDenominator,
    int MinimumNumerator, int MinimumDenominator,
    int MaximumNumerator, int MaximumDenominator);

/// <summary>
/// The pictures in a Blorb file, with their sizes and the resolution
/// rules for scaling them to the screen.
/// </summary>
/// <remarks>
/// [zm 8.8.6] Pictures are not in the story file; the interpreter is
/// expected to know where to find them, and a Blorb file is where.
/// [zm op:picture_data] asks for a picture's height and width, so the
/// sizes are read here from each picture's own header without
/// decoding the picture, which a text frontend never needs to do.
///
/// [blorb 11.2] The resolution chunk says how pictures scale when the
/// screen is not the size the author drew for. The interpreter works
/// out the scaled size and reports that to the game, which lays its
/// display out from what it is told.
/// </remarks>
public sealed class BlorbPictures
{
    private readonly Dictionary<int, PictureInfo> _pictures = [];
    private readonly Dictionary<int, PictureScaling> _scaling = [];

    private BlorbPictures()
    {
    }

    /// <summary>The pictures by number, in resource order.</summary>
    public IReadOnlyList<PictureInfo> Pictures { get; private set; } = [];

    /// <summary>How many pictures there are.</summary>
    public int Count => Pictures.Count;

    /// <summary>
    /// [blorb 11.1] The release number of the picture file, which
    /// [zm op:picture_data] reports for picture 0.
    /// </summary>
    public int Release { get; private set; }

    /// <summary>
    /// [blorb 11.2] The window size the author drew for, or null when
    /// the file has no resolution chunk and nothing scales.
    /// </summary>
    public (int Width, int Height)? StandardWindow { get; private set; }

    /// <summary>
    /// Reads the pictures out of a Blorb file.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// A picture's header is malformed.
    /// </exception>
    public static BlorbPictures From(BlorbFile blorb)
    {
        ArgumentNullException.ThrowIfNull(blorb);

        var pictures = new BlorbPictures { Release = blorb.ReleaseNumber };
        var list = new List<PictureInfo>();

        foreach (var resource in blorb.Resources)
        {
            if (resource.Usage != ResourceUsage.Picture)
            {
                continue;
            }

            var info = Measure(resource);
            list.Add(info);
            pictures._pictures[resource.Number] = info;
        }

        pictures.Pictures = list;

        foreach (var chunk in blorb.Chunks)
        {
            if (chunk.Id == "Reso")
            {
                pictures.ReadResolution(chunk.Data.Span);
            }
        }

        return pictures;
    }

    /// <summary>Whether a picture with this number exists.</summary>
    public bool Contains(int number) => _pictures.ContainsKey(number);

    /// <summary>The picture with this number, or null.</summary>
    public PictureInfo? Find(int number) => _pictures.GetValueOrDefault(number);

    /// <summary>
    /// [blorb 11.2] The size a picture should be drawn at on a screen
    /// of the given size, in screen pixels: its own size when it does
    /// not scale, and otherwise its size times the scaling ratio the
    /// resolution chunk gives for a screen this big.
    /// </summary>
    /// <returns>Null when there is no such picture.</returns>
    public (int Width, int Height)? ScaledSize(int number, int screenWidth, int screenHeight)
    {
        if (Find(number) is not { } picture)
        {
            return null;
        }

        if (StandardWindow is not { } standard || !_scaling.TryGetValue(number, out var scaling))
        {
            return (picture.Width, picture.Height);
        }

        // The elbow room factor: how many times the standard window
        // fits into the screen, in whichever direction is tighter.
        var elbowRoom = Math.Min(screenWidth / (double)standard.Width, screenHeight / (double)standard.Height);
        var ratio = elbowRoom * scaling.StandardNumerator / scaling.StandardDenominator;

        var minimum = scaling.MinimumDenominator == 0 ? 0 : scaling.MinimumNumerator / (double)scaling.MinimumDenominator;
        var maximum = scaling.MaximumDenominator == 0 ? double.PositiveInfinity : scaling.MaximumNumerator / (double)scaling.MaximumDenominator;

        // A minimum equal to the maximum fixes the ratio outright.
        ratio = Math.Clamp(ratio, minimum, maximum);

        return (Scale(picture.Width, ratio), Scale(picture.Height, ratio));

        static int Scale(int size, double ratio) => size == 0 ? 0 : Math.Max((int)Math.Round(size * ratio), 1);
    }

    private static PictureInfo Measure(BlorbResource resource)
    {
        var data = resource.Data.Span;

        switch (resource.ChunkType)
        {
            case "PNG ":
            {
                // The PNG signature, then the IHDR chunk, whose data is
                // the width and height as four-byte big-endian words.
                ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
                if (data.Length < 24 || !data[..8].SequenceEqual(signature) || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
                {
                    throw new InvalidDataException($"Picture {resource.Number} is not a PNG file.");
                }

                var width = BinaryPrimitives.ReadInt32BigEndian(data.Slice(16, 4));
                var height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(20, 4));
                return new PictureInfo(resource.Number, PictureKind.Png, width, height, resource.Data);
            }

            case "JPEG":
            {
                var (width, height) = MeasureJpeg(data, resource.Number);
                return new PictureInfo(resource.Number, PictureKind.Jpeg, width, height, resource.Data);
            }

            case "Rect":
            {
                // [blorb 2.3] The width, then the height.
                if (data.Length < 8)
                {
                    throw new InvalidDataException($"Picture {resource.Number} is a rectangle without a size.");
                }

                var width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
                var height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
                return new PictureInfo(resource.Number, PictureKind.Rectangle, width, height, resource.Data);
            }

            default:
                throw new InvalidDataException($"Picture {resource.Number} is a {resource.ChunkType.Trim()} chunk, which is not a picture format.");
        }
    }

    // A JPEG is a sequence of marker segments; the start-of-frame
    // segment carries the height and width, each as two bytes, after
    // the segment length and the sample precision.
    private static (int Width, int Height) MeasureJpeg(ReadOnlySpan<byte> data, int number)
    {
        var at = 2;
        while (at + 4 <= data.Length && data[at] == 0xFF)
        {
            var marker = data[at + 1];
            if (marker == 0xFF)
            {
                at++;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at + 2, 2));
            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC);
            if (isStartOfFrame && at + 9 <= data.Length)
            {
                var height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at + 5, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at + 7, 2));
                return (width, height);
            }

            at += 2 + length;
        }

        throw new InvalidDataException($"Picture {number} is a JPEG file without a frame header.");
    }

    // [blorb 11.2] The standard, minimum, and maximum window sizes, then
    // one 28-byte entry for each picture that scales.
    private void ReadResolution(ReadOnlySpan<byte> data)
    {
        if (data.Length < 24)
        {
            throw new InvalidDataException("The resolution chunk is too short to hold a window size.");
        }

        StandardWindow = (BinaryPrimitives.ReadInt32BigEndian(data[..4]), BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4)));

        for (var at = 24; at + 28 <= data.Length; at += 28)
        {
            var number = BinaryPrimitives.ReadInt32BigEndian(data.Slice(at, 4));
            _scaling[number] = new PictureScaling(
                BinaryPrimitives.ReadInt32BigEndian(data.Slice(at + 4, 4)),
                BinaryPrimitives.ReadInt32BigEndian(data.Slice(at + 8, 4)),
                BinaryPrimitives.ReadInt32BigEndian(data.Slice(at + 12, 4)),
                BinaryPrimitives.ReadInt32BigEndian(data.Slice(at + 16, 4)),
                BinaryPrimitives.ReadInt32BigEndian(data.Slice(at + 20, 4)),
                BinaryPrimitives.ReadInt32BigEndian(data.Slice(at + 24, 4)));
        }
    }
}

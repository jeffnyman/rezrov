using Rezrov.Core.Blorb;

namespace Rezrov.Core.Graphics;

/// <summary>
/// Turns a picture of a resource file into pixels, whichever format it
/// is written in.
/// </summary>
/// <remarks>
/// [blorb 2] A resource file may hold a picture as a PNG, as a JPEG, or
/// as a placeholder rectangle with nothing in it. A frontend wants the
/// pixels and not the difference, so this is the one door in, and the
/// decoders behind it are the ones that read each format.
/// </remarks>
public static class PictureReader
{
    /// <summary>
    /// The pixels of a picture, or null for one that cannot be drawn:
    /// [blorb 2.3] a placeholder rectangle, or a file this cannot
    /// decode.
    /// </summary>
    public static Pixels? Decode(PictureInfo picture) => Decode(picture, default);

    /// <summary>
    /// The pixels of a picture, plotted with a palette of the caller's
    /// choosing in place of the one it carries.
    /// </summary>
    /// <remarks>
    /// [blorb 11.3] Only an indexed PNG has a palette to replace, and
    /// only a resource file with an adaptive palette chunk ever asks,
    /// so anything else ignores the palette and decodes as it would
    /// have anyway.
    /// </remarks>
    public static Pixels? Decode(PictureInfo picture, ReadOnlySpan<byte> palette)
    {
        ArgumentNullException.ThrowIfNull(picture);

        return Decode(picture.Kind, picture.Data.Span, palette);
    }

    /// <summary>
    /// The pixels of a picture of the given kind.
    /// </summary>
    public static Pixels? Decode(PictureKind kind, ReadOnlySpan<byte> data) =>
        Decode(kind, data, default);

    /// <summary>
    /// The pixels of a picture of the given kind, with a palette of the
    /// caller's choosing.
    /// </summary>
    public static Pixels? Decode(PictureKind kind, ReadOnlySpan<byte> data, ReadOnlySpan<byte> palette) => kind switch
    {
        PictureKind.Png => PngReader.Read(data, palette),
        PictureKind.Jpeg => JpegReader.Read(data),
        _ => null,
    };
}

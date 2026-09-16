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
    public static Pixels? Decode(PictureInfo picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        return Decode(picture.Kind, picture.Data.Span);
    }

    /// <summary>
    /// The pixels of a picture of the given kind.
    /// </summary>
    public static Pixels? Decode(PictureKind kind, ReadOnlySpan<byte> data) => kind switch
    {
        PictureKind.Png => PngReader.Read(data),
        PictureKind.Jpeg => JpegReader.Read(data),
        _ => null,
    };
}

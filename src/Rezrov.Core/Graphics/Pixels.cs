namespace Rezrov.Core.Graphics;

/// <summary>
/// A decoded picture: its size, and its pixels as red, green, blue,
/// and alpha, one byte each, row by row from the top.
/// </summary>
/// <remarks>
/// [blorb 2] A resource file holds pictures in their own formats, each
/// with its own way of saying what a pixel is. Decoding turns them all
/// into this one shape, which is what a frontend draws and what the
/// scaling rules of the Blorb specification measure.
/// </remarks>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="Rgba">
/// Four bytes for every pixel, width times height of them, with an
/// alpha of 255 where the picture is opaque.
/// </param>
public sealed record Pixels(int Width, int Height, byte[] Rgba)
{
    /// <summary>
    /// One pixel, as red, green, blue, and alpha. A point outside the
    /// picture is transparent black.
    /// </summary>
    public (byte Red, byte Green, byte Blue, byte Alpha) At(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return (0, 0, 0, 0);
        }

        var at = ((y * Width) + x) * 4;
        return (Rgba[at], Rgba[at + 1], Rgba[at + 2], Rgba[at + 3]);
    }
}

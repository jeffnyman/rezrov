using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Rezrov.Core.Blorb;
using Rezrov.Core.Graphics;

namespace Rezrov.Gui;

/// <summary>
/// The pictures of a resource file, decoded once each and kept as
/// bitmaps ready to draw.
/// </summary>
/// <remarks>
/// [zm 8.8.6] A Version 6 game draws the same few pictures over and
/// over as the player moves about, and Zork Zero has two thousand of
/// them in all, so decoding on every repaint is out of the question and
/// decoding all of them up front nearly as bad. Each is read the first
/// time it is asked for and kept from then on, and one that cannot be
/// read is remembered as such so the attempt is made once.
/// </remarks>
internal sealed class GuiPictures
{
    // [blorb 11.3] A picture that takes its colors from another is a
    // different picture under each set of them, so the colors are part
    // of what a kept bitmap is kept under. An ordinary picture has no
    // palette of its own here and is kept under none.
    private readonly Dictionary<(int Number, byte[]? Palette), WriteableBitmap?> _bitmaps = [];
    private readonly BlorbPictures? _catalog;

    public GuiPictures(BlorbFile? resources)
    {
        if (resources is null)
        {
            return;
        }

        try
        {
            _catalog = BlorbPictures.From(resources);
        }
        catch (InvalidDataException)
        {
            // [blorb 2] A malformed picture header makes the whole
            // catalog unreadable, and a game with no pictures it can
            // draw is better than none at all.
            _catalog = null;
        }
    }

    /// <summary>
    /// Whether the resource file has a picture of that number at all,
    /// which can be asked from any thread since nothing is decoded and
    /// nothing is kept.
    /// </summary>
    public bool Has(int number) => _catalog?.Contains(number) == true;

    /// <summary>
    /// The picture as a bitmap, or null for one that is not there,
    /// cannot be decoded, or [blorb 2.3] is a placeholder rectangle
    /// with nothing in it.
    /// </summary>
    public WriteableBitmap? Bitmap(int number, byte[]? palette = null)
    {
        if (_bitmaps.TryGetValue((number, palette), out var known))
        {
            return known;
        }

        var bitmap = Read(number, palette);
        _bitmaps[(number, palette)] = bitmap;
        return bitmap;
    }

    /// <summary>
    /// A decoded picture as a bitmap ready to draw, or null for one
    /// with no pixels in it at all.
    /// </summary>
    /// <remarks>
    /// Red, green, blue, and alpha, one byte each and the color not
    /// multiplied by the alpha, which is the shape the decoders and the
    /// canvas already keep pixels in, so this is a copy and nothing
    /// more.
    /// </remarks>
    public static WriteableBitmap? ToBitmap(Pixels pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        if (pixels.Width <= 0 || pixels.Height <= 0)
        {
            return null;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(pixels.Width, pixels.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);

        using (var locked = bitmap.Lock())
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                Marshal.Copy(
                    pixels.Rgba,
                    y * pixels.Width * 4,
                    locked.Address + (y * locked.RowBytes),
                    pixels.Width * 4);
            }
        }

        return bitmap;
    }

    private WriteableBitmap? Read(int number, byte[]? palette) =>
        _catalog?.Find(number) is { } picture && PictureReader.Decode(picture, palette) is { } pixels
            ? ToBitmap(pixels)
            : null;
}

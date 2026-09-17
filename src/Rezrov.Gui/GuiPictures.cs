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
    private readonly Dictionary<int, WriteableBitmap?> _bitmaps = [];
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
    /// The picture as a bitmap, or null for one that is not there,
    /// cannot be decoded, or [blorb 2.3] is a placeholder rectangle
    /// with nothing in it.
    /// </summary>
    public WriteableBitmap? Bitmap(int number)
    {
        if (_bitmaps.TryGetValue(number, out var known))
        {
            return known;
        }

        var bitmap = Read(number);
        _bitmaps[number] = bitmap;
        return bitmap;
    }

    private WriteableBitmap? Read(int number)
    {
        if (_catalog?.Find(number) is not { } picture || PictureReader.Decode(picture) is not { } pixels)
        {
            return null;
        }

        if (pixels.Width <= 0 || pixels.Height <= 0)
        {
            return null;
        }

        // Red, green, blue, and alpha, one byte each and the color not
        // multiplied by the alpha, which is the shape the decoders
        // already keep pixels in.
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
}

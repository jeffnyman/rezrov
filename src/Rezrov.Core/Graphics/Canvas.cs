namespace Rezrov.Core.Graphics;

/// <summary>
/// A rectangle of pixels that can be painted on: filled with a color,
/// drawn on with a picture, and resized.
/// </summary>
/// <remarks>
/// [glk #window_graphics] A graphics window is a canvas whose contents
/// are entirely the game's to decide, kept from one call to the next,
/// and a resize throws away what falls outside and fills what is new
/// with the background color. That is all this is, and keeping it here
/// rather than in a frontend means the compositing is the same
/// wherever the picture ends up, and can be tested with no frontend at
/// all.
///
/// [glk #graphics_testing] Alpha is honored rather than ignored, which
/// is what gestalt_GraphicsTransparency promises when it answers one.
/// The canvas itself stays opaque, since it begins as a solid color.
/// </remarks>
public sealed class Canvas
{
    /// <summary>
    /// [glk #graphics_graphics] The color a window starts out as.
    /// </summary>
    public const uint White = 0x00FFFFFFu;

    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="background">
    /// [glk op:window_set_background_color] The color a clear or a
    /// resize leaves behind, as 0x00RRGGBB.
    /// </param>
    public Canvas(int width, int height, uint background = White)
    {
        Background = background;
        Pixels = new Pixels(Math.Max(width, 0), Math.Max(height, 0), new byte[Math.Max(width, 0) * Math.Max(height, 0) * 4]);
        Clear();
    }

    /// <summary>What has been painted, for a frontend to show.</summary>
    public Pixels Pixels { get; private set; }

    public int Width => Pixels.Width;

    public int Height => Pixels.Height;

    /// <summary>
    /// [glk op:window_set_background_color] The color a clear or a
    /// resize leaves behind. Changing it does not change what is
    /// already painted.
    /// </summary>
    public uint Background { get; set; }

    /// <summary>One pixel, as red, green, blue, and alpha.</summary>
    public (byte Red, byte Green, byte Blue, byte Alpha) At(int x, int y) => Pixels.At(x, y);

    /// <summary>
    /// [glk op:window_clear] Paints the whole canvas the background
    /// color.
    /// </summary>
    public void Clear() => Fill(0, 0, Width, Height, Background);

    /// <summary>
    /// [glk #window_graphics] Changes the size, keeping what the old and
    /// the new have in common and filling the rest with the background
    /// color.
    /// </summary>
    public void Resize(int width, int height)
    {
        width = Math.Max(width, 0);
        height = Math.Max(height, 0);

        if (width == Width && height == Height)
        {
            return;
        }

        var kept = Pixels;
        Pixels = new Pixels(width, height, new byte[width * height * 4]);
        Fill(0, 0, width, height, Background);

        var across = Math.Min(width, kept.Width);
        var down = Math.Min(height, kept.Height);

        for (var y = 0; y < down; y++)
        {
            kept.Rgba.AsSpan(y * kept.Width * 4, across * 4)
                .CopyTo(Pixels.Rgba.AsSpan(y * width * 4));
        }
    }

    /// <summary>
    /// [glk op:window_fill_rect] Paints a rectangle one color. A
    /// rectangle may hang over any edge, and only what lands on the
    /// canvas is painted.
    /// </summary>
    public void Fill(int left, int top, int width, int height, uint color)
    {
        // [glk op:window_fill_rect] A rectangle of no width or height
        // draws nothing.
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var (right, bottom) = (Clip(left + (long)width, Width), Clip(top + (long)height, Height));
        left = Clip(left, Width);
        top = Clip(top, Height);

        if (right <= left || bottom <= top)
        {
            return;
        }

        var red = (byte)(color >> 16);
        var green = (byte)(color >> 8);
        var blue = (byte)color;

        for (var y = top; y < bottom; y++)
        {
            var at = ((y * Width) + left) * 4;
            for (var x = left; x < right; x++)
            {
                Pixels.Rgba[at] = red;
                Pixels.Rgba[at + 1] = green;
                Pixels.Rgba[at + 2] = blue;
                Pixels.Rgba[at + 3] = 255;
                at += 4;
            }
        }
    }

    /// <summary>
    /// [glk op:image_draw_scaled] Draws a picture at a place and a size,
    /// over whatever is already there.
    /// </summary>
    /// <remarks>
    /// A picture drawn smaller than it is has each of its destination
    /// pixels averaged over the source pixels it stands for, so that
    /// reducing a photograph does not drop every other row of it. A
    /// picture drawn at its own size or larger takes the nearest source
    /// pixel, which leaves the edges of drawn artwork as sharp as they
    /// were. Both fall out of the same walk: the span a destination
    /// pixel covers is one source pixel wide until there is more than
    /// one to cover.
    /// </remarks>
    public void Draw(Pixels image, int left, int top, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (width <= 0 || height <= 0 || image.Width <= 0 || image.Height <= 0)
        {
            return;
        }

        var right = Clip(left + (long)width, Width);
        var bottom = Clip(top + (long)height, Height);
        var from = Clip(left, Width);
        var down = Clip(top, Height);

        for (var y = down; y < bottom; y++)
        {
            var (topRow, bottomRow) = Span(y - top, height, image.Height);

            for (var x = from; x < right; x++)
            {
                var (leftColumn, rightColumn) = Span(x - left, width, image.Width);
                Over(image, leftColumn, topRow, rightColumn, bottomRow, ((y * Width) + x) * 4);
            }
        }
    }

    /// <summary>
    /// Which source pixels one destination pixel stands for: at least
    /// one, and more where the picture is being reduced.
    /// </summary>
    private static (int From, int To) Span(int at, int size, int source)
    {
        var from = (int)((long)at * source / size);
        var to = (int)(((long)at + 1) * source / size);
        return (from, Math.Max(to, from + 1));
    }

    /// <summary>
    /// Averages a patch of the picture and lays it over the canvas,
    /// which keeps the canvas opaque since it began as a solid color.
    /// </summary>
    private void Over(Pixels image, int left, int top, int right, int bottom, int at)
    {
        long red = 0;
        long green = 0;
        long blue = 0;
        long alpha = 0;
        var counted = 0;

        for (var y = top; y < bottom && y < image.Height; y++)
        {
            for (var x = left; x < right && x < image.Width; x++)
            {
                var pixel = image.At(x, y);

                // The color is weighted by the alpha before averaging,
                // so that a transparent pixel does not drag whatever
                // color it happens to carry into its neighbors.
                red += pixel.Red * pixel.Alpha;
                green += pixel.Green * pixel.Alpha;
                blue += pixel.Blue * pixel.Alpha;
                alpha += pixel.Alpha;
                counted++;
            }
        }

        if (counted == 0 || alpha == 0)
        {
            return;
        }

        // Rounded rather than truncated, so that a pixel standing for
        // an equal share of black and white is the gray between them
        // and not the shade just below it.
        var over = (int)((alpha + (counted / 2)) / counted);
        var under = 255 - over;
        var canvas = Pixels.Rgba;

        canvas[at] = Blend(Average(red, alpha), over, canvas[at], under);
        canvas[at + 1] = Blend(Average(green, alpha), over, canvas[at + 1], under);
        canvas[at + 2] = Blend(Average(blue, alpha), over, canvas[at + 2], under);
        canvas[at + 3] = 255;
    }

    private static long Average(long weighted, long alpha) => (weighted + (alpha / 2)) / alpha;

    private static byte Blend(long source, int over, byte destination, int under) =>
        (byte)(((source * over) + (destination * under) + 127) / 255);

    private static int Clip(long at, int limit) => (int)Math.Clamp(at, 0, limit);
}

namespace Rezrov.Gtui;

/// <summary>
/// A rectangle of pixels to draw into, which is everything this program
/// has: the window is handed one of these and shows it.
/// </summary>
/// <remarks>
/// A pixel is a color and nothing more, kept as one number per pixel in
/// a single array, row after row from the top. That is the shape every
/// window system on the three platforms can be handed directly, so
/// nothing has to be converted on the way out.
///
/// Everything here clips rather than throws. A game is free to ask for
/// a character at a place that is half off the screen, and a frontend
/// that fell over when it did would be the frontend's fault.
/// </remarks>
public sealed class Surface
{
    /// <summary>
    /// A color, as red, green, and blue in the low three bytes. The top
    /// byte is unused and kept clear, which is what the window systems
    /// want for an opaque pixel.
    /// </summary>
    public const uint White = 0x00FFFFFF;

    /// <summary>The color a screen starts as, until a game says.</summary>
    public const uint Black = 0x00000000;

    public Surface(int width, int height)
    {
        Width = Math.Max(width, 0);
        Height = Math.Max(height, 0);
        Pixels = new uint[Width * Height];
    }

    /// <summary>The pixels across.</summary>
    public int Width { get; }

    /// <summary>The pixels down.</summary>
    public int Height { get; }

    /// <summary>
    /// The pixels themselves, one number each, row after row from the
    /// top. This is handed to the window system as it stands.
    /// </summary>
    public uint[] Pixels { get; }

    /// <summary>
    /// The color of one pixel, or black outside the surface.
    /// </summary>
    public uint this[int x, int y] =>
        x >= 0 && x < Width && y >= 0 && y < Height ? Pixels[(y * Width) + x] : Black;

    /// <summary>Makes the whole surface one color.</summary>
    public void Fill(uint color) => Array.Fill(Pixels, color);

    /// <summary>
    /// Makes a rectangle one color, clipped to the surface.
    /// </summary>
    public void Fill(int left, int top, int width, int height, uint color)
    {
        var right = Math.Min(left + width, Width);
        var bottom = Math.Min(top + height, Height);

        for (var y = Math.Max(top, 0); y < bottom; y++)
        {
            for (var x = Math.Max(left, 0); x < right; x++)
            {
                Pixels[(y * Width) + x] = color;
            }
        }
    }

    /// <summary>
    /// Draws one character, a cell of the font at a time, in the ink
    /// color. Cells that are off are left as they are, so whatever
    /// filled the background first shows through.
    /// </summary>
    /// <remarks>
    /// The height asked for need not be the height the font draws:
    /// [zm 16.1] font 3 is eight rows and is shown in a cell of
    /// sixteen, so each of its rows is drawn twice. That is not a
    /// decoration. The standard says font 3's characters are printed
    /// immediately next to each other in all four directions, and a
    /// frontend that drew eight rows in a sixteen row cell would leave
    /// a gap through which every box Infocom drew would come apart.
    /// </remarks>
    /// <param name="width">
    /// How far to the right the character may reach, counted from the
    /// left edge given. Nothing is drawn past it, so a character can
    /// never spill into the cell beside it however it is styled, which
    /// bold and italic together would otherwise do.
    /// </param>
    /// <param name="slant">
    /// Leans the character, by drawing its upper half one cell further
    /// right than its lower half. [zm 8.7.1] Italic in a font with one
    /// shape per character can be no better than this, and the lean is
    /// what tells a player the words are emphasized. The gap column
    /// pays for it, so the leaning half does not touch its neighbor.
    /// </param>
    public void Glyph(ReadOnlySpan<byte> rows, int left, int top, int width, int height, uint ink, bool slant = false)
    {
        if (rows.Length == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        for (var y = 0; y < height; y++)
        {
            var lean = slant && y < height / 2 ? 1 : 0;
            var bits = rows[y * rows.Length / height];
            if (bits == 0)
            {
                continue;
            }

            var at = top + y;
            if (at < 0 || at >= Height)
            {
                continue;
            }

            for (var cell = 0; cell < 8; cell++)
            {
                if ((bits & (1 << (7 - cell))) == 0 || cell + lean >= width)
                {
                    continue;
                }

                var x = left + cell + lean;
                if (x >= 0 && x < Width)
                {
                    Pixels[(at * Width) + x] = ink;
                }
            }
        }
    }
}

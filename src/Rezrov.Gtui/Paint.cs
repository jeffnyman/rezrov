using Rezrov.ZMachine.Screen;

namespace Rezrov.Gtui;

/// <summary>
/// Turns a screen of characters into a rectangle of pixels.
/// </summary>
/// <remarks>
/// This is the whole of what the graphical toolkit was doing for the
/// other window program, written out: a cell of the screen becomes a
/// rectangle the size of the font, the background is filled, and the
/// character is drawn into it a bit at a time.
///
/// It is deliberately separate from the window. Nothing here knows what
/// a window is, so the drawing can be tested by looking at the pixels
/// rather than at a screen, and the same drawing serves all three
/// platforms.
/// </remarks>
public static class Paint
{
    /// <summary>The pixels across one character cell.</summary>
    public const int CellWidth = TextFont.Width;

    /// <summary>The pixels down one character cell.</summary>
    public const int CellHeight = TextFont.Height;

    /// <summary>
    /// How many characters across and down fit on a surface of that
    /// size, which is what the game is told its screen measures.
    /// </summary>
    public static (int Columns, int Rows) Fits(int width, int height) =>
        (Math.Max(width / CellWidth, 1), Math.Max(height / CellHeight, 1));

    /// <summary>
    /// Draws the whole screen onto the surface.
    /// </summary>
    public static void Screen(Surface surface, BufferedScreen screen)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(screen);

        // [zm 8.4] A window is rarely a whole number of characters
        // across and down, so a strip is left over at the right and the
        // bottom. It is the screen's own color, not a border, or the
        // game appears to sit on a sheet of paper a size too small.
        surface.Fill(Pixel(screen.DefaultBackground, Surface.Black));

        for (var row = 0; row < screen.Height; row++)
        {
            for (var column = 0; column < screen.Width; column++)
            {
                Character(surface, screen, screen[row, column], row, column);
            }
        }

        Cursor(surface, screen);
    }

    /// <summary>
    /// One cell: its background, and then whatever character is in it.
    /// </summary>
    private static void Character(Surface surface, BufferedScreen screen, Cell cell, int row, int column)
    {
        var attributes = cell.Attributes;

        // [zm 8.7.1] Reverse video swaps the two colors for this cell
        // only, which is how a status line is drawn.
        var reversed = attributes.Style.HasFlag(TextStyle.ReverseVideo);
        var ink = Pixel(
            reversed ? attributes.Background : attributes.Foreground,
            Pixel(screen.DefaultForeground, Surface.White));
        var paper = Pixel(
            reversed ? attributes.Foreground : attributes.Background,
            Pixel(screen.DefaultBackground, Surface.Black));

        var left = column * CellWidth;
        var top = row * CellHeight;

        surface.Fill(left, top, CellWidth, CellHeight, paper);

        if (cell.Character is '\0' or ' ')
        {
            return;
        }

        // [zm 16] Font 3 is drawn from the shapes the standard gives,
        // rather than from the nearest character an ordinary font
        // happens to have, which is all a frontend made of cells can
        // manage. This is the reason the program has its own font.
        var rows = attributes.Font == TextAttributes.CharacterGraphicsFont
            ? CharacterGraphics.Bitmap(cell.Character)
            : TextFont.Bitmap(cell.Character);

        var slant = attributes.Style.HasFlag(TextStyle.Italic);

        surface.Glyph(rows, left, top, CellWidth, CellHeight, ink, slant);

        // [zm 8.7.1] Bold, in a font with one weight, is the character
        // drawn again one cell to the right. There is room for it: a
        // letter uses seven of the eight columns and the eighth is the
        // gap, so the heavier shape spends the gap rather than running
        // into its neighbor.
        if (attributes.Style.HasFlag(TextStyle.Bold))
        {
            surface.Glyph(rows, left + 1, top, CellWidth - 1, CellHeight, ink, slant);
        }
    }

    /// <summary>
    /// The cursor, as a line under the character it is on, so that it
    /// does not hide what is already there.
    /// </summary>
    private static void Cursor(Surface surface, BufferedScreen screen)
    {
        if (screen.Cursor is not { } cursor)
        {
            return;
        }

        surface.Fill(
            cursor.Column * CellWidth,
            ((cursor.Row + 1) * CellHeight) - 2,
            CellWidth,
            2,
            Pixel(screen.DefaultForeground, Surface.White));
    }

    /// <summary>
    /// [zm 8.3.7] A color number as a pixel. The standard gives each of
    /// them as five bits per channel, which is the palette these games
    /// were written for, so the bits are spread across eight rather
    /// than a second table of colors being invented here.
    /// </summary>
    public static uint Pixel(ScreenColor color, uint fallback)
    {
        if (!ScreenColors.IsActualColor(color))
        {
            return fallback;
        }

        var packed = ScreenColors.ToTrueColor(color);

        return (Spread(packed & 0x1F) << 16)
            | (Spread((packed >> 5) & 0x1F) << 8)
            | Spread((packed >> 10) & 0x1F);
    }

    // Five bits to eight, with the top bits repeated in the low ones so
    // that all ones comes out as all ones rather than very nearly white.
    private static uint Spread(int value) => (uint)((value << 3) | (value >> 2));
}

using Rezrov.Grid;
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
    /// [zm 8] Draws the whole Z-machine screen onto the surface, in
    /// whatever colors the game has asked the screen to default to.
    /// </summary>
    public static void Screen(Surface surface, BufferedScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        Cells(
            surface,
            screen.Width,
            screen.Height,
            (row, column) => screen[row, column],
            screen.Cursor,
            Pixel(screen.DefaultForeground, Surface.White),
            Pixel(screen.DefaultBackground, Surface.Black));
    }

    /// <summary>
    /// Draws a grid of cells that some other machine keeps, in the
    /// colors this program draws in where the cells name none.
    /// </summary>
    /// <remarks>
    /// [aam output] The Aa-machine has no default colors of its own to
    /// ask for, since its style sheet names the color of everything it
    /// cares about and says nothing about the page behind, so the page
    /// is this program's to choose.
    /// </remarks>
    public static void Picture(Surface surface, IGridPicture picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        Cells(
            surface,
            picture.Width,
            picture.Height,
            (row, column) => picture[row, column],
            picture.Cursor,
            Surface.White,
            Surface.Black);
    }

    /// <summary>
    /// The drawing itself, which is the same whichever machine filled
    /// the cells.
    /// </summary>
    private static void Cells(
        Surface surface,
        int width,
        int height,
        Func<int, int, Cell> at,
        (int Row, int Column)? cursor,
        uint ink,
        uint paper)
    {
        ArgumentNullException.ThrowIfNull(surface);

        // [zm 8.4] A window is rarely a whole number of characters
        // across and down, so a strip is left over at the right and the
        // bottom. It is the screen's own color, not a border, or the
        // game appears to sit on a sheet of paper a size too small.
        surface.Fill(paper);

        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                Character(surface, at(row, column), row, column, ink, paper);
            }
        }

        if (cursor is { } place)
        {
            Cursor(surface, place, ink);
        }
    }

    /// <summary>
    /// One cell: its background, and then whatever character is in it.
    /// </summary>
    private static void Character(Surface surface, Cell cell, int row, int column, uint plain, uint page)
    {
        var attributes = cell.Attributes;

        // [zm 8.7.1] Reverse video swaps the two colors for this cell
        // only, which is how a status line is drawn.
        var reversed = attributes.Style.HasFlag(TextStyle.ReverseVideo);
        var ink = Pixel(reversed ? attributes.Background : attributes.Foreground, reversed ? page : plain);
        var paper = Pixel(reversed ? attributes.Foreground : attributes.Background, reversed ? plain : page);

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
    private static void Cursor(Surface surface, (int Row, int Column) cursor, uint ink)
    {
        surface.Fill(
            cursor.Column * CellWidth,
            ((cursor.Row + 1) * CellHeight) - 2,
            CellWidth,
            2,
            ink);
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

using Rezrov.Core.Graphics;
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
    public static void Screen(Surface surface, BufferedScreen screen, Action<Surface>? behind = null)
    {
        ArgumentNullException.ThrowIfNull(screen);

        Cells(
            surface,
            screen.Width,
            screen.Height,
            (row, column) => screen[row, column],
            screen.Cursor,
            Pixel(screen.DefaultForeground, Surface.White),
            Pixel(screen.DefaultBackground, Surface.Black),
            behind);
    }

    /// <summary>
    /// [zm 8.8.6] The pictures a Version 6 game has drawn, each where
    /// it put it.
    /// </summary>
    /// <remarks>
    /// [zm 8.8.1] The placement carries the cells it covers and where
    /// it really is in units, and a unit is a pixel here, so the unit
    /// rectangle is the one to use. Rounding to whole characters would
    /// move artwork drawn to the pixel by as much as a character,
    /// which on these games is plainly visible.
    /// </remarks>
    public static void Pictures(
        Surface surface,
        IReadOnlyList<PicturePlacement> placements,
        GridPictures pictures)
    {
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(pictures);

        foreach (var placement in placements)
        {
            if (pictures.Decode(placement.Number, placement.Palette) is not { } drawn)
            {
                continue;
            }

            Artwork(surface, drawn, placement.UnitLeft, placement.UnitTop, placement.UnitWidth, placement.UnitHeight);
        }
    }

    /// <summary>
    /// Draws a picture into a rectangle of the surface, taking the
    /// nearest pixel of it for each pixel drawn.
    /// </summary>
    /// <remarks>
    /// [blorb 2.3] A picture may be asked for at a size other than its
    /// own, and these games do ask: the MCGA artwork is 320 by 200 in
    /// a 640 by 400 screen, so every picture is drawn at twice its
    /// size. Taking the nearest pixel is what keeps it the pixel art
    /// it is.
    ///
    /// A pixel that is not fully opaque is left alone rather than
    /// blended, because nothing here keeps a partly transparent pixel:
    /// the decoders give a picture's own colors or nothing at all.
    /// </remarks>
    public static void Artwork(Surface surface, Pixels picture, int left, int top, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(picture);

        // A picture asked for at no size at all, which a game may do
        // and which would otherwise divide by nothing.
        if (width <= 0 || height <= 0)
        {
            return;
        }

        for (var y = 0; y < height; y++)
        {
            var down = top + y;

            if (down < 0 || down >= surface.Height)
            {
                continue;
            }

            var from = y * picture.Height / height;

            for (var x = 0; x < width; x++)
            {
                var (red, green, blue, alpha) = picture.At(x * picture.Width / width, from);

                if (alpha != 0)
                {
                    surface.Set(left + x, down, (uint)((red << 16) | (green << 8) | blue));
                }
            }
        }
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
        uint paper,
        Action<Surface>? behind = null)
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
                Background(surface, at(row, column), row, column, ink, paper);
            }
        }

        // [zm 8.8.6] Whatever goes over the backgrounds and under the
        // text, which is the order a Version 6 game draws in: it
        // paints a picture and then writes over it.
        behind?.Invoke(surface);

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
    /// One cell's background, where it is not the color the whole
    /// screen was already filled with.
    /// </summary>
    /// <remarks>
    /// Leaving the rest alone is what lets a picture show through the
    /// blank cells over it, and it saves filling most of the screen
    /// twice into the bargain.
    /// </remarks>
    private static void Background(Surface surface, Cell cell, int row, int column, uint plain, uint page)
    {
        var (_, paper) = Colors(cell, plain, page);

        if (paper != page)
        {
            surface.Fill(column * CellWidth, row * CellHeight, CellWidth, CellHeight, paper);
        }
    }

    /// <summary>
    /// [zm 8.7.1] The two colors a cell is drawn in, which reverse
    /// video swaps for that cell only and nothing else.
    /// </summary>
    private static (uint Ink, uint Paper) Colors(Cell cell, uint plain, uint page)
    {
        var attributes = cell.Attributes;
        var reversed = attributes.Style.HasFlag(TextStyle.ReverseVideo);

        return (
            Pixel(reversed ? attributes.Background : attributes.Foreground, reversed ? page : plain),
            Pixel(reversed ? attributes.Foreground : attributes.Background, reversed ? plain : page));
    }

    /// <summary>
    /// Whatever character is in one cell, over the background that
    /// has already been laid down for it.
    /// </summary>
    private static void Character(Surface surface, Cell cell, int row, int column, uint plain, uint page)
    {
        var attributes = cell.Attributes;
        var (ink, _) = Colors(cell, plain, page);

        var left = column * CellWidth;
        var top = row * CellHeight;

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

namespace Rezrov.Gui;

/// <summary>
/// How large a window to open a game in, counted in the characters the
/// screen is made of rather than in pixels.
/// </summary>
/// <remarks>
/// Characters are the right unit because characters are what the window
/// holds: [zm 8.4] a Z-machine screen is a grid of them and [glk
/// #window_arrangement] a Glk layout divides the display into them, so
/// a size that is not a whole number of them leaves a strip over.
/// </remarks>
public static class Screenful
{
    /// <summary>
    /// How wide a page of text the window opens to. Wide, but still a
    /// readable measure for prose.
    /// </summary>
    public const int Columns = 120;

    /// <summary>
    /// How tall: a screenful of text under [zm 8.8.6] the artwork a
    /// Version 6 game draws above it.
    /// </summary>
    public const int Rows = 40;

    /// <summary>
    /// The characters that fit in the space available, in the shape the
    /// artwork was drawn for where the game names one.
    /// </summary>
    /// <param name="width">How wide the window may be, in pixels.</param>
    /// <param name="height">How tall it may be.</param>
    /// <param name="cell">How wide one character is.</param>
    /// <param name="line">How tall one character is.</param>
    /// <param name="shape">
    /// [blorb 11.2] The width of the screen the pictures were drawn
    /// for, divided by its height, or null for a game that says nothing
    /// about it.
    /// </param>
    public static (int Columns, int Rows) Fit(double width, double height, double cell, double line, double? shape)
    {
        var columns = Math.Max((int)(width / cell), 1);
        var rows = Math.Max((int)(height / line), 1);

        return shape is { } wanted and > 0 ? Shaped(columns, rows, wanted, cell, line) : (columns, rows);
    }

    /// <summary>
    /// A window size the display can give exactly, so that the window
    /// opens with all the room it was asked for.
    /// </summary>
    /// <remarks>
    /// A size is asked for in the toolkit's pixels, and the display
    /// gives it in its own, which are those multiplied by how far the
    /// display is scaled. At a scaling of 1.75, a window 1074 wide is
    /// 1879.5 of the display's pixels, and the half is cut off rather
    /// than rounded, so the window comes back 1073.71 wide. A window
    /// sized for 118 columns then holds 117.97 of them, and the game is
    /// told 117. So the size is rounded up to whole pixels of the
    /// display before it is asked for.
    ///
    /// The half pixel added after rounding up keeps the cut on the right
    /// side of the whole number: a size that comes to 1880 pixels when
    /// multiplied back out may come to 1879.9999999 in floating point,
    /// and cutting that off would lose the pixel all over again. Where a
    /// display rounds instead, the half costs at most one pixel more.
    /// </remarks>
    /// <param name="size">
    /// The width or height wanted, in the toolkit's pixels.
    /// </param>
    /// <param name="scaling">How far the display is scaled.</param>
    public static double Whole(double size, double scaling)
    {
        if (scaling <= 0)
        {
            return size;
        }

        // The small allowance stops a size that is already whole, give or
        // take the last bit of a double, from being rounded up a pixel.
        var pixels = Math.Ceiling((size * scaling) - 1e-6);

        return (pixels + 0.5) / scaling;
    }

    /// <summary>
    /// The same, brought into the shape the artwork was drawn for.
    /// </summary>
    /// <remarks>
    /// [blorb 11.2] The scaling rules keep each picture's own shape:
    /// the scale is whichever of the width and the height runs out
    /// first. In a window of a different shape that leaves a band of
    /// background along one edge, which is what a player sees when
    /// Shogun's title screen stops short of the bottom and its side
    /// panels stop with it.
    ///
    /// So whichever side is too long for the shape is brought in. A
    /// character cannot be divided, so this is done again where
    /// rounding down to whole characters has left the other side long,
    /// and it stops once neither is out by more than one character,
    /// which is as close as a grid of characters can come.
    /// </remarks>
    private static (int Columns, int Rows) Shaped(int columns, int rows, double shape, double cell, double line)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            var width = columns * cell;
            var height = rows * line;

            if (width > (height * shape) + cell)
            {
                columns = Math.Max((int)(height * shape / cell), 1);
            }
            else if (height > (width / shape) + line)
            {
                rows = Math.Max((int)(width / shape / line), 1);
            }
            else
            {
                break;
            }
        }

        return (columns, rows);
    }
}

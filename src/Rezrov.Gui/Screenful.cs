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

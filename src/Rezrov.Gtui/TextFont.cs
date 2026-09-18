namespace Rezrov.Gtui;

/// <summary>
/// The font this program sets ordinary text in: eight cells across and
/// sixteen down, drawn here rather than taken from anywhere.
/// </summary>
/// <remarks>
/// A frontend that owns its pixels has to answer what a letter looks
/// like, and the other three programs never do: the console and the
/// terminal leave it to whatever the terminal is set in, and the
/// graphical program asks the system for a typeface. This one carries
/// its own, so that nothing here needs a font from outside and the
/// shapes on the screen are all accounted for in the repository.
///
/// The cell is eight cells wide and sixteen tall. Letters occupy the
/// first seven columns and the eighth is the gap to the next character,
/// so text needs no spacing of its own. Rows 0 and 1 are clear above;
/// capitals and digits run from <see cref="Cap"/> down to the row above
/// <see cref="Baseline"/>; lowercase bodies start at <see cref="Body"/>
/// and sit on the same baseline; the five descenders reach row 13,
/// which leaves rows 14 and 15 clear for the line below. Stems are one
/// cell wide throughout. [zm 16.1] Font 3 is a separate matter and
/// comes from the standard rather than from here.
///
/// The shapes were drawn by eye, which no test can check, so the
/// program prints the whole font on request and the way to judge it is
/// to look at it. What the tests can hold is everything measurable:
/// that each character has a shape at all, that every letter which
/// should sit on the baseline does, and that nothing strays into the
/// gap column and runs into the next character.
/// </remarks>
public static class TextFont
{
    /// <summary>The cells across one character.</summary>
    public const int Width = 8;

    /// <summary>The rows down one character.</summary>
    public const int Height = 16;

    /// <summary>
    /// The gap to the next character, which is the last column of the
    /// cell and is always clear.
    /// </summary>
    public const int Gap = Width - 1;

    /// <summary>The top row of a capital.</summary>
    public const int Cap = 2;

    /// <summary>The top row of a lowercase body.</summary>
    public const int Body = 5;

    /// <summary>
    /// The first row below the baseline, which only the descenders of
    /// g, j, p, q and y and the low punctuation reach into.
    /// </summary>
    public const int Baseline = 11;

    /// <summary>The first character the font draws.</summary>
    public const char First = ' ';

    /// <summary>The last character the font draws.</summary>
    public const char Last = '~';

    private static readonly byte[] Cells = Draw();

    private static readonly byte[] Nothing = new byte[Height];

    /// <summary>
    /// The sixteen rows of a character, each row a byte whose top bit
    /// is its leftmost cell, which is the same shape as
    /// <see cref="Rezrov.ZMachine.Screen.CharacterGraphics.Bitmap"/> so
    /// that one piece of drawing code serves both fonts. A character
    /// the font does not draw comes back blank.
    /// </summary>
    public static ReadOnlySpan<byte> Bitmap(char character) =>
        character is >= First and <= Last
            ? Cells.AsSpan((character - First) * Height, Height)
            : Nothing;

    /// <summary>
    /// The font itself. Each character names the row its drawing starts
    /// on and then the rows themselves, a hash for a cell that is on,
    /// which is how it was drawn and how it is read back.
    /// </summary>
    private static byte[] Draw()
    {
        var cells = new byte[(Last - First + 1) * Height];

        Draw(cells, ' ', 0);
        Draw(cells, '!', Cap, "...#...", "...#...", "...#...", "...#...", "...#...", "...#...", ".......", "...#...", "...#...");
        Draw(cells, '"', Cap, "..#.#..", "..#.#..", "..#.#..");
        Draw(cells, '#', Cap, "..#.#..", "..#.#..", "#######", "..#.#..", "..#.#..", "..#.#..", "#######", "..#.#..", "..#.#..");
        Draw(cells, '$', Cap, "...#...", ".#####.", "#..#..#", "#..#...", ".#####.", "...#..#", "#..#..#", ".#####.", "...#...");
        Draw(cells, '%', Cap, "##....#", "##...#.", "....#..", "...#...", "..#....", ".#....#", "#....##", "....###", ".....##");
        Draw(cells, '&', Cap, "..###..", ".#...#.", ".#...#.", "..###..", ".#.#...", "#...#.#", "#....#.", "#...#.#", ".###..#");
        Draw(cells, '\'', Cap, "...#...", "...#...", "...#...");
        Draw(cells, '(', Cap, "....#..", "...#...", "..#....", "..#....", "..#....", "..#....", "..#....", "...#...", "....#..");
        Draw(cells, ')', Cap, "..#....", "...#...", "....#..", "....#..", "....#..", "....#..", "....#..", "...#...", "..#....");
        Draw(cells, '*', Cap + 1, "...#...", "#..#..#", ".#.#.#.", "..###..", ".#.#.#.", "#..#..#", "...#...");
        Draw(cells, '+', Cap + 2, "...#...", "...#...", "#######", "...#...", "...#...");
        Draw(cells, ',', Baseline - 2, "..##...", "..##...", "...#...", "..#....");
        Draw(cells, '-', Cap + 4, "#######");
        Draw(cells, '.', Baseline - 2, "..##...", "..##...");
        Draw(cells, '/', Cap, "......#", ".....#.", "....#..", "....#..", "...#...", "..#....", "..#....", ".#.....", "#......");
        Draw(cells, '0', Cap, ".#####.", "#.....#", "#....##", "#...#.#", "#..#..#", "#.#...#", "##....#", "#.....#", ".#####.");
        Draw(cells, '1', Cap, "...#...", "..##...", ".#.#...", "...#...", "...#...", "...#...", "...#...", "...#...", ".#####.");
        Draw(cells, '2', Cap, ".#####.", "#.....#", "......#", ".....#.", "....#..", "...#...", "..#....", ".#.....", "#######");
        Draw(cells, '3', Cap, ".#####.", "#.....#", "......#", "......#", "..####.", "......#", "......#", "#.....#", ".#####.");
        Draw(cells, '4', Cap, "....##.", "...#.#.", "..#..#.", ".#...#.", "#....#.", "#######", ".....#.", ".....#.", ".....#.");
        Draw(cells, '5', Cap, "#######", "#......", "#......", "######.", "......#", "......#", "......#", "#.....#", ".#####.");
        Draw(cells, '6', Cap, "..####.", ".#.....", "#......", "#......", "######.", "#.....#", "#.....#", "#.....#", ".#####.");
        Draw(cells, '7', Cap, "#######", "......#", ".....#.", "....#..", "...#...", "...#...", "...#...", "...#...", "...#...");
        Draw(cells, '8', Cap, ".#####.", "#.....#", "#.....#", "#.....#", ".#####.", "#.....#", "#.....#", "#.....#", ".#####.");
        Draw(cells, '9', Cap, ".#####.", "#.....#", "#.....#", "#.....#", ".######", "......#", "......#", ".....#.", ".####..");
        Draw(cells, ':', Body, "..##...", "..##...", ".......", ".......", "..##...", "..##...");
        Draw(cells, ';', Body, "..##...", "..##...", ".......", ".......", "..##...", "..##...", "...#...", "..#....");
        Draw(cells, '<', Cap + 1, "....#..", "...#...", "..#....", ".#.....", "..#....", "...#...", "....#..");
        Draw(cells, '=', Cap + 3, "#######", ".......", ".......", "#######");
        Draw(cells, '>', Cap + 1, "..#....", "...#...", "....#..", ".....#.", "....#..", "...#...", "..#....");
        Draw(cells, '?', Cap, ".#####.", "#.....#", "......#", ".....#.", "...##..", "...#...", ".......", "...#...", "...#...");
        Draw(cells, '@', Cap, ".#####.", "#.....#", "#..####", "#.#...#", "#.#...#", "#.#...#", "#..####", "#......", ".#####.");
        Draw(cells, 'A', Cap, "..###..", ".#...#.", "#.....#", "#.....#", "#######", "#.....#", "#.....#", "#.....#", "#.....#");
        Draw(cells, 'B', Cap, "######.", "#.....#", "#.....#", "#.....#", "######.", "#.....#", "#.....#", "#.....#", "######.");
        Draw(cells, 'C', Cap, ".#####.", "#.....#", "#......", "#......", "#......", "#......", "#......", "#.....#", ".#####.");
        Draw(cells, 'D', Cap, "#####..", "#....#.", "#.....#", "#.....#", "#.....#", "#.....#", "#.....#", "#....#.", "#####..");
        Draw(cells, 'E', Cap, "#######", "#......", "#......", "#......", "#####..", "#......", "#......", "#......", "#######");
        Draw(cells, 'F', Cap, "#######", "#......", "#......", "#......", "#####..", "#......", "#......", "#......", "#......");
        Draw(cells, 'G', Cap, ".#####.", "#.....#", "#......", "#......", "#..####", "#.....#", "#.....#", "#.....#", ".#####.");
        Draw(cells, 'H', Cap, "#.....#", "#.....#", "#.....#", "#.....#", "#######", "#.....#", "#.....#", "#.....#", "#.....#");
        Draw(cells, 'I', Cap, ".#####.", "...#...", "...#...", "...#...", "...#...", "...#...", "...#...", "...#...", ".#####.");
        Draw(cells, 'J', Cap, "..#####", "....#..", "....#..", "....#..", "....#..", "....#..", "#...#..", "#...#..", ".###...");
        Draw(cells, 'K', Cap, "#....#.", "#...#..", "#..#...", "#.#....", "##.....", "#.#....", "#..#...", "#...#..", "#....#.");
        Draw(cells, 'L', Cap, "#......", "#......", "#......", "#......", "#......", "#......", "#......", "#......", "#######");
        Draw(cells, 'M', Cap, "#.....#", "##...##", "#.#.#.#", "#..#..#", "#.....#", "#.....#", "#.....#", "#.....#", "#.....#");
        Draw(cells, 'N', Cap, "#.....#", "##....#", "#.#...#", "#..#..#", "#...#.#", "#....##", "#.....#", "#.....#", "#.....#");
        Draw(cells, 'O', Cap, ".#####.", "#.....#", "#.....#", "#.....#", "#.....#", "#.....#", "#.....#", "#.....#", ".#####.");
        Draw(cells, 'P', Cap, "######.", "#.....#", "#.....#", "#.....#", "######.", "#......", "#......", "#......", "#......");
        Draw(cells, 'Q', Cap, ".#####.", "#.....#", "#.....#", "#.....#", "#.....#", "#.....#", "#..#..#", "#...#..", ".####.#");
        Draw(cells, 'R', Cap, "######.", "#.....#", "#.....#", "#.....#", "######.", "#..#...", "#...#..", "#....#.", "#.....#");
        Draw(cells, 'S', Cap, ".#####.", "#.....#", "#......", "#......", ".#####.", "......#", "......#", "#.....#", ".#####.");
        Draw(cells, 'T', Cap, "#######", "...#...", "...#...", "...#...", "...#...", "...#...", "...#...", "...#...", "...#...");
        Draw(cells, 'U', Cap, "#.....#", "#.....#", "#.....#", "#.....#", "#.....#", "#.....#", "#.....#", "#.....#", ".#####.");
        Draw(cells, 'V', Cap, "#.....#", "#.....#", "#.....#", "#.....#", ".#...#.", ".#...#.", "..#.#..", "..#.#..", "...#...");
        Draw(cells, 'W', Cap, "#.....#", "#.....#", "#.....#", "#.....#", "#..#..#", "#.#.#.#", "#.#.#.#", "##...##", "#.....#");
        Draw(cells, 'X', Cap, "#.....#", "#.....#", ".#...#.", "..#.#..", "...#...", "..#.#..", ".#...#.", "#.....#", "#.....#");
        Draw(cells, 'Y', Cap, "#.....#", ".#...#.", "..#.#..", "...#...", "...#...", "...#...", "...#...", "...#...", "...#...");
        Draw(cells, 'Z', Cap, "#######", "......#", ".....#.", "....#..", "...#...", "..#....", ".#.....", "#......", "#######");
        Draw(cells, '[', Cap, "..###..", "..#....", "..#....", "..#....", "..#....", "..#....", "..#....", "..#....", "..###..");
        Draw(cells, '\\', Cap, "#......", ".#.....", "..#....", "..#....", "...#...", "....#..", "....#..", ".....#.", "......#");
        Draw(cells, ']', Cap, "..###..", "....#..", "....#..", "....#..", "....#..", "....#..", "....#..", "....#..", "..###..");
        Draw(cells, '^', Cap, "...#...", "..#.#..", ".#...#.");
        Draw(cells, '_', Baseline + 1, "#######");
        Draw(cells, '`', Cap, "..#....", "...#...", "....#..");
        Draw(cells, 'a', Body, ".#####.", "......#", ".######", "#.....#", "#.....#", ".######");
        Draw(cells, 'b', Cap, "#......", "#......", "#......", "######.", "#.....#", "#.....#", "#.....#", "#.....#", "######.");
        Draw(cells, 'c', Body, ".#####.", "#.....#", "#......", "#......", "#.....#", ".#####.");
        Draw(cells, 'd', Cap, "......#", "......#", "......#", ".######", "#.....#", "#.....#", "#.....#", "#.....#", ".######");
        Draw(cells, 'e', Body, ".#####.", "#.....#", "#######", "#......", "#.....#", ".#####.");
        Draw(cells, 'f', Cap, "...###.", "..#....", "..#....", "#####..", "..#....", "..#....", "..#....", "..#....", "..#....");
        Draw(cells, 'g', Body, ".######", "#.....#", "#.....#", "#.....#", "#.....#", ".######", "......#", "#.....#", ".#####.");
        Draw(cells, 'h', Cap, "#......", "#......", "#......", "######.", "#.....#", "#.....#", "#.....#", "#.....#", "#.....#");
        Draw(cells, 'i', Cap, "...#...", ".......", ".......", "..##...", "...#...", "...#...", "...#...", "...#...", ".#####.");
        Draw(cells, 'j', Cap, "....#..", ".......", ".......", "....#..", "....#..", "....#..", "....#..", "....#..", "....#..", "....#..", "#...#..", ".###...");
        Draw(cells, 'k', Cap, "#......", "#......", "#......", "#....#.", "#...#..", "#.##...", "#...#..", "#....#.", "#.....#");
        Draw(cells, 'l', Cap, "..##...", "...#...", "...#...", "...#...", "...#...", "...#...", "...#...", "...#...", ".#####.");
        Draw(cells, 'm', Body, "##...#.", "#.#.#.#", "#.#.#.#", "#.#.#.#", "#.#.#.#", "#.#.#.#");
        Draw(cells, 'n', Body, "######.", "#.....#", "#.....#", "#.....#", "#.....#", "#.....#");
        Draw(cells, 'o', Body, ".#####.", "#.....#", "#.....#", "#.....#", "#.....#", ".#####.");
        Draw(cells, 'p', Body, "######.", "#.....#", "#.....#", "#.....#", "#.....#", "######.", "#......", "#......", "#......");
        Draw(cells, 'q', Body, ".######", "#.....#", "#.....#", "#.....#", "#.....#", ".######", "......#", "......#", "......#");
        Draw(cells, 'r', Body, "#.####.", "##....#", "#......", "#......", "#......", "#......");
        Draw(cells, 's', Body, ".######", "#......", ".#####.", "......#", "......#", "######.");
        Draw(cells, 't', Cap + 1, "..#....", "..#....", "#####..", "..#....", "..#....", "..#....", "..#..#.", "...##..");
        Draw(cells, 'u', Body, "#.....#", "#.....#", "#.....#", "#.....#", "#.....#", ".######");
        Draw(cells, 'v', Body, "#.....#", "#.....#", ".#...#.", ".#...#.", "..#.#..", "...#...");
        Draw(cells, 'w', Body, "#.....#", "#..#..#", "#.#.#.#", "#.#.#.#", "##...##", "#.....#");
        Draw(cells, 'x', Body, "#.....#", ".#...#.", "..#.#..", "..#.#..", ".#...#.", "#.....#");
        Draw(cells, 'y', Body, "#.....#", "#.....#", "#.....#", "#.....#", "#.....#", ".######", "......#", "......#", ".#####.");
        Draw(cells, 'z', Body, "#######", "....#..", "...#...", "..#....", ".#.....", "#######");
        Draw(cells, '{', Cap, "....##.", "...#...", "...#...", "...#...", "..#....", "...#...", "...#...", "...#...", "....##.");
        Draw(cells, '|', Cap, "...#...", "...#...", "...#...", "...#...", "...#...", "...#...", "...#...", "...#...", "...#...");
        Draw(cells, '}', Cap, "..##...", "...#...", "...#...", "...#...", "....#..", "...#...", "...#...", "...#...", "..##...");
        Draw(cells, '~', Cap + 3, ".##...#", "#..#..#", "#...##.");

        return cells;
    }

    private static void Draw(byte[] cells, char character, int top, params string[] rows)
    {
        for (var row = 0; row < rows.Length; row++)
        {
            var bits = 0;

            for (var cell = 0; cell < rows[row].Length; cell++)
            {
                if (rows[row][cell] == '#')
                {
                    bits |= 1 << (Width - 1 - cell);
                }
            }

            cells[((character - First) * Height) + top + row] = (byte)bits;
        }
    }
}

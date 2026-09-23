using System.Text;

namespace Rezrov.ZMachine.Screen;

/// <summary>
/// The upper window: a grid of cells with a cursor, as wide as the
/// screen and as many lines high as the game last asked for.
/// </summary>
/// <remarks>
/// [zm 8.6.1.1] and [zm 8.7.2.1] The upper window has variable height
/// and the same width as the screen. [zm 8.6.1.1.1] Printing onto it
/// overlays whatever is there, and [zm 8.7.3.1] it never scrolls. That
/// makes it a plain grid, which is what this is. The rows are kept
/// beyond the current height too, up to the screen height, so that
/// shrinking and regrowing the window in Version 5 (which does not
/// clear on split) shows what was there before, as a real screen would.
/// </remarks>
public sealed class UpperWindow
{
    private Cell[][] _rows;

    internal UpperWindow(int width, int maxLines, TextAttributes blank)
    {
        Width = width;
        _rows = Make(width, maxLines, blank);
    }

    /// <summary>
    /// The width in characters, the same as the screen's.
    /// </summary>
    public int Width { get; private set; }

    /// <summary>
    /// Fits the window to a screen that has changed size, keeping the
    /// cells that still have somewhere to be.
    /// </summary>
    /// <remarks>
    /// [zm 8.7.2.1] The upper window is as wide as the screen, so when
    /// the screen stops being one width it stops being that width too.
    /// Without this it kept whatever width the screen happened to have
    /// when the game started, forever: a window opened narrow and then
    /// widened left a Version 4 or later game, which draws its own
    /// status line up here, writing into a strip the width it first
    /// saw and unable to reach the rest of the screen ever again.
    ///
    /// What was on it is kept rather than cleared, because the game is
    /// not told to redraw and may not do so for several turns. A
    /// stale status line is worth more than an empty one.
    /// </remarks>
    internal void Fit(int width, int maxLines, TextAttributes blank)
    {
        if (width == Width && maxLines == _rows.Length)
        {
            return;
        }

        var rows = Make(width, maxLines, blank);

        for (var row = 0; row < Math.Min(maxLines, _rows.Length); row++)
        {
            for (var column = 0; column < Math.Min(width, Width); column++)
            {
                rows[row][column] = _rows[row][column];
            }
        }

        _rows = rows;
        Width = width;
        Lines = Math.Min(Lines, maxLines);
        CursorRow = Math.Clamp(CursorRow, 1, Math.Max(maxLines, 1));
        CursorColumn = Math.Clamp(CursorColumn, 1, Math.Max(width, 1));
    }

    private static Cell[][] Make(int width, int maxLines, TextAttributes blank)
    {
        var rows = new Cell[maxLines][];

        for (var row = 0; row < maxLines; row++)
        {
            rows[row] = new Cell[width];
            Array.Fill(rows[row], Cell.Blank(blank));
        }

        return rows;
    }

    /// <summary>
    /// [zm 8.7.2.1] The current height in lines, 0 when unsplit.
    /// </summary>
    public int Lines { get; internal set; }

    /// <summary>
    /// [zm 8.7.2.3.1] The cursor row, counted from 1 at the top.
    /// </summary>
    public int CursorRow { get; internal set; } = 1;

    /// <summary>
    /// [zm 8.7.2.3.1] The cursor column, counted from 1 at the left.
    /// </summary>
    public int CursorColumn { get; internal set; } = 1;

    /// <summary>The cell at a 1-based row and column.</summary>
    public Cell this[int row, int column] => _rows[row - 1][column - 1];

    /// <summary>The characters of a row as a string, for display.</summary>
    public string RowText(int row)
    {
        var builder = new StringBuilder(Width);
        foreach (var cell in _rows[row - 1])
        {
            builder.Append(cell.Character);
        }

        return builder.ToString();
    }

    internal void Put(int row, int column, Cell cell) => _rows[row - 1][column - 1] = cell;

    internal void Clear(TextAttributes attributes)
    {
        foreach (var row in _rows)
        {
            Array.Fill(row, Cell.Blank(attributes));
        }
    }

    internal void ClearRow(int row, int fromColumn, TextAttributes attributes) =>
        Array.Fill(_rows[row - 1], Cell.Blank(attributes), fromColumn - 1, Width - fromColumn + 1);
}

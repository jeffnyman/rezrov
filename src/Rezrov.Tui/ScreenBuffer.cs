using Rezrov.ZMachine.Screen;

namespace Rezrov.Tui;

/// <summary>
/// The terminal's picture of the screen: a grid of cells holding the
/// status line, the upper window, and the scrolling lower window.
/// </summary>
/// <remarks>
/// The screen model keeps the upper window's cells and streams the
/// lower window as styled runs with the line breaks already decided.
/// This turns both into one grid the size of the terminal, which is
/// what a terminal can paint: [zm 8.6.1.1] the status line on the top
/// row in Versions 1 to 3, [zm 8.7.2.1] the upper window on the rows
/// below it, and [zm 8.7.3.1] the lower window filling the rest and
/// scrolling upward when text reaches the bottom. It knows nothing of
/// Terminal.Gui, so it can be tested without a terminal.
/// </remarks>
public sealed class ScreenBuffer
{
    private Cell[][] _rows;
    private readonly TextAttributes _blank;

    public ScreenBuffer(int width, int height, TextAttributes blank, bool cursorStartsAtBottom)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Width = width;
        Height = height;
        _blank = blank;
        _rows = MakeRows(width, height, blank);
        CursorStartsAtBottom = cursorStartsAtBottom;
        CursorRow = cursorStartsAtBottom ? height - 1 : 0;
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>
    /// [zm 8.6.3] and [zm 8.7.3.2.1] Whether an erased lower window
    /// puts its cursor at the bottom, as Versions 1 to 4 do, so that
    /// text scrolls up from the start, or at the top, as Version 5
    /// does.
    /// </summary>
    public bool CursorStartsAtBottom { get; }

    /// <summary>
    /// [zm 8.6.1.1] Rows taken by the status line: 0 or 1.
    /// </summary>
    public int StatusRows { get; private set; }

    /// <summary>[zm 8.7.2.1] Rows taken by the upper window.</summary>
    public int UpperLines { get; private set; }

    /// <summary>The first row of the lower window.</summary>
    public int LowerTop => Math.Min(StatusRows + UpperLines, Height - 1);

    /// <summary>The lower window's cursor row, from 0.</summary>
    public int CursorRow { get; private set; }

    /// <summary>The lower window's cursor column, from 0.</summary>
    public int CursorColumn { get; private set; }

    /// <summary>
    /// [zm op:set_cursor] Whether the cursor is shown, which a Version
    /// 6 game can turn off.
    /// </summary>
    public bool CursorVisible { get; private set; } = true;

    /// <summary>The cell at a row and column, both from 0.</summary>
    public Cell this[int row, int column] => _rows[row][column];

    /// <summary>
    /// A row's characters as a string, for tests and display.
    /// </summary>
    public string RowText(int row) => string.Concat(_rows[row].Select(c => c.Character));

    /// <summary>
    /// Copies the status line and the upper window from the model, and
    /// keeps the lower window's cursor out of their way.
    /// </summary>
    public void UpdateUpper(ScreenModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        StatusRows = model.StatusLine is null ? 0 : 1;
        if (model.StatusLine is { } status)
        {
            CopyRow(0, status);
        }

        UpperLines = Math.Min(model.UpperWindow.Lines, Height - StatusRows);
        for (var line = 0; line < UpperLines; line++)
        {
            var cells = new Cell[Width];
            for (var column = 0; column < Width; column++)
            {
                cells[column] = column < model.UpperWindow.Width
                    ? model.UpperWindow[line + 1, column + 1]
                    : Cell.Blank(_blank);
            }

            CopyRow(StatusRows + line, cells);
        }

        // [zm 8.7.2.2] A split that would swallow the lower window's
        // cursor moves it to the line just below the upper window.
        if (CursorRow < LowerTop)
        {
            CursorRow = LowerTop;
            CursorColumn = 0;
        }
    }

    /// <summary>
    /// [zm 8.8] Takes the whole screen from the Version 6 model, which
    /// keeps every cell itself, and puts the cursor where its current
    /// window has it.
    /// </summary>
    public void UpdateWindows(WindowedScreenModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        StatusRows = 0;
        UpperLines = 0;

        for (var row = 0; row < Math.Min(Height, model.Height); row++)
        {
            var cells = new Cell[Width];
            for (var column = 0; column < Width; column++)
            {
                cells[column] = column < model.Width ? model[row, column] : Cell.Blank(_blank);
            }

            CopyRow(row, cells);
        }

        CursorRow = Math.Min(model.CursorRow, Height - 1);
        CursorColumn = Math.Min(model.CursorColumn, Width - 1);
        CursorVisible = model.CursorVisible;
    }

    /// <summary>Prints a run at the cursor, in the lower window.</summary>
    public void Print(string text, TextAttributes attributes)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var character in text)
        {
            if (CursorColumn >= Width)
            {
                NewLine();
            }

            _rows[CursorRow][CursorColumn] = new Cell(character, attributes);
            CursorColumn++;
        }
    }

    /// <summary>
    /// Ends the line: down one row, or [zm 8.7.3.1] a scroll of the
    /// lower window when the cursor is on the bottom row.
    /// </summary>
    public void NewLine()
    {
        CursorColumn = 0;
        if (CursorRow < Height - 1)
        {
            CursorRow++;
            return;
        }

        for (var row = LowerTop; row < Height - 1; row++)
        {
            _rows[row] = _rows[row + 1];
        }

        _rows[Height - 1] = BlankRow(Width, _blank);
    }

    /// <summary>
    /// Takes back the character before the cursor, for editing input.
    /// </summary>
    public void Backspace()
    {
        if (CursorColumn == 0)
        {
            return;
        }

        CursorColumn--;
        _rows[CursorRow][CursorColumn] = Cell.Blank(_blank);
    }

    /// <summary>
    /// [zm 8.7.3.2] Clears the lower window to the background.
    /// </summary>
    public void EraseLower(TextAttributes background)
    {
        for (var row = LowerTop; row < Height; row++)
        {
            _rows[row] = BlankRow(Width, background);
        }

        CursorRow = CursorStartsAtBottom ? Height - 1 : LowerTop;
        CursorColumn = 0;
    }

    /// <summary>
    /// [zm op:erase_line] Clears from the cursor to the end of its row.
    /// </summary>
    public void EraseToEndOfLine(TextAttributes background)
    {
        for (var column = CursorColumn; column < Width; column++)
        {
            _rows[CursorRow][column] = Cell.Blank(background);
        }
    }

    /// <summary>
    /// Fits the buffer to a new terminal size, keeping what fits and
    /// the cursor inside.
    /// </summary>
    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        var rows = MakeRows(width, height, _blank);
        for (var row = 0; row < Math.Min(height, Height); row++)
        {
            for (var column = 0; column < Math.Min(width, Width); column++)
            {
                rows[row][column] = _rows[row][column];
            }
        }

        _rows = rows;
        Width = width;
        Height = height;
        UpperLines = Math.Min(UpperLines, Math.Max(height - StatusRows, 0));
        CursorRow = Math.Clamp(CursorRow, LowerTop, height - 1);
        CursorColumn = Math.Min(CursorColumn, width - 1);
    }

    private void CopyRow(int row, IReadOnlyList<Cell> cells)
    {
        for (var column = 0; column < Width; column++)
        {
            _rows[row][column] = column < cells.Count ? cells[column] : Cell.Blank(_blank);
        }
    }

    private static Cell[][] MakeRows(int width, int height, TextAttributes blank)
    {
        var rows = new Cell[height][];
        for (var row = 0; row < height; row++)
        {
            rows[row] = BlankRow(width, blank);
        }

        return rows;
    }

    private static Cell[] BlankRow(int width, TextAttributes blank)
    {
        var row = new Cell[width];
        Array.Fill(row, Cell.Blank(blank));
        return row;
    }
}

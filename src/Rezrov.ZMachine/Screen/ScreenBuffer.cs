namespace Rezrov.ZMachine.Screen;

/// <summary>
/// A picture of the whole screen: a grid of cells holding the
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
/// any toolkit, so it can be tested without a terminal.
/// </remarks>
public sealed class ScreenBuffer
{
    /// <summary>
    /// How many of the lower window's paragraphs are kept so that the
    /// text can be laid out again at another width.
    /// </summary>
    /// <remarks>
    /// Enough to fill any window several times over, and bounded so
    /// that a long game does not carry every word it has ever printed.
    /// What falls off the end is text that scrolled away long ago.
    /// </remarks>
    private const int Kept = 400;

    private readonly List<List<Piece>> _said = [[]];

    private Cell[][] _rows;
    private readonly TextAttributes _blank;
    private StatusValues? _status;
    private TextAttributes _statusAttributes;

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
    /// [arc contract 3] Rows taken by the arc_image band across the
    /// top, 0 while there is no band. The band is not part of the
    /// Z-machine screen at all: the whole text model, status line and
    /// upper window included, lives strictly below it.
    /// </summary>
    public int BandRows { get; private set; }

    /// <summary>
    /// [zm 8.6.1.1] Rows taken by the status line: 0 or 1.
    /// </summary>
    public int StatusRows { get; private set; }

    /// <summary>[zm 8.7.2.1] Rows taken by the upper window.</summary>
    public int UpperLines { get; private set; }

    /// <summary>The first row of the lower window.</summary>
    public int LowerTop => Math.Min(BandRows + StatusRows + UpperLines, Height - 1);

    /// <summary>
    /// [arc contract 3] Gives the band the top <paramref name="rows"/>
    /// of the grid, or takes it down again with 0, and says whether
    /// anything changed.
    /// </summary>
    /// <remarks>
    /// Every row the band held before or holds now is blanked, and so
    /// are the status line and upper window rows, because a band that
    /// changes height moves both of those up or down the screen and
    /// whatever they painted stays behind otherwise. Leaving it behind
    /// shows as a second status line stranded where the band used to
    /// end. They are written again from the model on the next update,
    /// so blanking them costs nothing.
    ///
    /// Whether the text that was there survives is settled before this
    /// is called: [arc contract 3] says the re-base may not eat an
    /// unread line.
    /// </remarks>
    public bool SetBand(int rows)
    {
        rows = Math.Clamp(rows, 0, Math.Max(Height - 1, 0));

        if (rows == BandRows)
        {
            return false;
        }

        var stale = Math.Min(Math.Max(BandRows, rows) + StatusRows + UpperLines, Height);

        BandRows = rows;

        for (var row = 0; row < stale; row++)
        {
            _rows[row] = BlankRow(Width, _blank);
        }

        CursorRow = Math.Clamp(CursorRow, LowerTop, Math.Max(Height - 1, 0));
        return true;
    }

    /// <summary>
    /// The upper window's cursor as a row and column of this grid,
    /// while the upper window is the game's current window, or null
    /// while the lower window is. [zm 8.7.2.3] A game that reads in
    /// the upper window has put its cursor there first, and Infocom's
    /// interpreters showed the cursor there: Bureaucracy's form is
    /// filled in a field at a time, each at the cursor.
    /// </summary>
    public (int Row, int Column)? UpperCursor { get; private set; }

    /// <summary>
    /// Where the terminal's cursor belongs for input: the upper
    /// window's cursor while that window is current, otherwise the
    /// lower window's.
    /// </summary>
    public (int Row, int Column) InputCursor =>
        UpperCursor ?? (CursorRow, Math.Min(CursorColumn, Width - 1));

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

        // Kept so that the line can be arranged again if the screen
        // changes width, since the game is only asked for it once a
        // turn and would otherwise leave the score stranded at the old
        // right-hand edge until the player typed something.
        _status = model.Status;
        _statusAttributes = model.StatusAttributes;

        StatusRows = model.StatusLine is null ? 0 : 1;
        if (model.StatusLine is { } status)
        {
            // [arc contract 3] Below the band, which the status line
            // shares a screen with rather than sitting above.
            CopyRow(Math.Min(BandRows, Height - 1), status);
        }

        UpperLines = Math.Max(Math.Min(model.UpperWindow.Lines, Height - BandRows - StatusRows), 0);
        for (var line = 0; line < UpperLines; line++)
        {
            var cells = new Cell[Width];
            for (var column = 0; column < Width; column++)
            {
                cells[column] = column < model.UpperWindow.Width
                    ? model.UpperWindow[line + 1, column + 1]
                    : Cell.Blank(_blank);
            }

            CopyRow(BandRows + StatusRows + line, cells);
        }

        // [zm 8.7.2.2] A split that would swallow the lower window's
        // cursor moves it to the line just below the upper window.
        if (CursorRow < LowerTop)
        {
            CursorRow = LowerTop;
            CursorColumn = 0;
        }

        // The model's cursor counts from 1, this grid from 0, and the
        // status line sits above the upper window.
        UpperCursor = model.CurrentWindow == ScreenModel.Upper
            ? (Math.Min(BandRows + StatusRows + model.UpperWindow.CursorRow - 1, Height - 1),
               Math.Min(model.UpperWindow.CursorColumn - 1, Width - 1))
            : null;
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
        UpperCursor = null;
    }

    /// <summary>Prints a run at the cursor, in the lower window.</summary>
    public void Print(string text, TextAttributes attributes)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Kept as it was printed, unwrapped, so that the text can be
        // laid out again if the screen is ever a different width. The
        // cells below are this paragraph at the width it has now.
        if (text.Length > 0 && CursorRow >= LowerTop)
        {
            _said[^1].Add(new Piece(text, attributes));
        }

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
        // The game ended the line, so it is part of the text and is
        // kept. A break the width forced comes through WrapLine and is
        // deliberately not kept, because it is a fact about the width
        // rather than about the text.
        _said.Add([]);

        if (_said.Count > Kept)
        {
            _said.RemoveRange(0, _said.Count - Kept);
        }

        WrapLine();
    }

    /// <summary>
    /// [zm 7.2] Keeps a space that was not drawn because the line had
    /// no room for it.
    /// </summary>
    public void Swallow(TextAttributes attributes)
    {
        if (CursorRow >= LowerTop)
        {
            _said[^1].Add(new Piece(" ", attributes));
        }
    }

    /// <summary>
    /// Ends the line because the width ran out, leaving the paragraph
    /// it broke unfinished.
    /// </summary>
    public void WrapLine()
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

        // Taken back out of what was said as well, so that a line
        // being edited does not come back on a resize with the
        // characters the player already deleted.
        Unsay();
    }

    /// <summary>Drops the last character of the kept text.</summary>
    private void Unsay()
    {
        var paragraph = _said[^1];

        if (paragraph.Count == 0)
        {
            return;
        }

        var last = paragraph[^1];

        if (last.Text.Length <= 1)
        {
            paragraph.RemoveAt(paragraph.Count - 1);
            return;
        }

        paragraph[^1] = last with { Text = last.Text[..^1] };
    }

    /// <summary>
    /// [zm 8.7.3.2] Clears the lower window to the background.
    /// </summary>
    public void EraseLower(TextAttributes background)
    {
        // What was said is gone with the cells that showed it, or a
        // resize would bring a cleared screen back.
        _said.Clear();
        _said.Add([]);

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

        // [zm 8.6.1.1] Where the status line is, so that the one row
        // the interpreter draws itself can be carried across a widened
        // screen. Only needed where there is nothing to arrange the
        // line again from; with the game's own words kept, the row is
        // rebuilt below instead. The upper window is deliberately
        // neither: those rows belong to the game, which sized its own
        // window and will paint it again.
        var status = StatusRows > 0 && _status is null ? Math.Min(BandRows, Height - 1) : -1;

        for (var row = 0; row < Math.Min(height, Height); row++)
        {
            for (var column = 0; column < Math.Min(width, Width); column++)
            {
                rows[row][column] = _rows[row][column];
            }

            if (row != status)
            {
                continue;
            }

            // The status line was drawn to the old width and only the
            // next turn will draw it again. Left at the default, the
            // new columns are the ordinary page color, which on these
            // games is usually black, so widening the screen breaks
            // the band across the top with a gap until the player
            // types something. Carrying the last cell's own attributes
            // keeps the band whole. The attributes go over verbatim
            // rather than through Cell.Blank, which strips reverse
            // video, and reverse video is exactly what most of these
            // games make their status line out of.
            var carried = _rows[row][Width - 1].Attributes;

            for (var column = Width; column < width; column++)
            {
                rows[row][column] = new Cell(' ', carried);
            }
        }

        _rows = rows;
        Width = width;
        Height = height;
        UpperLines = Math.Min(UpperLines, Math.Max(height - StatusRows, 0));
        CursorRow = Math.Clamp(CursorRow, LowerTop, height - 1);
        CursorColumn = Math.Min(CursorColumn, width - 1);

        // The cells copied above were wrapped for the old width, and
        // narrowing has just cut the end off every one of them. The
        // text itself was kept, so the lower window is laid out again
        // from that instead. A screen with nothing said on it, which
        // is what a Version 6 game painting its own cells looks like
        // from here, is left exactly as it was.
        if (_said.Any(paragraph => paragraph.Count > 0))
        {
            Relay();
        }

        // [zm 8.2] And the status line is arranged again for the width
        // it now has, so the score goes back to the right-hand edge
        // rather than sitting where that edge used to be.
        if (StatusRows > 0 && _status is { } said)
        {
            CopyRow(
                Math.Min(BandRows, Height - 1),
                StatusBar.Compose(
                    said.Name,
                    said.TimeGame,
                    said.First,
                    said.Second,
                    Width,
                    _statusAttributes));
        }
    }

    /// <summary>
    /// A run of the lower window's text as the game printed it.
    /// </summary>
    private readonly record struct Piece(string Text, TextAttributes Attributes);

    /// <summary>
    /// Lays the kept text out again at the width the grid has now and
    /// puts the result back on the lower window's rows.
    /// </summary>
    /// <remarks>
    /// [zm 7.2] The rules are <see cref="LineFit"/>'s, the same ones
    /// the screen model applies as the game prints, so text laid out
    /// here and text that arrived at this width come out the same. A
    /// test holds them to that, because the two walking the rules
    /// separately is the one way this can go quietly wrong.
    ///
    /// Only the lower window. The status line and the upper window are
    /// cells somebody else owns and are left exactly as they were.
    /// </remarks>
    private void Relay() => Show(Lines());

    /// <summary>
    /// Lays the kept text out again and shows its end, which is where
    /// the game and the player both are.
    /// </summary>
    public void Settle() => Relay();

    /// <summary>
    /// The lower window's text as it stands on the screen now, line by
    /// line.
    /// </summary>
    /// <remarks>
    /// [arc contract 3] For a caller about to take rows away from the
    /// window and forbidden to lose a line doing it. What comes back
    /// is what the player can see and has not read, laid out at this
    /// width, so it can be shown again in a smaller window.
    /// </remarks>
    public IReadOnlyList<IReadOnlyList<Cell>> Page()
    {
        var lines = Lines();
        var visible = Math.Max(Height - LowerTop, 1);

        return lines.Count <= visible
            ? lines
            : lines.GetRange(lines.Count - visible, visible);
    }

    /// <summary>
    /// Puts a window-full of lines at the top of the lower window,
    /// starting at <paramref name="from"/>, and says how many it
    /// showed.
    /// </summary>
    /// <remarks>
    /// The top rather than the bottom, because this is for reading a
    /// page through from its start rather than for following a game
    /// that is writing. The cursor lands at the end of the last line
    /// shown, which is where a [MORE] belongs.
    /// </remarks>
    public int ShowFrom(IReadOnlyList<IReadOnlyList<Cell>> lines, int from)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var rows = Math.Max(Height - LowerTop, 1);

        for (var row = LowerTop; row < Height; row++)
        {
            _rows[row] = BlankRow(Width, _blank);
        }

        var shown = Math.Clamp(lines.Count - from, 0, rows);

        for (var i = 0; i < shown; i++)
        {
            var line = lines[from + i];

            for (var column = 0; column < line.Count && column < Width; column++)
            {
                _rows[LowerTop + i][column] = line[column];
            }
        }

        CursorRow = Math.Clamp(LowerTop + Math.Max(shown - 1, 0), LowerTop, Height - 1);
        CursorColumn = shown > 0 ? Math.Min(lines[from + shown - 1].Count, Width - 1) : 0;

        return shown;
    }

    /// <summary>
    /// The kept text laid out at the width the grid has now.
    /// </summary>
    private List<List<Cell>> Lines()
    {
        var lines = new List<List<Cell>>();
        var line = new List<Cell>();
        var word = new List<Cell>();

        void Place()
        {
            if (word.Count == 0)
            {
                return;
            }

            if (LineFit.Breaks(line.Count, word.Count, Width))
            {
                lines.Add(line);
                line = [];
            }

            foreach (var cell in word)
            {
                if (LineFit.Full(line.Count, Width))
                {
                    lines.Add(line);
                    line = [];
                }

                line.Add(cell);
            }

            word.Clear();
        }

        for (var paragraph = 0; paragraph < _said.Count; paragraph++)
        {
            foreach (var piece in _said[paragraph])
            {
                foreach (var character in piece.Text)
                {
                    if (character == ' ')
                    {
                        Place();

                        // A space at the right edge is swallowed, so
                        // that the next word starts the new line.
                        if (!LineFit.Full(line.Count, Width))
                        {
                            line.Add(new Cell(' ', piece.Attributes));
                        }
                    }
                    else
                    {
                        word.Add(new Cell(character, piece.Attributes));
                    }
                }
            }

            Place();

            // The last paragraph has not been ended by the game, so the
            // line it is on is the one the cursor is still sitting on.
            if (paragraph < _said.Count - 1)
            {
                lines.Add(line);
                line = [];
            }
        }

        lines.Add(line);

        return lines;
    }

    /// <summary>
    /// Puts laid-out lines on the lower window's rows, keeping the end
    /// of the text rather than the start, since that is where the game
    /// and the player both are.
    /// </summary>
    private void Show(List<List<Cell>> lines)
    {
        var rows = Height - LowerTop;

        for (var row = LowerTop; row < Height; row++)
        {
            _rows[row] = BlankRow(Width, _blank);
        }

        // Bottom of the window when the text fills it, and also when
        // the lower window is one that fills from the bottom, which is
        // how [zm 8.6.3] Versions 1 to 4 start.
        var top = lines.Count >= rows || CursorStartsAtBottom
            ? Height - Math.Min(lines.Count, rows)
            : LowerTop;

        var first = Math.Max(lines.Count - rows, 0);

        for (var i = first; i < lines.Count; i++)
        {
            var row = top + (i - first);
            for (var column = 0; column < lines[i].Count && column < Width; column++)
            {
                _rows[row][column] = lines[i][column];
            }
        }

        CursorRow = Math.Clamp(top + (lines.Count - 1 - first), LowerTop, Height - 1);
        CursorColumn = Math.Min(lines[^1].Count, Width - 1);
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

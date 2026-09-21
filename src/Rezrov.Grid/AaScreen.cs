using Rezrov.ZMachine.Screen;

namespace Rezrov.Grid;

/// <summary>
/// A grid of characters showing an Aa-machine story: a status area across
/// the top and the game's text below it.
/// </summary>
/// <remarks>
/// [aam output] The Aa-machine's output goes to a main area and to one
/// or more status areas. A grid can show the top one, which is
/// where every game in the corpus puts its location and its score, so
/// that is the one kept here. It is as many rows tall as its style
/// class asks for and is emptied each time the game enters it, which
/// is what the specification says entering means.
///
/// Both areas are <see cref="TextPane"/>s, so the wrapping, the
/// margins and the alignment are the same code that lays out a Glk
/// text buffer, and a change of screen size lays them out again.
/// </remarks>
public sealed class AaScreen
{
    private readonly TextAttributes _normal;

    private Cell[][] _rows;

    /// <param name="width">The screen's width in cells.</param>
    /// <param name="height">The screen's height in cells.</param>
    /// <param name="normal">
    /// The look of plain text and of the cells nothing has been
    /// written to.
    /// </param>
    public AaScreen(int width, int height, TextAttributes normal)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Width = width;
        Height = height;

        _normal = normal;
        _rows = MakeRows(width, height, normal);

        Main = new TextPane();
        Status = new TextPane();

        Main.Resize(width);
        Status.Resize(width);
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>The game's text.</summary>
    public TextPane Main { get; }

    /// <summary>What is in the top status area.</summary>
    public TextPane Status { get; }

    /// <summary>
    /// How many rows the status area takes. Zero until the game has
    /// entered one, so a game that never does gets the whole screen.
    /// </summary>
    public int StatusRows { get; set; }

    /// <summary>
    /// [aam output] Text set against the right of the status area, as
    /// a score is, kept apart from the rest because a grid of
    /// characters cannot flow text around a floated box.
    /// </summary>
    public string Floated { get; set; } = string.Empty;

    /// <summary>The look of the status area's text.</summary>
    public TextAttributes StatusAttributes { get; set; }

    /// <summary>
    /// The cell at a row and column, both counted from 0.
    /// </summary>
    public Cell this[int row, int column] => _rows[row][column];

    /// <summary>
    /// Where the cursor goes: the end of the game's text,
    /// which is where the player is typing.
    /// </summary>
    public (int Row, int Column) Cursor
    {
        get
        {
            var rows = Math.Max(Height - StatusRows, 1);
            var (row, column) = Main.Cursor(rows);

            return (Math.Min(StatusRows + row, Height - 1), Math.Min(column, Width - 1));
        }
    }

    /// <summary>A row's characters as a string, for tests.</summary>
    public string Row(int row)
    {
        var text = new char[Width];

        for (var column = 0; column < Width; column++)
        {
            text[column] = _rows[row][column].Character;
        }

        return new string(text).TrimEnd();
    }

    /// <summary>Lays both areas out onto the grid of cells.</summary>
    public void Paint()
    {
        foreach (var row in _rows)
        {
            Array.Fill(row, new Cell(' ', _normal));
        }

        var status = Math.Min(StatusRows, Height);

        if (status > 0)
        {
            PaintPane(Status, 0, status);
            PaintFloated(status);
        }

        PaintPane(Main, status, Height - status);
    }

    /// <summary>The screen changed size.</summary>
    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Width = width;
        Height = height;
        _rows = MakeRows(width, height, _normal);

        Main.Resize(width);
        Status.Resize(width);
    }

    private static Cell[][] MakeRows(int width, int height, TextAttributes normal)
    {
        var rows = new Cell[height][];

        for (var row = 0; row < height; row++)
        {
            rows[row] = new Cell[width];
            Array.Fill(rows[row], new Cell(' ', normal));
        }

        return rows;
    }

    private void PaintPane(TextPane pane, int top, int rows)
    {
        if (rows <= 0)
        {
            return;
        }

        var lines = pane.VisibleLines(rows);

        for (var i = 0; i < lines.Count && top + i < Height; i++)
        {
            var line = lines[i];

            for (var column = 0; column < line.Count && column < Width; column++)
            {
                _rows[top + i][column] = line[column].Cell;
            }
        }
    }

    // [aam output] The floated text sits at the right of the status
    // area's first row, which is where a score belongs and is the one
    // thing a floating box means on a grid of characters.
    private void PaintFloated(int status)
    {
        if (Floated.Length == 0 || status <= 0)
        {
            return;
        }

        var text = Floated.Length > Width ? Floated[..Width] : Floated;
        var start = Width - text.Length;

        for (var i = 0; i < text.Length; i++)
        {
            _rows[0][start + i] = new Cell(text[i], StatusAttributes);
        }
    }
}

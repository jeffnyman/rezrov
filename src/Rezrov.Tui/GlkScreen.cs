using Rezrov.Grid;
using Rezrov.Glulx.Glk;
using Rezrov.ZMachine.Screen;
using GlkWindowType = Rezrov.Glulx.Glk.WindowType;

namespace Rezrov.Tui;

/// <summary>
/// The terminal's picture of a Glk display: the window tree laid out
/// as rectangles on one grid of cells the size of the terminal.
/// </summary>
/// <remarks>
/// [glk #window_arrangement] The library keeps the tree, sizes every
/// window, and now places it too, so this only has to paint: a text
/// grid window's cells come straight from the library, a text buffer
/// window's from the <see cref="TextPane"/> kept for it here, a blank
/// window shows nothing, and a pair window is only the space its
/// children fill. It knows nothing of Terminal.Gui, so it can be
/// tested without a terminal. It paints when asked, once per frame,
/// since a text grid can change without the display hearing of it.
/// </remarks>
public sealed class GlkScreen
{
    private readonly Dictionary<GlkWindow, TextPane> _panes = [];
    private readonly HashSet<GlkWindow> _leaves = [];
    private readonly TextAttributes _normal;
    private Cell[][] _rows;
    private uint[][] _links;
    private GlkWindow? _root;

    /// <param name="width">The terminal's width in cells.</param>
    /// <param name="height">The terminal's height in cells.</param>
    /// <param name="normal">
    /// The attributes of plain text and of empty cells, which every
    /// style is a variation on.
    /// </param>
    public GlkScreen(int width, int height, TextAttributes normal)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Width = width;
        Height = height;
        _normal = normal;
        _rows = MakeRows(width, height, normal);
        _links = MakeLinks(width, height);
    }

    /// <summary>The color link text is shown in.</summary>
    public const ScreenColor LinkColor = ScreenColor.Cyan;

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>The root of the tree last laid out, or null.</summary>
    public GlkWindow? Root => _root;

    /// <summary>
    /// Cells painted over everything else, from a row and column: the
    /// line being typed into a text grid, whose cells belong to the
    /// library, or a prompt to the player. Null for none.
    /// </summary>
    public (int Row, int Column, IReadOnlyList<Cell> Cells)? Overlay { get; set; }

    /// <summary>
    /// The cell at a row and column, both from 0, as last painted.
    /// </summary>
    public Cell this[int row, int column] => _rows[row][column];

    /// <summary>
    /// [glk #link_creating] The link the character at a screen cell
    /// belongs to, zero where the text is not a link. Painted with the
    /// cells, so it is what the player sees at that spot.
    /// </summary>
    public uint LinkAt(int row, int column) =>
        row >= 0 && row < Height && column >= 0 && column < Width ? _links[row][column] : 0;

    /// <summary>
    /// [glk #window_arrangement] The window whose rectangle covers a
    /// screen cell, or null where no window does.
    /// </summary>
    public GlkWindow? WindowAt(int row, int column)
    {
        foreach (var window in Leaves(_root))
        {
            if (row >= window.Top && row < window.Top + window.Height &&
                column >= window.Left && column < window.Left + window.Width)
            {
                return window;
            }
        }

        return null;
    }

    /// <summary>
    /// A row's characters as a string, for tests and display.
    /// </summary>
    public string RowText(int row) => string.Concat(_rows[row].Select(c => c.Character));

    /// <summary>
    /// [glk #window_arrangement] Takes the tree as the library laid it
    /// out: text buffer windows new to the tree get a pane, windows
    /// gone from it lose theirs, and every pane wraps to its window's
    /// width.
    /// </summary>
    public void Arranged(GlkWindow? root)
    {
        _root = root;
        var leaves = Leaves(root).ToList();
        _leaves.Clear();
        _leaves.UnionWith(leaves);

        foreach (var gone in _panes.Keys.Where(w => !leaves.Contains(w)).ToList())
        {
            _panes.Remove(gone);
        }

        foreach (var window in leaves)
        {
            if (window.Type == GlkWindowType.TextBuffer)
            {
                if (!_panes.TryGetValue(window, out var pane))
                {
                    pane = new TextPane();
                    _panes[window] = pane;
                }

                pane.Resize(window.Width);
            }
        }
    }

    /// <summary>
    /// [glk #window_textbuf] A character printed to a text buffer
    /// window, in a style. Text grid windows keep their own cells, so
    /// nothing arrives here for them.
    /// </summary>
    public void Print(GlkWindow window, uint character, GlkStyle style, uint link = 0)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_panes.TryGetValue(window, out var pane))
        {
            pane.Put(ToChar(character), Attributes(style, _normal), link);
        }
    }

    /// <summary>
    /// Takes back the last character typed into a text buffer window.
    /// </summary>
    public void Backspace(GlkWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_panes.TryGetValue(window, out var pane))
        {
            pane.Backspace();
        }
    }

    /// <summary>
    /// How many lines a text buffer window's text wraps to, or zero
    /// for any other window.
    /// </summary>
    public int LineCount(GlkWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return _panes.TryGetValue(window, out var pane) ? pane.LineCount : 0;
    }

    /// <summary>[glk op:window_clear] A window was cleared.</summary>
    public void Clear(GlkWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_panes.TryGetValue(window, out var pane))
        {
            pane.Clear();
        }
    }

    /// <summary>
    /// Fits the grid to a new terminal size. The library lays the tree
    /// out again for the new size and says so through
    /// <see cref="Arranged"/>; until then the old rectangles are
    /// painted where they still fit.
    /// </summary>
    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Width = width;
        Height = height;
        _rows = MakeRows(width, height, _normal);
        _links = MakeLinks(width, height);
    }

    /// <summary>
    /// Where the cursor belongs for input in a window: the end of a text
    /// buffer's newest line, or a text grid's cursor, as a row and
    /// column of the whole screen, or null for a window that takes no
    /// input, has no room, or is no longer in the tree.
    /// </summary>
    public (int Row, int Column)? CursorFor(GlkWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!_leaves.Contains(window) || window.Width <= 0 || window.Height <= 0)
        {
            return null;
        }

        if (window is TextGridWindow grid)
        {
            return Clamp(window.Top + Math.Min(grid.CursorY, window.Height - 1), window.Left + Math.Min(grid.CursorX, window.Width - 1));
        }

        if (_panes.TryGetValue(window, out var pane))
        {
            var (row, column) = pane.Cursor(window.Height);
            return Clamp(window.Top + row, window.Left + Math.Min(column, window.Width - 1));
        }

        return null;
    }

    /// <summary>
    /// [glk #stream_style] How a Glk style looks on a terminal that has
    /// bold, italic, and reverse video and nothing else: emphasis is
    /// italic, headings and alerts are bold, input is bold, and the
    /// rest are plain.
    /// </summary>
    public static TextAttributes Attributes(GlkStyle style, TextAttributes normal) => style switch
    {
        GlkStyle.Emphasized => normal with { Style = normal.Style | TextStyle.Italic },
        GlkStyle.Header or GlkStyle.Subheader or GlkStyle.Alert or GlkStyle.Input => normal with { Style = normal.Style | TextStyle.Bold },
        GlkStyle.Preformatted => normal with { Style = normal.Style | TextStyle.FixedPitch },
        _ => normal,
    };

    private (int Row, int Column)? Clamp(int row, int column) =>
        row >= 0 && row < Height && column >= 0 && column < Width ? (row, column) : null;

    /// <summary>
    /// Paints every window onto the grid: blank first, then each leaf
    /// of the tree in its rectangle.
    /// </summary>
    public void Repaint()
    {
        foreach (var row in _rows)
        {
            Array.Fill(row, Cell.Blank(_normal));
        }

        foreach (var row in _links)
        {
            Array.Clear(row);
        }

        foreach (var window in Leaves(_root))
        {
            switch (window)
            {
                case TextGridWindow grid:
                    PaintGrid(grid);
                    break;
                case TextBufferWindow buffer when _panes.TryGetValue(buffer, out var pane):
                    PaintPane(buffer, pane);
                    break;
                default:
                    break;
            }
        }

        if (Overlay is { } overlay)
        {
            for (var i = 0; i < overlay.Cells.Count; i++)
            {
                Paint(overlay.Row, overlay.Column + i, overlay.Cells[i]);
            }
        }
    }

    private void PaintGrid(TextGridWindow grid)
    {
        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                Paint(grid.Top + y, grid.Left + x, new Cell(ToChar(grid.CharacterAt(x, y)), Attributes(grid.StyleAt(x, y), _normal)), grid.LinkAt(x, y));
            }
        }
    }

    private void PaintPane(GlkWindow window, TextPane pane)
    {
        var lines = pane.VisibleLines(window.Height);
        for (var y = 0; y < lines.Count; y++)
        {
            var line = lines[y];
            for (var x = 0; x < Math.Min(line.Count, window.Width); x++)
            {
                Paint(window.Top + y, window.Left + x, line[x].Cell, line[x].Link);
            }
        }
    }

    /// <summary>
    /// Sets a cell, or nothing when the layout put it off the screen,
    /// which can happen between a resize and the next arrangement.
    /// </summary>
    private void Paint(int row, int column, Cell cell, uint link = 0)
    {
        if (row < 0 || row >= Height || column < 0 || column >= Width)
        {
            return;
        }

        // [glk #link_creating] "The library will attempt to display
        // links in some distinctive way." A terminal cell has no
        // underline to give it, so a link takes a color of its own,
        // which is the other half of the convention.
        _rows[row][column] = link == 0
            ? cell
            : cell with { Attributes = cell.Attributes with { Foreground = LinkColor } };
        _links[row][column] = link;
    }

    /// <summary>
    /// The windows of the tree that are not pairs, in order.
    /// </summary>
    private static IEnumerable<GlkWindow> Leaves(GlkWindow? window)
    {
        if (window is null)
        {
            yield break;
        }

        if (window is PairWindow pair)
        {
            foreach (var leaf in Leaves(pair.First))
            {
                yield return leaf;
            }

            foreach (var leaf in Leaves(pair.Second))
            {
                yield return leaf;
            }
        }
        else
        {
            yield return window;
        }
    }

    /// <summary>
    /// A cell can hold one UTF-16 unit, so a character beyond the
    /// Basic Multilingual Plane, or a value that is no character, is
    /// shown as a question mark.
    /// </summary>
    private static char ToChar(uint character) =>
        character <= 0xFFFF && character is < 0xD800 or > 0xDFFF ? (char)character : '?';

    private static uint[][] MakeLinks(int width, int height)
    {
        var links = new uint[height][];
        for (var row = 0; row < height; row++)
        {
            links[row] = new uint[width];
        }

        return links;
    }

    private static Cell[][] MakeRows(int width, int height, TextAttributes normal)
    {
        var rows = new Cell[height][];
        for (var row = 0; row < height; row++)
        {
            rows[row] = new Cell[width];
            Array.Fill(rows[row], Cell.Blank(normal));
        }

        return rows;
    }
}

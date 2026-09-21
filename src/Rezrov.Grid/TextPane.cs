using Rezrov.ZMachine.Screen;

namespace Rezrov.Grid;

/// <summary>
/// The text of one Glk text buffer window, kept as paragraphs and
/// wrapped to the window's width when it is shown.
/// </summary>
/// <remarks>
/// [glk #window_textbuf] A text buffer is a stream of text: the
/// library hands it over one character at a time, and where the lines
/// break is up to the display. This keeps each paragraph as the run of
/// styled characters the game printed, breaks it into lines at word
/// boundaries for whatever width the window has now, and remembers the
/// lines of finished paragraphs so that showing the newest text is
/// cheap however long the buffer has grown. Wrapping again at a new
/// width is the same operation over all the paragraphs.
///
/// [aam output] A paragraph may also be set inside margins of its own
/// and centered or pushed right within them, which is what an
/// Aa-machine style class can ask for and what a Glk window never
/// does. At the default of no margins and no alignment the wrapping is
/// exactly what it was.
/// </remarks>
/// <summary>
/// [aam output] Where a paragraph's lines sit inside its margins.
/// </summary>
public enum PaneAlignment
{
    /// <summary>Against the left margin, which is the usual.</summary>
    Start,

    /// <summary>Centered between the margins.</summary>
    Center,

    /// <summary>Against the right margin.</summary>
    End,
}

public sealed class TextPane
{
    /// <summary>
    /// One character of a buffer: how it looks, and [glk
    /// #link_creating] the link it belongs to, zero for ordinary text.
    /// </summary>
    /// <param name="Cell">The character and its attributes.</param>
    /// <param name="Link">The link value, or zero.</param>
    public readonly record struct PaneCell(Cell Cell, uint Link);

    /// <summary>
    /// How many paragraphs are kept before the oldest go: enough to
    /// scroll back through a long session, not enough to grow without
    /// bound.
    /// </summary>
    public const int Scrollback = 2000;

    private readonly List<Paragraph> _paragraphs = [new Paragraph()];
    private int? _finishedLines;

    /// <summary>The width the paragraphs are wrapped to.</summary>
    public int Width { get; private set; }

    /// <summary>
    /// How many paragraphs there are, counting the open one.
    /// </summary>
    public int ParagraphCount => _paragraphs.Count;

    /// <summary>
    /// How many lines the whole buffer wraps to at the current width,
    /// counting the open paragraph's: what a display compares before
    /// and after to know how much is new.
    /// </summary>
    public int LineCount
    {
        get
        {
            _finishedLines ??= _paragraphs.Take(_paragraphs.Count - 1).Sum(p => Lines(p).Count);
            return _finishedLines.Value + Lines(Current).Count;
        }
    }

    /// <summary>The paragraph being printed to.</summary>
    private Paragraph Current => _paragraphs[^1];

    /// <summary>
    /// [aam output] Sets the margins and the alignment of the open
    /// paragraph, which hold for as long as it does. The margins are
    /// in characters and come off the width the paragraph wraps to.
    /// </summary>
    public void Layout(int left, int right, PaneAlignment alignment)
    {
        Current.Left = Math.Max(left, 0);
        Current.Right = Math.Max(right, 0);
        Current.Alignment = alignment;
        Current.Wrapped = null;
    }

    /// <summary>Wraps to a new width from now on.</summary>
    public void Resize(int width)
    {
        if (width != Width)
        {
            Width = Math.Max(width, 0);
            _finishedLines = null;
        }
    }

    /// <summary>
    /// Prints one character: a newline ends the paragraph, anything
    /// else joins it.
    /// </summary>
    public void Put(char character, TextAttributes attributes, uint link = 0)
    {
        if (character == '\n')
        {
            if (_finishedLines is { } finished)
            {
                _finishedLines = finished + Lines(Current).Count;
            }

            _paragraphs.Add(new Paragraph());
            if (_paragraphs.Count > Scrollback)
            {
                if (_finishedLines is { } counted)
                {
                    _finishedLines = counted - Lines(_paragraphs[0]).Count;
                }

                _paragraphs.RemoveAt(0);
            }

            return;
        }

        Current.Cells.Add(new PaneCell(new Cell(character, attributes), link));
        Current.Wrapped = null;
    }

    /// <summary>
    /// Takes back the last character of the open paragraph, for editing
    /// input; a finished paragraph is left alone.
    /// </summary>
    public void Backspace()
    {
        if (Current.Cells.Count > 0)
        {
            Current.Cells.RemoveAt(Current.Cells.Count - 1);
            Current.Wrapped = null;
        }
    }

    /// <summary>[glk op:window_clear] Forgets everything.</summary>
    public void Clear()
    {
        _paragraphs.Clear();
        _paragraphs.Add(new Paragraph());
        _finishedLines = 0;
    }

    /// <summary>
    /// The lines to show in a window <paramref name="height"/> rows
    /// tall: the last that many, so that the newest text is always in
    /// view, or all of them when there are fewer. An empty open
    /// paragraph is a line too, since that is where the cursor is.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<PaneCell>> VisibleLines(int height)
    {
        var lines = new List<IReadOnlyList<PaneCell>>();
        for (var i = _paragraphs.Count - 1; i >= 0 && lines.Count < height; i--)
        {
            var wrapped = Lines(_paragraphs[i]);
            for (var j = wrapped.Count - 1; j >= 0 && lines.Count < height; j--)
            {
                lines.Add(wrapped[j]);
            }
        }

        lines.Reverse();
        return lines;
    }

    /// <summary>
    /// Where the next character goes, as a row among the visible lines
    /// and a column: the end of the open paragraph's last line.
    /// </summary>
    public (int Row, int Column) Cursor(int height)
    {
        var visible = VisibleLines(height);
        var last = Lines(Current);
        return (Math.Max(visible.Count - 1, 0), last.Count == 0 ? 0 : last[^1].Count);
    }

    private List<IReadOnlyList<PaneCell>> Lines(Paragraph paragraph)
    {
        if (paragraph.Wrapped is null || paragraph.WrappedWidth != Width)
        {
            paragraph.Wrapped = Lay(paragraph, Width);
            paragraph.WrappedWidth = Width;
        }

        return paragraph.Wrapped;
    }

    // Wraps a paragraph inside its own margins and then puts each line
    // where its alignment asks. A paragraph with neither margins nor
    // alignment comes out of this exactly as it went in.
    private static List<IReadOnlyList<PaneCell>> Lay(Paragraph paragraph, int width)
    {
        var inside = width - paragraph.Left - paragraph.Right;
        var lines = Wrap(paragraph.Cells, Math.Max(inside, 1));

        if (paragraph.Left == 0 && paragraph.Alignment == PaneAlignment.Start)
        {
            return lines;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var slack = Math.Max(inside - line.Count, 0);

            var indent = paragraph.Left + paragraph.Alignment switch
            {
                PaneAlignment.Center => slack / 2,
                PaneAlignment.End => slack,
                _ => 0,
            };

            if (indent == 0)
            {
                continue;
            }

            // The blanks in front take the look of the text they are
            // in front of, which matters only when it is reversed.
            var attributes = line.Count > 0 ? line[0].Cell.Attributes : default;
            var laid = new List<PaneCell>(indent + line.Count);

            for (var blank = 0; blank < indent; blank++)
            {
                laid.Add(new PaneCell(new Cell(' ', attributes), 0));
            }

            laid.AddRange(line);
            lines[i] = laid;
        }

        return lines;
    }

    /// <summary>
    /// Breaks a paragraph into lines no wider than the width, at the
    /// last space that fits, or in the middle of a word that is wider
    /// than the window. The space a line breaks at is dropped, and an
    /// empty paragraph is one empty line.
    /// </summary>
    private static List<IReadOnlyList<PaneCell>> Wrap(List<PaneCell> cells, int width)
    {
        var lines = new List<IReadOnlyList<PaneCell>>();
        if (width <= 0)
        {
            lines.Add([]);
            return lines;
        }

        var start = 0;
        while (cells.Count - start > width)
        {
            var end = start + width;
            var breakAt = -1;
            for (var i = end; i > start; i--)
            {
                if (cells[i].Cell.Character == ' ')
                {
                    breakAt = i;
                    break;
                }
            }

            if (breakAt < 0)
            {
                lines.Add(cells.GetRange(start, width));
                start = end;
            }
            else
            {
                lines.Add(cells.GetRange(start, breakAt - start));
                start = breakAt + 1;
            }
        }

        lines.Add(cells.GetRange(start, cells.Count - start));
        return lines;
    }

    private sealed class Paragraph
    {
        public List<PaneCell> Cells { get; } = [];

        public List<IReadOnlyList<PaneCell>>? Wrapped { get; set; }

        public int WrappedWidth { get; set; }

        public int Left { get; set; }

        public int Right { get; set; }

        public PaneAlignment Alignment { get; set; }
    }
}

using System.Text;
using Rezrov.Glulx.Glk;

namespace Rezrov.Gui;

/// <summary>
/// A piece of a line, ready to be drawn: some text in one style, at a
/// place along the line.
/// </summary>
/// <param name="Text">The characters.</param>
/// <param name="Style">The style they are in.</param>
/// <param name="Link">
/// [glk #link_creating] The link they belong to, or zero for none.
/// </param>
/// <param name="Left">How far along the line the piece starts.</param>
/// <param name="Width">How wide it is.</param>
public sealed record Piece(string Text, GlkStyle Style, uint Link, double Left, double Width);

/// <summary>
/// One laid out line of a text buffer: the pieces across it, and how
/// tall it is.
/// </summary>
public sealed record Line(IReadOnlyList<Piece> Pieces, double Height);

/// <summary>
/// The text of one Glk text buffer window: what the game has printed,
/// and the lines it comes out as at a given width.
/// </summary>
/// <remarks>
/// [glk #window_textbuf] A text buffer is a stream of text, handed over
/// a character at a time as it is printed, that the library expects the
/// display to wrap, scroll, and keep. The text is kept here as runs of
/// one style, which is how it arrives and how it is drawn; the lines
/// are worked out from them whenever the width changes, since a window
/// that is made narrower has to wrap again from the top.
///
/// Wrapping breaks between words, and a word too long for a whole line
/// is broken where it reaches the edge rather than left to run off it.
/// </remarks>
public sealed class BufferText(IGlyphs glyphs)
{
    private readonly List<Run> _runs = [];
    private readonly IGlyphs _glyphs = glyphs;
    private List<Line> _lines = [];
    private double _width = -1;

    /// <summary>
    /// How many characters have been printed, which is where a mark can
    /// be put to find what arrived after it.
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    /// [glk #line_events] The line the player is typing, which the
    /// display shows because the library does not: it is drawn after
    /// everything printed so far and is not part of the text.
    /// </summary>
    public string Pending
    {
        get;
        set
        {
            field = value;
            _width = -1;
            Scroll = 0;
        }
    }

    = "";

    /// <summary>
    /// [glk #window_textbuf] Adds a character in a style, as part of a
    /// link or of none.
    /// </summary>
    public void Put(uint character, GlkStyle style, uint link)
    {
        var text = GlkText.ToString(character);
        if (text.Length == 0)
        {
            return;
        }

        // A run holds as much as it can: the text arrives one character
        // at a time, and a run for each would be thousands of them.
        if (_runs.Count > 0 && _runs[^1].Style == style && _runs[^1].Link == link)
        {
            _runs[^1].Text.Append(text);
        }
        else
        {
            _runs.Add(new Run(new StringBuilder(text), style, link));
        }

        Length += text.Length;
        _width = -1;

        // Something new to read means the player wants to see it, so
        // anything they had scrolled back to is left behind.
        Scroll = 0;
    }

    /// <summary>[glk op:window_clear] Throws all of it away.</summary>
    public void Clear()
    {
        _runs.Clear();
        _lines = [];
        Length = 0;
        _width = -1;
        Scroll = 0;
    }

    /// <summary>
    /// The lines the text comes out as at the given width, worked out
    /// again only when the width or the text has changed.
    /// </summary>
    public IReadOnlyList<Line> Lines(double width)
    {
        if (Math.Abs(width - _width) < 0.01)
        {
            return _lines;
        }

        _width = width;
        _lines = Wrap(width);
        return _lines;
    }

    /// <summary>How tall all of it is at the given width.</summary>
    public double Height(double width)
    {
        var total = 0.0;
        foreach (var line in Lines(width))
        {
            total += line.Height;
        }

        return total;
    }

    /// <summary>
    /// How far back the player has scrolled, in pixels, with zero
    /// meaning the end of the text is in view.
    /// </summary>
    public double Scroll { get; private set; }

    /// <summary>
    /// The furthest back it is worth scrolling: enough to bring the
    /// first line into view and no further.
    /// </summary>
    public double Furthest(double width, double height) => Math.Max(Height(width) - height, 0);

    /// <summary>
    /// Moves the view back by so many pixels, or forward for a negative
    /// number, as far as there is text to see.
    /// </summary>
    public void ScrollBy(double pixels, double width, double height) =>
        Scroll = Math.Clamp(Scroll + pixels, 0, Furthest(width, height));

    /// <summary>Brings the end of the text back into view.</summary>
    public void ScrollToEnd() => Scroll = 0;

    /// <summary>
    /// Where the bottom of the last line sits, measured down from the
    /// top of a window of the given height.
    /// </summary>
    /// <remarks>
    /// [glk #window_textbuf] While the text fits, it starts at the top
    /// of the window and the space below it is empty, as a page of text
    /// does. Once there is more of it than fits, the last line sits
    /// against the bottom edge and the earlier part runs off the top,
    /// so that what the game printed last is always the part in view.
    ///
    /// Scrolling back moves the whole of it down, which brings the
    /// earlier part into view and takes the last line off the bottom.
    /// The amount is clamped here as well as where it is set, since a
    /// window made taller can leave a scroll that was reasonable at the
    /// old size further back than there is now text to show.
    /// </remarks>
    public double Bottom(double width, double height) =>
        Math.Min(height, Height(width)) + Math.Clamp(Scroll, 0, Furthest(width, height));

    private List<Line> Wrap(double width)
    {
        var lines = new List<Line>();
        var pieces = new List<Piece>();
        var left = 0.0;
        var height = 0.0;

        // [glk #line_events] The line being typed goes on the end, in the
        // style Glk keeps for it, so it wraps with the text before it.
        var showing = Pending.Length == 0
            ? _runs
            : [.. _runs, new Run(new StringBuilder(Pending), GlkStyle.Input, 0)];

        foreach (var run in showing)
        {
            var lineHeight = _glyphs.LineHeight(run.Style);

            foreach (var part in Split(run.Text.ToString()))
            {
                if (part == "\n")
                {
                    lines.Add(new Line(pieces, Math.Max(height, lineHeight)));
                    pieces = [];
                    left = 0;
                    height = 0;
                    continue;
                }

                var measured = _glyphs.Width(part, run.Style);

                // [glk #window_textbuf] A word that does not fit goes on
                // the next line, unless the line is empty, in which case
                // it is broken where it reaches the edge: something has
                // to give, and dropping characters is worse.
                if (left + measured > width && left > 0 && part != " ")
                {
                    lines.Add(new Line(pieces, Math.Max(height, lineHeight)));
                    pieces = [];
                    left = 0;
                    height = 0;
                }

                if (measured > width && width > 0)
                {
                    foreach (var (text, size) in Break(part, run.Style, width))
                    {
                        if (left + size > width && left > 0)
                        {
                            lines.Add(new Line(pieces, Math.Max(height, lineHeight)));
                            pieces = [];
                            left = 0;
                            height = 0;
                        }

                        pieces.Add(new Piece(text, run.Style, run.Link, left, size));
                        left += size;
                        height = Math.Max(height, lineHeight);
                    }

                    continue;
                }

                // A space that falls at the edge is kept where it is
                // rather than pushed to the next line, where it would
                // show as an indent.
                pieces.Add(new Piece(part, run.Style, run.Link, left, measured));
                left += measured;
                height = Math.Max(height, lineHeight);
            }
        }

        if (pieces.Count > 0 || lines.Count == 0)
        {
            lines.Add(new Line(pieces, height > 0 ? height : _glyphs.LineHeight(GlkStyle.Normal)));
        }

        return lines;
    }

    /// <summary>
    /// The text as words, the spaces between them, and the line breaks,
    /// each of which the wrapper treats differently.
    /// </summary>
    private static IEnumerable<string> Split(string text)
    {
        var word = new StringBuilder();

        foreach (var character in text)
        {
            if (character == '\n' || character == ' ')
            {
                if (word.Length > 0)
                {
                    yield return word.ToString();
                    word.Clear();
                }

                yield return character == '\n' ? "\n" : " ";
                continue;
            }

            word.Append(character);
        }

        if (word.Length > 0)
        {
            yield return word.ToString();
        }
    }

    /// <summary>
    /// Breaks a word too long for any line into pieces that fit, one
    /// character at a time so that a proportional font is measured
    /// rather than guessed at.
    /// </summary>
    private IEnumerable<(string Text, double Width)> Break(string word, GlkStyle style, double width)
    {
        var piece = new StringBuilder();
        var measured = 0.0;

        foreach (var character in word)
        {
            var one = _glyphs.Width(character.ToString(), style);
            if (piece.Length > 0 && measured + one > width)
            {
                yield return (piece.ToString(), measured);
                piece.Clear();
                measured = 0;
            }

            piece.Append(character);
            measured += one;
        }

        if (piece.Length > 0)
        {
            yield return (piece.ToString(), measured);
        }
    }

    private sealed record Run(StringBuilder Text, GlkStyle Style, uint Link);
}

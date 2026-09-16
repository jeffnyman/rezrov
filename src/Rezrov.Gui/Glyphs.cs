using System.Globalization;
using Avalonia.Media;
using Rezrov.Glulx.Glk;

namespace Rezrov.Gui;

/// <summary>
/// The fonts the frontend draws with, and the measuring the layout asks
/// of them.
/// </summary>
/// <remarks>
/// [glk #stream_style] Glk names eleven styles and leaves it to the
/// library to decide what each looks like. Two faces serve all of them
/// here: a proportional one for the prose a text buffer is made of, and
/// a fixed one for [glk #window_textgrid] text grids and for the
/// preformatted style, which a game uses precisely when it is counting
/// on characters lining up.
///
/// Measurements are kept, since a line of prose is measured word by
/// word and the same words come round again and again.
/// </remarks>
internal sealed class Glyphs : IGlyphs
{
    private readonly Dictionary<(string Text, GlkStyle Style), double> _widths = [];
    private readonly Typeface _body;
    private readonly Typeface _italic;
    private readonly Typeface _bold;
    private readonly Typeface _fixed;
    private readonly Typeface _fixedItalic;
    private readonly Typeface _fixedBold;
    private readonly double _size;

    /// <param name="size">The size of ordinary text, in pixels.</param>
    public Glyphs(double size = 16)
    {
        _size = size;

        // A list of families rather than one: the first that the
        // machine actually has is used, and the generic name at the end
        // is always there.
        var body = new FontFamily("Georgia, Palatino, Times New Roman, serif");
        var mono = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace");

        _body = new Typeface(body);
        _italic = new Typeface(body, FontStyle.Italic);
        _bold = new Typeface(body, FontStyle.Normal, FontWeight.Bold);
        _fixed = new Typeface(mono);
        _fixedItalic = new Typeface(mono, FontStyle.Italic);
        _fixedBold = new Typeface(mono, FontStyle.Normal, FontWeight.Bold);

        // [glk #window_textgrid] The cell of the fixed font, which the
        // library divides the whole display into. It is rounded to whole
        // pixels so that a grid of them lands on pixel boundaries and
        // the columns line up down the screen.
        var measured = Measure("M", _fixed, _size);
        CellWidth = Math.Max(Math.Round(measured.Width), 1);
        CellHeight = Math.Max(Math.Round(measured.Height), 1);
    }

    public double CellWidth { get; }

    public double CellHeight { get; }

    public double Width(string text, GlkStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (_widths.TryGetValue((text, style), out var known))
        {
            return known;
        }

        // The width a space takes is trailing whitespace as far as the
        // text layout is concerned, and Width leaves that out. Words are
        // measured one at a time here, so leaving it out would run every
        // word into the next.
        var width = Measure(text, Face(style), Size(style)).WidthIncludingTrailingWhitespace;
        _widths[(text, style)] = width;
        return width;
    }

    public double LineHeight(GlkStyle style) => Math.Ceiling(Measure("Mg", Face(style), Size(style)).Height);

    /// <summary>The face a style is drawn in.</summary>
    public Typeface Face(GlkStyle style) => style switch
    {
        // [glk #stream_style] Emphasis and the quoted styles lean; the
        // headings and the warnings are heavy; preformatted is the
        // fixed face, which is what makes it preformatted.
        GlkStyle.Emphasized or GlkStyle.Note or GlkStyle.BlockQuote => _italic,
        GlkStyle.Header or GlkStyle.Subheader or GlkStyle.Alert => _bold,
        GlkStyle.Preformatted => _fixed,
        _ => _body,
    };

    /// <summary>
    /// [glk #window_textgrid] The face a style is drawn in inside a text
    /// grid, which is fixed whatever the style, since a grid is a grid
    /// of cells and the characters have to line up in them.
    /// </summary>
    public Typeface Grid(GlkStyle style) => style switch
    {
        GlkStyle.Emphasized or GlkStyle.Note or GlkStyle.BlockQuote => _fixedItalic,
        GlkStyle.Header or GlkStyle.Subheader or GlkStyle.Alert => _fixedBold,
        _ => _fixed,
    };

    /// <summary>How large a style is drawn.</summary>
    public double Size(GlkStyle style) => style switch
    {
        GlkStyle.Header => _size * 1.4,
        GlkStyle.Subheader => _size * 1.15,
        _ => _size,
    };

    private static FormattedText Measure(string text, Typeface typeface, double size) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black);
}

using System.Globalization;
using Avalonia.Media;
using Rezrov.Glulx.Glk;

namespace Rezrov.Gui;

/// <summary>
/// The faces the frontend draws with and the measurements taken from
/// them, shared by every window.
/// </summary>
/// <remarks>
/// [glk #stream_style] Two families serve all eleven styles: a
/// proportional one for the prose a text buffer is made of, and a fixed
/// one for [glk #window_textgrid] text grids and for the preformatted
/// style, which a game uses precisely when it is counting on characters
/// lining up. Which family a style is drawn in, at what size, weight
/// and slant, is <see cref="GlkLook"/>'s to say.
///
/// Measurements are kept, since a line of prose is measured word by
/// word and the same words come round again and again.
/// </remarks>
internal sealed class Fonts
{
    private readonly Dictionary<(string Text, Typeface Face, double Size), double> _widths = [];
    private readonly Dictionary<(Typeface Face, double Size), (double Height, double Baseline)> _lines = [];
    private readonly FontFamily _body;
    private readonly FontFamily _mono;

    /// <param name="size">The size of ordinary text, in pixels.</param>
    /// <param name="prose">The family the prose is set in.</param>
    /// <param name="fixedWidth">The family fixed text is set in.</param>
    public Fonts(double size, string prose, string fixedWidth)
    {
        Size = size;

        // A list of families rather than one: the first that the
        // machine actually has is used, and the generic name at the end
        // is always there, which is why the defaults are written that
        // way and why a name a player gives is passed through as it
        // stands.
        _body = new FontFamily(prose);
        _mono = new FontFamily(fixedWidth);

        // [glk #window_textgrid] The cell of the fixed font, which the
        // library divides the whole display into. It is rounded to whole
        // pixels so that a grid of them lands on pixel boundaries and
        // the columns line up down the screen.
        var measured = Measure("M", Face(proportional: false, weight: 0, oblique: false), size);
        CellWidth = Math.Max(Math.Round(measured.Width), 1);
        CellHeight = Math.Max(Math.Round(measured.Height), 1);
    }

    public double Size { get; }

    public double CellWidth { get; }

    public double CellHeight { get; }

    /// <summary>
    /// [glk #stream_style_hints] The face an appearance calls for: one
    /// of the two families, at one of the three weights the hint has
    /// values for, upright or leaning.
    /// </summary>
    public Typeface Face(bool proportional, int weight, bool oblique) => new(
        proportional ? _body : _mono,
        oblique ? FontStyle.Italic : FontStyle.Normal,
        weight switch
        {
            > 0 => FontWeight.Bold,
            < 0 => FontWeight.Light,
            _ => FontWeight.Normal,
        });

    /// <summary>
    /// [aam story] The face a named family calls for, heavy or leaning
    /// as a style class asks. An Aa-machine story names its own
    /// families rather than choosing between two the frontend keeps,
    /// so the family comes in from outside.
    /// </summary>
    public static Typeface Face(FontFamily family, bool bold, bool italic) => new(
        family,
        italic ? FontStyle.Italic : FontStyle.Normal,
        bold ? FontWeight.Bold : FontWeight.Normal);

    public double Width(string text, Typeface face, double size)
    {
        if (_widths.TryGetValue((text, face, size), out var known))
        {
            return known;
        }

        // The width a space takes is trailing whitespace as far as the
        // text layout is concerned, and Width leaves that out. Words are
        // measured one at a time here, so leaving it out would run every
        // word into the next.
        var width = Measure(text, face, size).WidthIncludingTrailingWhitespace;
        _widths[(text, face, size)] = width;
        return width;
    }

    /// <summary>
    /// How tall a line of a face is and how far down its baseline sits.
    /// </summary>
    public (double Height, double Baseline) Line(Typeface face, double size)
    {
        if (_lines.TryGetValue((face, size), out var known))
        {
            return known;
        }

        var measured = Measure("Mg", face, size);
        var line = (Math.Ceiling(measured.Height), measured.Baseline);
        _lines[(face, size)] = line;
        return line;
    }

    private static FormattedText Measure(string text, Typeface typeface, double size) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black);
}

/// <summary>
/// The fonts as one window sees them: every measurement goes through
/// that window's own styles.
/// </summary>
/// <remarks>
/// [glk #stream_style_hints] A hint reaches only the windows opened
/// after it was set, so what a style looks like is a property of the
/// window rather than of the program. The faces and their measurements
/// are shared all the same, since two windows asking for the same face
/// at the same size want the same answer.
/// </remarks>
internal sealed class Glyphs : IGlyphs
{
    /// <summary>
    /// [glk #stream_style] The family a text buffer's prose is set in
    /// where the player names no other.
    /// </summary>
    public const string ProseFamily = "Georgia, Palatino, Times New Roman, serif";

    /// <summary>
    /// [glk #window_textgrid] The family a text grid and the
    /// preformatted style are set in, whose characters all have the
    /// same width, which is what a grid is made of.
    /// </summary>
    public const string FixedFamily = "Consolas, Menlo, DejaVu Sans Mono, monospace";

    /// <summary>The size of ordinary text, in pixels.</summary>
    public const double OrdinarySize = 16;

    private readonly Fonts _fonts;
    private readonly GlkLook _look;

    /// <param name="size">The size of ordinary text, in pixels.</param>
    /// <param name="prose">The family the prose is set in.</param>
    /// <param name="fixedWidth">The family fixed text is set in.</param>
    public Glyphs(double size = OrdinarySize, string prose = ProseFamily, string fixedWidth = FixedFamily)
    {
        _fonts = new Fonts(size, prose, fixedWidth);
        _look = new GlkLook(WindowType.TextBuffer, GlkStyles.None, size, _fonts.CellWidth);
    }

    private Glyphs(Fonts fonts, GlkLook look)
    {
        _fonts = fonts;
        _look = look;
    }

    /// <summary>
    /// [aam story] The faces themselves, for an Aa-machine story,
    /// which names its own families rather than choosing between the
    /// two a Glk game has.
    /// </summary>
    public Fonts Fonts => _fonts;

    public double CellWidth => _fonts.CellWidth;

    public double CellHeight => _fonts.CellHeight;

    public IGlyphs Bound(WindowType type, GlkStyles styles) =>
        new Glyphs(_fonts, new GlkLook(type, styles, _fonts.Size, _fonts.CellWidth));

    public GlkAppearance Look(GlkStyle style) => _look.Of(style);

    public double Width(string text, GlkStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);

        var look = _look.Of(style);
        return _fonts.Width(text, Face(look), look.Size);
    }

    public double LineHeight(GlkStyle style) => Line(style).Height;

    public double Baseline(GlkStyle style) => Line(style).Baseline;

    /// <summary>The face an appearance is drawn in.</summary>
    public Typeface Face(GlkAppearance look) =>
        _fonts.Face(look.Proportional, look.Weight, look.Oblique);

    /// <summary>How large a style is drawn, in pixels.</summary>
    public double Size(GlkStyle style) => _look.Of(style).Size;

    /// <summary>
    /// [zm 8.7] The fixed face, heavy or leaning as a Z-machine style
    /// asks. That machine's screen is a grid of cells and its styles
    /// are these, so it needs nothing else.
    /// </summary>
    public Typeface Fixed(bool bold, bool italic) =>
        _fonts.Face(proportional: false, weight: bold ? 1 : 0, oblique: italic);

    private (double Height, double Baseline) Line(GlkStyle style)
    {
        var look = _look.Of(style);
        return _fonts.Line(Face(look), look.Size);
    }
}

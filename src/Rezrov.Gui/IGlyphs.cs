using Rezrov.Glulx.Glk;

namespace Rezrov.Gui;

/// <summary>
/// What the frontend needs to know about the font it is drawing with:
/// how wide a piece of text comes out and how tall a line of it is.
/// </summary>
/// <remarks>
/// [glk #window_textbuf] A text buffer wraps its text at the window's
/// width, and with a proportional font only the font can say where a
/// line runs out. Measuring is the one thing the layout needs from the
/// drawing toolkit, so it is the only thing that crosses this seam; the
/// wrapping, the styles, and the scrolling above it are all ordinary
/// code that a test can drive with a font of its own invention.
/// </remarks>
public interface IGlyphs
{
    /// <summary>
    /// How wide a piece of text is, in pixels, in the given style.
    /// </summary>
    double Width(string text, GlkStyle style);

    /// <summary>
    /// How tall a line of text in the given style is, in pixels.
    /// </summary>
    double LineHeight(GlkStyle style);

    /// <summary>
    /// How far below the top of a line of the given style its baseline
    /// sits, in pixels.
    /// </summary>
    /// <remarks>
    /// A line is laid out around its baseline rather than around its
    /// top edge, so that a heading and the prose beside it sit on the
    /// same line instead of hanging from the same top. [glk
    /// #graphics_textbuf] The inline alignments of a picture are
    /// defined against the baseline and the top of the line as well,
    /// which is the other reason the layout has to know where it is.
    ///
    /// A font that cannot say gives four fifths of the way down, which
    /// is about where a Latin face puts it.
    /// </remarks>
    double Baseline(GlkStyle style) => LineHeight(style) * 0.8;

    /// <summary>
    /// [glk #window_textgrid] The width of one cell of the fixed font, in
    /// pixels, which is what a text grid is measured in and what the
    /// library divides the display into.
    /// </summary>
    double CellWidth { get; }

    /// <summary>The height of one cell of the fixed font.</summary>
    double CellHeight { get; }
}

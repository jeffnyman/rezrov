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
    /// [glk #window_textgrid] The width of one cell of the fixed font, in
    /// pixels, which is what a text grid is measured in and what the
    /// library divides the display into.
    /// </summary>
    double CellWidth { get; }

    /// <summary>The height of one cell of the fixed font.</summary>
    double CellHeight { get; }
}

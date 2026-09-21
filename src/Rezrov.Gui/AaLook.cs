using Rezrov.AaMachine;

namespace Rezrov.Gui;

/// <summary>
/// [aam story] How a run of text is set: the face, the size, the
/// weight and slant, how far the letters stand apart, and the colors.
/// </summary>
/// <remarks>
/// A style class is a handful of CSS properties, and a run of text
/// sits inside however many classes the game has opened around it. All
/// of them together come to one of these, worked out once when the
/// text arrives, so that the layout below has a single thing to
/// measure and the painter a single thing to draw.
///
/// It is a value rather than an object because runs are joined when
/// they match, and two pieces of text set the same way should be one
/// piece.
/// </remarks>
/// <param name="Family">
/// The faces to try, best first, as CSS writes them. Empty asks for
/// whatever the frontend sets its prose in.
/// </param>
/// <param name="Size">How large the text is, in pixels.</param>
/// <param name="Bold">Whether it is heavier than ordinary.</param>
/// <param name="Italic">Whether it leans.</param>
/// <param name="Spacing">
/// How much room to leave after every letter, in pixels, which a game
/// asks for to spread a title out.
/// </param>
/// <param name="Ink">The color of the text, as 0xAARRGGBB.</param>
/// <param name="Paper">
/// The color behind it, or nothing at all where no class asked for
/// one, which is the usual: the page shows through.
/// </param>
public readonly record struct AaLook(
    string Family,
    double Size,
    bool Bold,
    bool Italic,
    double Spacing,
    uint Ink,
    uint Paper)
{
    /// <summary>Whether anything is drawn behind the text.</summary>
    public bool HasPaper => (Paper >> 24) != 0;
}

/// <summary>
/// [aam story] The four sides of a margin, a padding or a border, in
/// pixels.
/// </summary>
/// <param name="Top">Above the box.</param>
/// <param name="Right">To the right of it.</param>
/// <param name="Bottom">Below it.</param>
/// <param name="Left">To the left of it.</param>
public readonly record struct AaEdges(double Top, double Right, double Bottom, double Left)
{
    /// <summary>Nothing on any side.</summary>
    public static AaEdges None => default;

    /// <summary>How much the two sides take between them.</summary>
    public double Across => Left + Right;

    /// <summary>How much the top and the bottom take between them.</summary>
    public double Down => Top + Bottom;
}

/// <summary>
/// [aam story] A div worked out for a screen: where its edges fall,
/// what is drawn around and behind it, and how its lines are set.
/// </summary>
/// <param name="Margin">The room left outside it.</param>
/// <param name="Padding">The room left inside it.</param>
/// <param name="Border">How thick a line is drawn around it.</param>
/// <param name="BorderColor">What color that line is.</param>
/// <param name="Radius">How far its corners are rounded.</param>
/// <param name="Background">The color behind it, or none.</param>
/// <param name="Width">
/// How wide the box is inside, or null to fill whatever room there is.
/// </param>
/// <param name="Centered">
/// Whether the room left over goes half on each side, which is how a
/// narrow block is put in the middle of a page.
/// </param>
/// <param name="Alignment">Where its lines sit between its edges.</param>
/// <param name="Floats">Which edge it is set aside to, if either.</param>
/// <param name="Clears">
/// Whether it waits until it is past whatever has been set aside.
/// </param>
/// <param name="Hidden">Whether it is not shown at all.</param>
/// <param name="Height">
/// How tall it is at the least, in pixels, or nought where it is as
/// tall as what it holds.
/// </param>
public readonly record struct AaBox(
    AaEdges Margin,
    AaEdges Padding,
    double Border,
    uint BorderColor,
    double Radius,
    uint Background,
    double? Width,
    bool Centered,
    AaAlignment Alignment,
    AaFloat Floats,
    bool Clears,
    bool Hidden,
    double Height)
{
    /// <summary>A box that asks for nothing.</summary>
    public static AaBox Plain => default;

    /// <summary>Whether a line is drawn around it.</summary>
    public bool HasBorder => Border > 0 && (BorderColor >> 24) != 0;

    /// <summary>Whether anything is painted behind it.</summary>
    public bool HasBackground => (Background >> 24) != 0;

    /// <summary>
    /// Whether anything at all is drawn for the box itself, as against
    /// for the text in it.
    /// </summary>
    public bool IsDrawn => HasBorder || HasBackground;

    /// <summary>
    /// Whether a margin above or below it can run together with the
    /// margin it meets. CSS runs two margins into one where they touch,
    /// and a line or a strip of padding between them keeps them apart.
    /// </summary>
    public bool Collapses => !HasBorder && Padding.Top == 0 && Padding.Bottom == 0;
}

/// <summary>
/// What the layout needs to know about the faces it is setting text
/// in: how wide a piece of it comes out, and how far a line of it
/// reaches above and below the line it stands on.
/// </summary>
/// <remarks>
/// Measuring is the only thing the layout needs from the drawing
/// toolkit, so it is the only thing that crosses this seam. Everything
/// above it, which is all the wrapping and all the boxes, is ordinary
/// code that a test can drive with a font of its own invention.
/// </remarks>
public interface IAaGlyphs
{
    /// <summary>
    /// How wide a piece of text is, in pixels, set the given way. The
    /// room left after each letter is the layout's to add, not the
    /// font's.
    /// </summary>
    double Width(string text, AaLook look);

    /// <summary>
    /// How far a line of it reaches above the line it stands on.
    /// </summary>
    double Ascent(AaLook look);

    /// <summary>How far it reaches below.</summary>
    double Descent(AaLook look);

    /// <summary>
    /// [aam story] How wide the figure nought is, which is what CSS
    /// means by a measurement in characters and what a game means when
    /// it asks for a status field so many characters wide.
    /// </summary>
    double CharacterWidth(AaLook look);
}

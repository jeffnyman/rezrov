using Rezrov.Gui;

namespace Rezrov.Tests;

/// <summary>
/// A face invented for the tests, where every character is exactly as
/// wide as the text is tall.
/// </summary>
/// <remarks>
/// That is the point of the measuring seam: a measurement in ems, a
/// measurement in characters and a count of letters are then all the
/// same number, so the layout can be stated exactly rather than
/// approximately. Ordinary text is ten tall, so a width of a hundred
/// holds ten characters and a line takes thirteen and a half.
/// </remarks>
internal sealed class AaRuler : IAaGlyphs
{
    /// <summary>The size ordinary text is set at.</summary>
    public const double Size = 10;

    /// <summary>How tall a line of it comes out.</summary>
    public const double LineHeight = Size * 1.35;

    /// <summary>How text is set where no class says otherwise.</summary>
    public static AaLook Plain { get; } =
        new(string.Empty, Size, false, false, 0, AaTheme.Ink, 0);

    public double Width(string text, AaLook look)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Length * look.Size;
    }

    public double Ascent(AaLook look) => look.Size * 0.8;

    public double Descent(AaLook look) => look.Size * 0.2;

    public double CharacterWidth(AaLook look) => look.Size;
}

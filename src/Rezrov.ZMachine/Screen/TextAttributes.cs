namespace Rezrov.ZMachine.Screen;

/// <summary>
/// How a character is to be shown: its style, colors, and font.
/// </summary>
/// <remarks>
/// [zm 8.7.1] style, [zm 8.3] colors, and [zm 8.1.2] font are the
/// three things a game can change about text in Versions 4 and 5,
/// and each can change [zm 8.7.1.2] in the middle of a word, so they
/// travel with every character rather than with a line.
/// </remarks>
/// <param name="Style">The styles in force, possibly combined.</param>
/// <param name="Foreground">An actual color, never 0 or 1.</param>
/// <param name="Background">An actual color, never 0 or 1.</param>
/// <param name="Font">[zm 8.1.2] The font number: 1, 3, or 4.</param>
public readonly record struct TextAttributes(TextStyle Style, ScreenColor Foreground, ScreenColor Background, int Font)
{
    /// <summary>[zm 8.1.2] The normal font.</summary>
    public const int NormalFont = 1;

    /// <summary>
    /// [zm 8.1.4] The picture font, which is never provided.
    /// </summary>
    public const int PictureFont = 2;

    /// <summary>[zm 8.1.5] The character graphics font.</summary>
    public const int CharacterGraphicsFont = 3;

    /// <summary>[zm 8.1.2] The Courier-style fixed pitch font.</summary>
    public const int FixedPitchFont = 4;

    /// <summary>
    /// [zm 8.1] Whether these attributes call for a fixed pitch font:
    /// either the style says so or font 4 is selected.
    /// </summary>
    public bool IsFixedPitch => Style.HasFlag(TextStyle.FixedPitch) || Font == FixedPitchFont;
}

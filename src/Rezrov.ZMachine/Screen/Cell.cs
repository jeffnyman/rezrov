namespace Rezrov.ZMachine.Screen;

/// <summary>
/// One character position in the upper window or the status line.
/// </summary>
/// <param name="Character">
/// The character shown, or a space where nothing has been printed.
/// </param>
/// <param name="Attributes">How it is shown.</param>
public readonly record struct Cell(char Character, TextAttributes Attributes)
{
    /// <summary>
    /// A blank cell in the given attributes, as erasing leaves behind.
    /// [zm 8.7.3.2] Erased space takes the background color but never
    /// reverse video.
    /// </summary>
    public static Cell Blank(TextAttributes attributes) =>
        new(' ', attributes with { Style = attributes.Style & ~TextStyle.ReverseVideo });
}

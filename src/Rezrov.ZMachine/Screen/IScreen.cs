namespace Rezrov.ZMachine.Screen;

/// <summary>
/// What a frontend provides for the screen model to draw on.
/// </summary>
/// <remarks>
/// The rules of section 8 live in <see cref="ScreenModel"/>, which owns
/// the windows, cursors, styles, and the upper window's contents. What
/// is left for a frontend is small: say how big the screen is and what
/// it can show, take the lower window's text as a stream of styled runs
/// and newlines, and repaint the upper window and status line from the
/// model when told they changed. A console that only writes a stream
/// can ignore the upper window entirely; a terminal or a GUI paints it.
///
/// This is the same division Frotz makes between its common screen code
/// and its os_ layer, with one difference: the upper window's cells are
/// kept here in the model, so no frontend has to keep its own copy.
/// </remarks>
public interface IScreen
{
    /// <summary>[zm 8.4] Width in characters.</summary>
    int Width { get; }

    /// <summary>[zm 8.4] Height in lines.</summary>
    int Height { get; }

    ScreenCapabilities Capabilities { get; }

    /// <summary>
    /// [zm 8.3.3] The color text is shown in unless the game asks for
    /// another. An actual color, black to white.
    /// </summary>
    ScreenColor DefaultForeground { get; }

    /// <summary>[zm 8.3.3] The color behind text by default.</summary>
    ScreenColor DefaultBackground { get; }

    /// <summary>
    /// Whether a Unicode character can be shown, for check_unicode.
    /// [zm 3.8.5.4.1] Everything below $0100 must be, at least as a
    /// textual equivalent.
    /// </summary>
    bool CanPrint(char character);

    /// <summary>
    /// Shows a run of text in the lower window at the cursor, in one
    /// set of attributes. The model has already decided where lines
    /// break when it is wrapping.
    /// </summary>
    void Print(string text, TextAttributes attributes);

    /// <summary>
    /// Ends the current line of the lower window, scrolling it if the
    /// frontend is a grid.
    /// </summary>
    void NewLine();

    /// <summary>
    /// [zm 8.7.3.2] Clears the lower window to the background color.
    /// </summary>
    void EraseLowerWindow(ScreenColor background);

    /// <summary>
    /// [zm op:erase_line] Clears from the lower window's cursor to the
    /// end of its line.
    /// </summary>
    void EraseToEndOfLine(ScreenColor background);

    /// <summary>
    /// [zm 8.4.1] Pauses for the player to read a full screen of text.
    /// Called only when <see cref="ScreenCapabilities.FixedGrid"/> is
    /// declared.
    /// </summary>
    void MorePrompt();

    /// <summary>
    /// The upper window or the status line changed, or the model is
    /// about to wait for input, so a frontend that shows them should
    /// repaint from <paramref name="model"/> now.
    /// </summary>
    void UpdateUpperWindow(ScreenModel model);
}

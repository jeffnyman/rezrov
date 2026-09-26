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

    /// <summary>
    /// [zm 8.1.1] A character's width in units, for Version 6, where
    /// [zm 8.8.1] the screen is measured in units rather than
    /// characters. A frontend made of character cells has nothing
    /// smaller than a cell, so its font is 1 unit wide and 1 high, and
    /// its units are its cells.
    /// </summary>
    int FontWidth => 1;

    /// <summary>[zm 8.1.1] A character's height in units.</summary>
    int FontHeight => 1;

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
    /// Ends the current line because the text ran out of width rather
    /// than because the game ended it.
    /// </summary>
    /// <remarks>
    /// The same break on the screen, and a frontend that only paints
    /// is right to treat it as one, which is why it falls back to
    /// <see cref="NewLine"/>. It is told apart for the sake of a
    /// frontend that keeps the text: a break the game printed belongs
    /// to the text and must survive, while a break the width forced is
    /// a fact about the old width and has to be forgotten before the
    /// text can be laid out again at a new one.
    /// </remarks>
    void WrapLine() => NewLine();

    /// <summary>
    /// [zm 7.2] A space that fell at the right edge and was not drawn,
    /// because the line had no room for it and the next word starts
    /// the next line.
    /// </summary>
    /// <remarks>
    /// Nothing to paint, so a frontend that only paints ignores it.
    /// A frontend that keeps its text keeps the space: it is part of
    /// what the game said, and only the width it arrived at made it
    /// invisible. Dropped, it would come back as two words run
    /// together the moment the screen was laid out any wider.
    /// </remarks>
    void SwallowedSpace(TextAttributes attributes)
    {
    }

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

    /// <summary>
    /// [zm 8.8] The Version 6 screen changed, or the model is about to
    /// wait for input, so a frontend that paints a grid should repaint
    /// every cell from <paramref name="model"/> now. A frontend that
    /// takes text as a stream has already had the text through
    /// <see cref="Print"/> and can ignore this.
    /// </summary>
    void UpdateWindows(WindowedScreenModel model)
    {
    }

    /// <summary>
    /// [arc contract 2] Show <paramref name="picture"/> in the band
    /// across the top of the screen, or where the number is 0 take the
    /// band down. A frontend that declares no
    /// <see cref="ScreenCapabilities.PictureBand"/> is never asked,
    /// because the story reads that capability out of the header and
    /// never issues the opcode without it.
    /// </summary>
    /// <param name="picture">
    /// Which picture to show, numbered as the resource file numbers
    /// them, or 0 to clear the band.
    /// </param>
    /// <param name="mode">
    /// How tall the band is in text rows, 9 or 12, which is eight
    /// pixel rows each. This operand is the authority on the band's
    /// height: it arrives on every call, a clear included, and a
    /// picture is never measured to work the layout out.
    /// </param>
    /// <remarks>
    /// [arc contract 2] A picture nothing answers to, or one that
    /// cannot be read, is passed over in silence and play goes on. A
    /// picture is presentation, never game state, so there is nothing
    /// here a story can be told went wrong.
    /// </remarks>
    /// <param name="paging">
    /// [zm 10.2.4] Whether the player may be held up to read. False
    /// while commands come from a file, when nothing should wait for
    /// a key that is not going to be pressed.
    /// </param>
    /// <returns>
    /// [arc contract 3] Whether the player was given a chance to read
    /// what the band was about to cover, which is what tells the
    /// screen model that the count of unread lines starts again.
    /// </returns>
    bool DrawImageBand(int picture, int mode, bool paging) => false;
}

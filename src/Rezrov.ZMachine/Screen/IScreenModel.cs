using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Input;

namespace Rezrov.ZMachine.Screen;

/// <summary>
/// What every screen model offers the rest of the interpreter, whether
/// it is the two-window model of Versions 1 to 5 or the eight-window
/// model of Version 6.
/// </summary>
/// <remarks>
/// Output stream 1 is a screen model, [zm 7.1.1] and the streams code
/// and the printing opcodes only need what is here: somewhere to send
/// characters, a way to ask whether one can be shown, the moments
/// around input, and a reset. The opcodes that shape the screen differ
/// so much between [zm 8.7] and [zm 8.8] that the interpreter calls
/// each model's own methods for those.
/// </remarks>
public interface IScreenModel : IOutput
{
    /// <summary>The frontend the model draws on.</summary>
    IScreen Screen { get; }

    /// <summary>[zm 8.4] Width in characters.</summary>
    int Width { get; }

    /// <summary>[zm 8.4] Height in lines.</summary>
    int Height { get; }

    /// <summary>
    /// [zm 8.1.1] A character's width in units: 1 before Version 6,
    /// where a unit is a character, and the frontend's font width in
    /// Version 6. [zm 7.1.2] Stream 3 measures its text by it.
    /// </summary>
    int FontWidth { get; }

    /// <summary>The number of the window text goes to.</summary>
    int CurrentWindow { get; }

    /// <summary>
    /// [zm 7.1.1.1] Whether text printed now is copied to the
    /// transcript: the lower window's text before Version 6, and
    /// [zm 8.8.3.1] whatever the window's attribute says in Version 6.
    /// </summary>
    bool EchoesToTranscript { get; }

    /// <summary>The attributes text is printed in right now.</summary>
    TextAttributes Attributes { get; }

    /// <summary>
    /// Where a table row begins, for print_table: the cursor column in
    /// whatever the model measures columns in.
    /// </summary>
    int TableColumn { get; }

    /// <summary>Prints one character to the current window.</summary>
    void Print(char character);

    /// <summary>
    /// [zm op:print_unicode] Prints a Unicode character, or a question
    /// mark where [zm 3.8.5.4.3] the screen has no form for it.
    /// </summary>
    void PrintUnicode(char character);

    /// <summary>
    /// [zm op:check_unicode] Whether the screen can show a character.
    /// </summary>
    bool CanPrint(char character);

    /// <summary>[zm op:new_line] Ends the line in the current window.</summary>
    void NewLine();

    /// <summary>
    /// [zm op:print_table] Moves to the start of the next row of a
    /// table, back to the column the table began in.
    /// </summary>
    void NextTableRow(int startColumn);

    /// <summary>
    /// [zm op:set_text_style] Sets the style of the current window, or
    /// adds to it.
    /// </summary>
    void SetTextStyle(int style);

    /// <summary>
    /// [zm op:buffer_mode] Turns word buffering on or off.
    /// </summary>
    void SetBuffering(bool enabled);

    /// <summary>
    /// Puts the screen as it is at the start of a game and after a
    /// restart.
    /// </summary>
    void Reset();

    /// <summary>
    /// The interpreter is about to wait for input: buffered text comes
    /// out, the [MORE] count starts over, and the frontend repaints.
    /// </summary>
    /// <param name="suppressPaging">
    /// [zm 10.2.4] True while commands come from a file.
    /// </param>
    void PrepareForInput(bool suppressPaging);

    /// <summary>
    /// A line of input has been read from the keyboard and shown by the
    /// frontend as it was typed, so the model can account for it, or
    /// null when the model showed the input itself because it was
    /// played from a file.
    /// </summary>
    void InputEnded(LineInput? typed);

    /// <summary>
    /// Sends out whatever is buffered and lets the frontend repaint.
    /// </summary>
    void Flush();
}

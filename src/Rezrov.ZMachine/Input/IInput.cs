namespace Rezrov.ZMachine.Input;

/// <summary>
/// Where the player's keypresses come from: input stream 0, the
/// keyboard, in the standard's terms.
/// </summary>
/// <remarks>
/// [zm 10.2] Keypresses are drawn from the current input stream, of
/// which there are two: the keyboard and a file of commands. The file is
/// the interpreter's to read, since [zm 10.2.1] its format is the
/// machine's own, and the frontend's to choose, through
/// <see cref="Streams.IFileChooser"/>, so this interface is only the
/// keyboard side. Whatever
/// sits behind it produces ZSCII, because [zm 10.7] the only characters
/// that can be read are the ZSCII characters defined for input. Turning
/// a keystroke into one of those is the frontend's job, and
/// <see cref="Text.Zscii.FromUnicode"/> does the translating.
///
/// Echoing is the frontend's job too. [zm 7.1.1.1] The text a player
/// types is shown on the screen as it is typed, which a terminal does
/// by itself and a screen model does deliberately, and the interpreter
/// does not print it again.
/// </remarks>
public interface IInput
{
    /// <summary>
    /// [zm 10.5.3] and [zm 10.6] Whether this source can time input and
    /// call the interrupt routine a game asks for. The interpreter tells
    /// the game the answer through bit 7 of Flags 1.
    /// </summary>
    bool SupportsTimedInput { get; }

    /// <summary>
    /// Reads a whole command, for the read opcode.
    /// </summary>
    /// <remarks>
    /// The request says how many characters may be accepted, what was
    /// already typed before an interruption, which keys end the command,
    /// and whether to run a timer. A source that honors the timer calls
    /// its interrupt at each interval and stops reading, returning what
    /// was typed so far with terminator 0, if the interrupt says so. The
    /// interrupt routine may print, so a source that shows an input line
    /// should redraw it afterward, as the read opcode's text asks.
    /// </remarks>
    LineInput ReadLine(LineInputRequest request);

    /// <summary>
    /// Reads a single keypress, for the read_char opcode, as a ZSCII code
    /// defined for input, or 0 if the timer's interrupt ended the wait.
    /// </summary>
    ushort ReadKey(InputTimer? timer);
}

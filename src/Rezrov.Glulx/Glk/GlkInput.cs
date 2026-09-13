namespace Rezrov.Glulx.Glk;

/// <summary>What a display has to report when asked to wait.</summary>
public enum GlkInputKind
{
    /// <summary>
    /// A line was entered in a window with a line request.
    /// </summary>
    Line,

    /// <summary>
    /// A key was pressed in a window with a character request.
    /// </summary>
    Key,

    /// <summary>The timer's interval passed with no input.</summary>
    Timer,

    /// <summary>
    /// [glk #arrange_events] The player changed the display's size, so
    /// the windows need laying out again.
    /// </summary>
    Arrange,

    /// <summary>
    /// There will never be any input: the player has gone, or the
    /// input was a file that ran out.
    /// </summary>
    Ended,
}

/// <summary>
/// One piece of player input from the display, before the library
/// turns it into an event: which window, and the line or key.
/// </summary>
/// <param name="Kind">What arrived.</param>
/// <param name="Window">The window it was for, for a line or key.</param>
/// <param name="Text">The line entered, for a line.</param>
/// <param name="Key">
/// [glk #encoding_inchar] The character or special keycode, for a key.
/// </param>
/// <param name="Terminator">
/// [glk #line_events] The special keycode that ended the line, or zero
/// for the enter key.
/// </param>
public sealed record GlkInput(GlkInputKind Kind, GlkWindow? Window, string? Text, uint Key, uint Terminator)
{
    public static GlkInput Ended { get; } = new(GlkInputKind.Ended, null, null, 0, 0);

    public static GlkInput Timer { get; } = new(GlkInputKind.Timer, null, null, 0, 0);

    /// <summary>[glk #arrange_events] The display changed size.</summary>
    public static GlkInput Arrange { get; } = new(GlkInputKind.Arrange, null, null, 0, 0);

    public static GlkInput Line(GlkWindow window, string text, uint terminator = 0) =>
        new(GlkInputKind.Line, window, text, 0, terminator);

    public static GlkInput KeyPress(GlkWindow window, uint key) =>
        new(GlkInputKind.Key, window, null, key, 0);
}

/// <summary>
/// [glk #event] An event as glk_select returns it: the type, the window
/// it came from if any, and two values whose meaning the type fixes.
/// </summary>
public readonly record struct GlkEvent(EventType Type, GlkWindow? Window, uint Value1, uint Value2)
{
    public static GlkEvent None { get; } = new(EventType.None, null, 0, 0);
}

/// <summary>
/// [glk #line_events] A pending line input request: where the line
/// goes and how much of it there is room for.
/// </summary>
/// <param name="Memory">The memory the buffer is in.</param>
/// <param name="Address">The buffer's address.</param>
/// <param name="MaxLength">The buffer's length in characters.</param>
/// <param name="Unicode">Whether the buffer holds words or bytes.</param>
/// <param name="Initial">Text already in the buffer, to be edited.</param>
public sealed record LineRequest(GlulxMemory Memory, uint Address, uint MaxLength, bool Unicode, string Initial);

/// <summary>
/// [glk #char_events] The kind of character request pending.
/// </summary>
public enum CharRequest
{
    None,
    Latin1,
    Unicode,
}

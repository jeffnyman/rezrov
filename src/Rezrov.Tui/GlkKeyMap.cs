using Rezrov.Glulx.Glk;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Rezrov.Tui;

/// <summary>
/// [glk #char_events] Turns the terminal's keys into Glk key codes:
/// the special keys the specification names, and a code point for
/// anything printable.
/// </summary>
public static class GlkKeyMap
{
    /// <summary>
    /// The Glk key for a terminal key, or null for one Glk has no name
    /// for, such as a control combination.
    /// </summary>
    public static uint? ToGlk(Key key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.IsCtrl || key.IsAlt)
        {
            return null;
        }

        switch (key.NoShift.KeyCode)
        {
            case KeyCode.Enter:
                return GlkKeyCode.Return;
            case KeyCode.Backspace:
            case KeyCode.Delete:
                return GlkKeyCode.Delete;
            case KeyCode.Esc:
                return GlkKeyCode.Escape;
            case KeyCode.Tab:
                return GlkKeyCode.Tab;
            case KeyCode.CursorUp:
                return GlkKeyCode.Up;
            case KeyCode.CursorDown:
                return GlkKeyCode.Down;
            case KeyCode.CursorLeft:
                return GlkKeyCode.Left;
            case KeyCode.CursorRight:
                return GlkKeyCode.Right;
            case KeyCode.PageUp:
                return GlkKeyCode.PageUp;
            case KeyCode.PageDown:
                return GlkKeyCode.PageDown;
            case KeyCode.Home:
                return GlkKeyCode.Home;
            case KeyCode.End:
                return GlkKeyCode.End;
            case >= KeyCode.F1 and <= KeyCode.F12:
                // [glk #char_events] Function keys count down from F1.
                return GlkKeyCode.Func1 - (uint)(key.NoShift.KeyCode - KeyCode.F1);
            default:
                break;
        }

        return key.TryGetPrintableRune(out var rune) ? (uint)rune.Value : null;
    }
}

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

    /// <summary>
    /// The Glk key codes for text pasted into the terminal, which
    /// arrives whole rather than as keys.
    /// </summary>
    /// <remarks>
    /// [glk #encoding_inchar] A character is its own code point, and a
    /// line ending of any of the three shapes is the return key, which
    /// is what makes a pasted command run.
    /// </remarks>
    public static IEnumerable<uint> ToGlk(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];

            if (character is '\r' or '\n')
            {
                if (character == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                yield return GlkKeyCode.Return;
                continue;
            }

            if (char.IsHighSurrogate(character) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                yield return (uint)char.ConvertToUtf32(character, text[i + 1]);
                i++;
                continue;
            }

            if (!char.IsControl(character))
            {
                yield return character;
            }
        }
    }
}

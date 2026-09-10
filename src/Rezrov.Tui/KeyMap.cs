using Rezrov.ZMachine.Text;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Rezrov.Tui;

/// <summary>
/// Turns a terminal keypress into the ZSCII code the game receives.
/// </summary>
/// <remarks>
/// [zm 10.7] The only characters that can be read are ZSCII characters
/// defined for input: [zm 3.8.4] the cursor keys are 129 to 132 and the
/// function keys 133 to 144, [zm 3.8.2] return is 13, delete 8, and
/// escape 27, and everything printable goes through the story's
/// translation table. A key with Ctrl or Alt held is not a character
/// at all, and is left to the frontend.
/// </remarks>
public sealed class KeyMap
{
    private readonly UnicodeTranslationTable _extraCharacters;

    public KeyMap(UnicodeTranslationTable extraCharacters)
    {
        ArgumentNullException.ThrowIfNull(extraCharacters);
        _extraCharacters = extraCharacters;
    }

    /// <summary>
    /// The ZSCII code for a key, or null if the key means nothing to a
    /// game.
    /// </summary>
    public ushort? ToZscii(Key key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.IsCtrl || key.IsAlt)
        {
            return null;
        }

        switch (key.NoShift.KeyCode)
        {
            case KeyCode.Enter:
                return Zscii.Newline;
            case KeyCode.Backspace:
            case KeyCode.Delete:
                return Zscii.Delete;
            case KeyCode.Esc:
                return Zscii.Escape;
            case KeyCode.CursorUp:
                return Zscii.CursorUp;
            case KeyCode.CursorDown:
                return Zscii.CursorDown;
            case KeyCode.CursorLeft:
                return Zscii.CursorLeft;
            case KeyCode.CursorRight:
                return Zscii.CursorRight;
            case >= KeyCode.F1 and <= KeyCode.F12:
                return (ushort)(Zscii.F1 + (key.NoShift.KeyCode - KeyCode.F1));
            default:
                break;
        }

        if (key.TryGetPrintableRune(out var rune) && rune.IsBmp)
        {
            return Zscii.FromUnicode((char)rune.Value, _extraCharacters);
        }

        return null;
    }
}

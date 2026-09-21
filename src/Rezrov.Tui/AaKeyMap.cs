using Rezrov.AaMachine;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Rezrov.Tui;

/// <summary>
/// [aam text] Turns the terminal's keys into the characters an
/// Aa-machine story reads.
/// </summary>
/// <remarks>
/// Which keys the machine has characters for is
/// <see cref="AaKeys"/>'s to say, since every frontend that takes keys
/// has to agree about that. What is here is the terminal's own names
/// for them and nothing else.
/// </remarks>
public static class AaKeyMap
{
    /// <summary>
    /// The character a terminal key stands for, or null for a key the
    /// machine has no character for.
    /// </summary>
    public static int? ToCharacter(Key key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.IsCtrl || key.IsAlt)
        {
            return null;
        }

        switch (key.NoShift.KeyCode)
        {
            case KeyCode.Enter:
                return AaKeys.Return;
            case KeyCode.Backspace:
            case KeyCode.Delete:
                return AaKeys.Backspace;
            case KeyCode.CursorUp:
                return AaKeys.Up;
            case KeyCode.CursorDown:
                return AaKeys.Down;
            case KeyCode.CursorLeft:
                return AaKeys.Left;
            case KeyCode.CursorRight:
                return AaKeys.Right;
            default:
                break;
        }

        var rune = key.AsRune;

        return rune.Value is >= ' ' and not 0x7f ? rune.Value : null;
    }

    /// <summary>
    /// The characters of a piece of pasted text, so that a command
    /// copied from somewhere else can be typed for the player.
    /// </summary>
    public static IEnumerable<int> ToCharacters(string text) => AaKeys.Pasted(text);
}

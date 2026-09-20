using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Rezrov.Tui;

/// <summary>
/// [aam text] Turns the terminal's keys into the characters an
/// Aa-machine story reads.
/// </summary>
/// <remarks>
/// The character set names only a handful of keys that are not
/// letters: backspace, return, and the four arrows. Anything else that
/// is printable is itself, and a key the machine has no character for
/// is not passed on at all.
/// </remarks>
public static class AaKeyMap
{
    /// <summary>[aam text] Backspace or delete.</summary>
    public const int Backspace = 0x08;

    /// <summary>[aam text] The return key.</summary>
    public const int Return = 0x0d;

    /// <summary>
    /// [aam text] The four arrows, in the order the set has them.
    /// </summary>
    public const int Up = 0x10;

    public const int Down = 0x11;

    public const int Left = 0x12;

    public const int Right = 0x13;

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
                return Return;
            case KeyCode.Backspace:
            case KeyCode.Delete:
                return Backspace;
            case KeyCode.CursorUp:
                return Up;
            case KeyCode.CursorDown:
                return Down;
            case KeyCode.CursorLeft:
                return Left;
            case KeyCode.CursorRight:
                return Right;
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
    public static IEnumerable<int> ToCharacters(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var character in text)
        {
            if (character is '\r' or '\n')
            {
                yield return Return;
            }
            else if (character >= ' ' && character != 0x7f)
            {
                yield return character;
            }
        }
    }
}

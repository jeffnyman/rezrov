using Rezrov.AaMachine;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Text;

namespace Rezrov.Gtui;

/// <summary>
/// [aam text] Turns the keys this program's windows report into the
/// characters an Aa-machine story reads.
/// </summary>
/// <remarks>
/// Each window translates its own keys into [zm 3.8] the Z-machine's
/// codes before handing them over, because only the platform knows what
/// its keys mean and nothing above that line should have to learn three
/// sets of them. That is the right arrangement for a program that began
/// as a Z-machine frontend, and it leaves one short step to take here:
/// the Z-machine's codes into the Aa-machine's.
///
/// The two machines agree about almost everything, since both write
/// ordinary characters as themselves. They disagree about the handful
/// of keys that are not characters, and about a key that one machine
/// names and the other does not, which is not passed on at all.
/// </remarks>
public static class AaGridKeys
{
    /// <summary>
    /// The character an Aa-machine story reads a Z-machine key as, or
    /// null for a key the Aa-machine has no character for.
    /// </summary>
    public static int? FromZscii(ushort key) => key switch
    {
        Zscii.Newline => AaKeys.Return,
        Zscii.Delete => AaKeys.Backspace,
        Zscii.CursorUp => AaKeys.Up,
        Zscii.CursorDown => AaKeys.Down,
        Zscii.CursorLeft => AaKeys.Left,
        Zscii.CursorRight => AaKeys.Right,

        // [aam text] The Aa-machine names no escape key and no function
        // keys, so there is nothing to turn them into. A story never
        // sees them rather than seeing something else.
        Zscii.Escape or Zscii.Tab => null,
        >= Zscii.F1 and <= Zscii.F12 => null,
        >= Zscii.Keypad0 and <= Zscii.Keypad9 => key - Zscii.Keypad0 + '0',

        // [zm 3.8] Everything else that can be typed is a character,
        // and the table says which one, including the accented letters
        // the two machines number differently. A code the table does
        // not name is not a character either machine can read.
        _ => Zscii.ToUnicode(key, ZMachineVersion.V5, UnicodeTranslationTable.Default),
    };
}

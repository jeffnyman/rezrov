using Avalonia.Input;
using Rezrov.Glulx.Glk;
using Rezrov.ZMachine.Text;

namespace Rezrov.Gui;

/// <summary>
/// [glk #character_input] The keys the toolkit reports, as the key codes
/// Glk names.
/// </summary>
/// <remarks>
/// Only the special keys are here. Ordinary characters arrive as text
/// rather than as keys, since what a key produces depends on the
/// keyboard layout and on any dead keys before it, and the toolkit has
/// already worked that out by the time it reports text.
/// </remarks>
internal static class GuiKeyMap
{
    /// <summary>
    /// The Glk key code for a key, or null for one Glk does not name.
    /// </summary>
    public static uint? ToGlk(Key key) => key switch
    {
        Key.Enter or Key.Return => GlkKeyCode.Return,
        Key.Back => GlkKeyCode.Delete,
        Key.Delete => GlkKeyCode.Delete,
        Key.Escape => GlkKeyCode.Escape,
        Key.Tab => GlkKeyCode.Tab,
        Key.Left => GlkKeyCode.Left,
        Key.Right => GlkKeyCode.Right,
        Key.Up => GlkKeyCode.Up,
        Key.Down => GlkKeyCode.Down,
        Key.PageUp => GlkKeyCode.PageUp,
        Key.PageDown => GlkKeyCode.PageDown,
        Key.Home => GlkKeyCode.Home,
        Key.End => GlkKeyCode.End,

        // [glk #character_input] The function keys run downward from
        // Func1, so the twelfth is eleven below the first.
        >= Key.F1 and <= Key.F12 => GlkKeyCode.Func1 - (uint)(key - Key.F1),
        _ => null,
    };

    /// <summary>
    /// [zm 3.8] The ZSCII code for a key, or null for one the Z-machine
    /// does not name. The function keys run upward from F1, unlike
    /// Glk's, and the keypad digits are their own codes.
    /// </summary>
    public static ushort? ToZscii(Key key) => key switch
    {
        Key.Enter or Key.Return => Zscii.Newline,
        Key.Back or Key.Delete => Zscii.Delete,
        Key.Escape => Zscii.Escape,
        Key.Tab => Zscii.Tab,
        Key.Up => Zscii.CursorUp,
        Key.Down => Zscii.CursorDown,
        Key.Left => Zscii.CursorLeft,
        Key.Right => Zscii.CursorRight,
        >= Key.F1 and <= Key.F12 => (ushort)(Zscii.F1 + (key - Key.F1)),
        >= Key.NumPad0 and <= Key.NumPad9 => (ushort)(Zscii.Keypad0 + (key - Key.NumPad0)),
        _ => null,
    };
}

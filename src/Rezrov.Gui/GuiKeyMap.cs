using Avalonia.Input;
using Rezrov.Glulx.Glk;

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
}

using Avalonia.Input;

namespace Rezrov.Gui;

/// <summary>
/// The keys that choose a menu item without opening the menu.
/// </summary>
/// <remarks>
/// The game owns the keyboard, so a key taken for the menu is a key no
/// game can be given. Only Control with a letter is taken. A letter
/// reaches a game as the text it types, never as a key, and Control
/// with one types nothing, so no game is ever passed one; that is why
/// pasting and the map already use it. The function keys are the
/// game's: [zm 3.8.4] the Z-machine has codes for F1 to F12 and Beyond
/// Zork reads them, and [glk #character_input] Glk names them too. Alt
/// with a letter opens the menus themselves.
///
/// Restart is given no key, since one pressed by mistake would throw
/// away a game, and neither is Quit, since the system's own key for
/// closing a window already does it.
///
/// What each key does is still only done where the item would be
/// enabled: a command is typed only while the game is waiting for one
/// and knows the word.
/// </remarks>
public static class MenuKeys
{
    /// <summary>Opening another story.</summary>
    public static KeyGesture Open { get; } = new(Key.O, KeyModifiers.Control);

    /// <summary>Opening and closing the map, where there is one.</summary>
    public static KeyGesture Map { get; } = new(Key.M, KeyModifiers.Control);

    /// <summary>
    /// Having a screen reader read the status line, which it is not told
    /// every turn, since it is drawn again every turn.
    /// </summary>
    public static KeyGesture Status { get; } = new(Key.L, KeyModifiers.Control);

    /// <summary>Having a screen reader read the last turn again.</summary>
    public static KeyGesture Repeat { get; } = new(Key.P, KeyModifiers.Control);

    /// <summary>
    /// The commands from the Game menu that are given a key, by the word
    /// typed for the player.
    /// </summary>
    public static IReadOnlyDictionary<string, KeyGesture> Commands { get; } = new Dictionary<string, KeyGesture>
    {
        ["save"] = new(Key.S, KeyModifiers.Control),
        ["restore"] = new(Key.R, KeyModifiers.Control),
        ["undo"] = new(Key.Z, KeyModifiers.Control),
    };

    /// <summary>Every key taken, pasting included.</summary>
    public static IEnumerable<KeyGesture> All =>
        [Open, Map, Status, Repeat, .. Commands.Values, new KeyGesture(Key.V, KeyModifiers.Control)];
}

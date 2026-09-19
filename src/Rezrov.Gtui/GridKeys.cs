using Rezrov.ZMachine.Text;

namespace Rezrov.Gtui;

/// <summary>
/// [zm 3.8] What a key on the keyboard means to the game.
/// </summary>
/// <remarks>
/// Windows reports a key twice over: once as a key going down, which is
/// a code for the key itself, and once as a character, which is what
/// that key means with the shift and the keyboard layout taken into
/// account. Letters and punctuation are taken from the character, so
/// that every layout in the world works without this program knowing
/// anything about layouts, and only the keys that have no character of
/// their own are taken from the key code.
/// </remarks>
public static class GridKeys
{
    // The virtual key codes of the keys a game can ask for that do not
    // arrive as characters.
    private const int Left = 0x25;
    private const int Up = 0x26;
    private const int Right = 0x27;
    private const int Down = 0x28;
    private const int FirstFunction = 0x70;
    private const int LastFunction = 0x7B;

    /// <summary>
    /// [zm 3.8] The character the player typed, or zero for one the
    /// Z-machine has no place for.
    /// </summary>
    public static ushort FromCharacter(char character) => character switch
    {
        // [zm 3.8.2.1] Both of the keys a keyboard might send for a new
        // line arrive as the one the Z-machine knows.
        '\r' or '\n' => Zscii.Newline,
        '\b' => Zscii.Delete,
        '' => Zscii.Escape,

        // [zm 3.8.3] The printable characters, which for the moment are
        // the ones the standard's own alphabet has. A character outside
        // it is dropped rather than sent as something else.
        >= ' ' and <= '~' => character,
        _ => 0,
    };

    // [x11] The symbols X names the same keys by. They are the same
    // keys; only the numbering differs.
    private const ulong SymLeft = 0xFF51;
    private const ulong SymUp = 0xFF52;
    private const ulong SymRight = 0xFF53;
    private const ulong SymDown = 0xFF54;
    private const ulong SymFirstFunction = 0xFFBE;
    private const ulong SymLastFunction = 0xFFC9;

    /// <summary>
    /// [zm 3.8.3] The key the player pressed, for the keys that send no
    /// character, or zero for a key the Z-machine does not name.
    /// </summary>
    public static ushort FromKey(int key) => key switch
    {
        Up => Zscii.CursorUp,
        Down => Zscii.CursorDown,
        Left => Zscii.CursorLeft,
        Right => Zscii.CursorRight,

        // [zm 3.8.4] The twelve function keys run upward from 133,
        // in the order the keyboard has them.
        >= FirstFunction and <= LastFunction => (ushort)(Zscii.F1 + (key - FirstFunction)),
        _ => 0,
    };

    /// <summary>
    /// [zm 3.8.3] The same, for X11, which names its keys by symbol
    /// rather than by code. A key that sends a character is dealt with
    /// as a character, so only the rest arrive here.
    /// </summary>
    public static ushort FromKeySym(ulong symbol) => symbol switch
    {
        SymUp => Zscii.CursorUp,
        SymDown => Zscii.CursorDown,
        SymLeft => Zscii.CursorLeft,
        SymRight => Zscii.CursorRight,
        >= SymFirstFunction and <= SymLastFunction => (ushort)(Zscii.F1 + (symbol - SymFirstFunction)),
        _ => 0,
    };
}

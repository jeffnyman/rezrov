namespace Rezrov.ZMachine.Screen;

/// <summary>
/// The text styles of [zm 8.7.1], modelled on a VT100 terminal.
/// </summary>
/// <remarks>
/// [zm op:set_text_style] The values are powers of two so that, as of
/// Standard 1.1, a game can ask for a combination in one opcode by
/// adding them. Roman is the absence of all of them, and selecting it
/// turns every other style off.
/// </remarks>
[Flags]
public enum TextStyle : byte
{
    Roman = 0,

    ReverseVideo = 1,

    Bold = 2,

    Italic = 4,

    FixedPitch = 8,
}

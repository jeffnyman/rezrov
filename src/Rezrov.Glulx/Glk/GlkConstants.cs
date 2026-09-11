namespace Rezrov.Glulx.Glk;

// The constants of glk.h that the library and its callers share. The
// values are the header's, since a game passes them as numbers.

/// <summary>[glk #window_types] The kinds of window.</summary>
public enum WindowType : uint
{
    /// <summary>For style hints: every kind of window.</summary>
    AllTypes = 0,
    Pair = 1,
    Blank = 2,
    TextBuffer = 3,
    TextGrid = 4,
    Graphics = 5,
}

/// <summary>
/// [glk #window_opening] The method argument of a split: a direction,
/// a division, and a border hint, combined with or.
/// </summary>
[Flags]
public enum WindowMethod : uint
{
    Left = 0x00,
    Right = 0x01,
    Above = 0x02,
    Below = 0x03,
    DirectionMask = 0x0F,
    Fixed = 0x10,
    Proportional = 0x20,
    DivisionMask = 0xF0,

    /// <summary>
    /// The border hint: set for no border. The hint's other value,
    /// winmethod_Border, is zero and so has no member here.
    /// </summary>
    NoBorder = 0x100,
}

/// <summary>[glk #stream] The mode a stream is opened in.</summary>
public enum FileMode : uint
{
    Write = 0x01,
    Read = 0x02,
    ReadWrite = 0x03,
    WriteAppend = 0x05,
}

/// <summary>
/// [glk #stream_positions] What a position is relative to.
/// </summary>
public enum SeekMode : uint
{
    Start = 0,
    Current = 1,
    End = 2,
}

/// <summary>[glk #stream_style] The eleven styles.</summary>
public enum GlkStyle : uint
{
    Normal = 0,
    Emphasized = 1,
    Preformatted = 2,
    Header = 3,
    Subheader = 4,
    Alert = 5,
    Note = 6,
    BlockQuote = 7,
    Input = 8,
    User1 = 9,
    User2 = 10,
}

/// <summary>[glk #stream_style_hints] The nine style hints.</summary>
public enum StyleHint : uint
{
    Indentation = 0,
    ParaIndentation = 1,
    Justification = 2,
    Size = 3,
    Weight = 4,
    Oblique = 5,
    Proportional = 6,
    TextColor = 7,
    BackColor = 8,
    ReverseColor = 9,
}

/// <summary>[glk #gestalt] The gestalt selectors.</summary>
public enum GestaltSelector : uint
{
    Version = 0,
    CharInput = 1,
    LineInput = 2,
    CharOutput = 3,
    MouseInput = 4,
    Timer = 5,
    Graphics = 6,
    DrawImage = 7,
    Sound = 8,
    SoundVolume = 9,
    SoundNotify = 10,
    Hyperlinks = 11,
    HyperlinkInput = 12,
    SoundMusic = 13,
    GraphicsTransparency = 14,
    Unicode = 15,
    UnicodeNorm = 16,
    LineInputEcho = 17,
    LineTerminators = 18,
    LineTerminatorKey = 19,
    DateTime = 20,
    Sound2 = 21,
    ResourceStream = 22,
    GraphicsCharInput = 23,
    DrawImageScale = 24,
}

/// <summary>
/// [glk #encoding_out] The answers of the CharOutput gestalt.
/// </summary>
public enum CharOutput : uint
{
    CannotPrint = 0,
    ApproxPrint = 1,
    ExactPrint = 2,
}

/// <summary>
/// [glk #encoding_inchar] The special keys character input can report,
/// counting down from FFFFFFFF.
/// </summary>
public static class GlkKeyCode
{
    public const uint Unknown = 0xFFFFFFFF;
    public const uint Left = 0xFFFFFFFE;
    public const uint Right = 0xFFFFFFFD;
    public const uint Up = 0xFFFFFFFC;
    public const uint Down = 0xFFFFFFFB;
    public const uint Return = 0xFFFFFFFA;
    public const uint Delete = 0xFFFFFFF9;
    public const uint Escape = 0xFFFFFFF8;
    public const uint Tab = 0xFFFFFFF7;
    public const uint PageUp = 0xFFFFFFF6;
    public const uint PageDown = 0xFFFFFFF5;
    public const uint Home = 0xFFFFFFF4;
    public const uint End = 0xFFFFFFF3;
    public const uint Func1 = 0xFFFFFFEF;
    public const uint Func12 = 0xFFFFFFE4;

    /// <summary>Whether a value is one of the special keycodes.</summary>
    public static bool IsSpecial(uint key) => key >= Func12;
}

/// <summary>[glk #event] The kinds of event.</summary>
public enum EventType : uint
{
    None = 0,
    Timer = 1,
    CharInput = 2,
    LineInput = 3,
    MouseInput = 4,
    Arrange = 5,
    Redraw = 6,
    SoundNotify = 7,
    Hyperlink = 8,
    VolumeNotify = 9,
}

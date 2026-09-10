namespace Rezrov.ZMachine.Screen;

/// <summary>
/// [zm 8.8.3.1] The four attributes a Version 6 window has, each on
/// or off.
/// </summary>
[Flags]
public enum WindowAttributes
{
    None = 0,

    /// <summary>
    /// [zm 8.8.3.1.1] Text reaching the right margin continues on the
    /// next line; without it, text past the margin is dropped.
    /// </summary>
    Wrapping = 1 << 0,

    /// <summary>
    /// [zm 8.8.3.4] and [zm op:window_style] The window scrolls when
    /// text reaches its bottom.
    /// </summary>
    Scrolling = 1 << 1,

    /// <summary>
    /// [zm 8.8.3.1] Text printed here is copied to output stream 2,
    /// the transcript, when that is selected.
    /// </summary>
    Transcript = 1 << 2,

    /// <summary>
    /// [zm 8.8.3.1.2] Text is buffered so that it wraps after the last
    /// word that fits rather than the last character.
    /// </summary>
    Buffering = 1 << 3,
}

/// <summary>
/// One of the eight windows of the Version 6 screen model: a position
/// and size in units, a cursor of its own, margins, the text style,
/// colors, and font in force there, its attributes, and the odd
/// properties the game may set.
/// </summary>
/// <remarks>
/// [zm 8.8.3] Windows are invisible and usually lie on top of each
/// other. Nothing printed belongs to a window afterward; a window is
/// only where the next thing goes and how it looks. [zm 8.8.3.2] The
/// eighteen properties the game can read are all here, and
/// <see cref="Property"/> hands them out by number.
/// </remarks>
public sealed class ZWindow
{
    internal ZWindow(int number)
    {
        Number = number;
    }

    /// <summary>The window's number, 0 to 7.</summary>
    public int Number { get; }

    /// <summary>[zm 8.8.3.2] Property 0: the y coordinate, 1 at the top.</summary>
    public int Y { get; internal set; } = 1;

    /// <summary>Property 1: the x coordinate, 1 at the left.</summary>
    public int X { get; internal set; } = 1;

    /// <summary>Property 2: the height in units.</summary>
    public int Height { get; internal set; }

    /// <summary>Property 3: the width in units.</summary>
    public int Width { get; internal set; }

    /// <summary>
    /// Property 4: the cursor's y position, relative to the window's
    /// own top left at (1,1).
    /// </summary>
    public int CursorY { get; internal set; } = 1;

    /// <summary>Property 5: the cursor's x position, relative.</summary>
    public int CursorX { get; internal set; } = 1;

    /// <summary>Property 6: the left margin in units.</summary>
    public int LeftMargin { get; internal set; }

    /// <summary>Property 7: the right margin in units.</summary>
    public int RightMargin { get; internal set; }

    /// <summary>
    /// [zm 8.8.3.2.2] Property 8: the packed address of the routine
    /// called when the countdown reaches zero.
    /// </summary>
    public ushort NewlineInterruptRoutine { get; internal set; }

    /// <summary>
    /// [zm 8.8.3.2.2] Property 9: decremented on each newline; the
    /// interrupt routine runs when it hits zero.
    /// </summary>
    public int InterruptCountdown { get; internal set; }

    /// <summary>[zm 8.8.3.2.3] Property 10: the text style.</summary>
    public TextStyle Style { get; internal set; }

    /// <summary>[zm 8.8.3.2.4] The foreground color.</summary>
    public ScreenColor Foreground { get; internal set; }

    /// <summary>[zm 8.8.3.2.4] The background color.</summary>
    public ScreenColor Background { get; internal set; }

    /// <summary>Property 12: the font number.</summary>
    public int Font { get; internal set; } = TextAttributes.NormalFont;

    /// <summary>[zm 8.8.3.2.5] The font width in units.</summary>
    public int FontWidth { get; internal set; } = 1;

    /// <summary>[zm 8.8.3.2.5] The font height in units.</summary>
    public int FontHeight { get; internal set; } = 1;

    /// <summary>Property 14: the attributes.</summary>
    public WindowAttributes Attributes { get; internal set; }

    /// <summary>
    /// [zm 8.8.3.2.6] Property 15: lines printed since the count was
    /// last reset, or -999 for a window that never shows [MORE].
    /// </summary>
    public int LineCount { get; internal set; }

    /// <summary>
    /// [zm 8.8.3.2.8] Property 16: the true foreground color, or -4
    /// for transparent.
    /// </summary>
    public short TrueForeground { get; internal set; }

    /// <summary>Property 17: the true background color.</summary>
    public short TrueBackground { get; internal set; }

    /// <summary>[zm 8.8.3.2.6] The line count that means never pause.</summary>
    public const int NeverPause = -999;

    public bool Wraps => Attributes.HasFlag(WindowAttributes.Wrapping);

    public bool Scrolls => Attributes.HasFlag(WindowAttributes.Scrolling);

    public bool CopiesToTranscript => Attributes.HasFlag(WindowAttributes.Transcript);

    public bool Buffers => Attributes.HasFlag(WindowAttributes.Buffering);

    /// <summary>The y coordinate just past the window's bottom edge.</summary>
    public int Bottom => Y + Height;

    /// <summary>The x coordinate just past the window's right edge.</summary>
    public int Right => X + Width;

    /// <summary>
    /// [zm 8.8.3.2] A property by number, as the game reads it with
    /// get_wind_prop.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// There is no such property.
    /// </exception>
    public int Property(int number) => number switch
    {
        0 => Y,
        1 => X,
        2 => Height,
        3 => Width,
        4 => CursorY,
        5 => CursorX,
        6 => LeftMargin,
        7 => RightMargin,
        8 => NewlineInterruptRoutine,
        9 => InterruptCountdown,
        10 => (int)Style,
        11 => ((int)Background << 8) | ((int)Foreground & 0xFF),
        12 => Font,
        13 => (FontHeight << 8) | (FontWidth & 0xFF),
        14 => (int)Attributes,
        15 => LineCount,
        16 => TrueForeground,
        17 => TrueBackground,
        _ => throw new ArgumentOutOfRangeException(nameof(number), number, "Window properties are numbered 0 to 17."),
    };

    /// <summary>
    /// [zm op:put_wind_prop] Writes a property by number. Only 0 to 15
    /// can be written; the true colors are the interpreter's.
    /// </summary>
    /// <returns>False if the property cannot be written.</returns>
    internal bool PutProperty(int number, int value)
    {
        switch (number)
        {
            case 0: Y = value; break;
            case 1: X = value; break;
            case 2: Height = value; break;
            case 3: Width = value; break;
            case 4: CursorY = value; break;
            case 5: CursorX = value; break;
            case 6: LeftMargin = value; break;
            case 7: RightMargin = value; break;
            case 8: NewlineInterruptRoutine = (ushort)value; break;
            case 9: InterruptCountdown = value; break;
            case 10: Style = (TextStyle)(value & 0x0F); break;
            case 11:
                Foreground = (ScreenColor)(sbyte)(value & 0xFF);
                Background = (ScreenColor)(sbyte)((value >> 8) & 0xFF);
                break;
            case 12: Font = value; break;
            case 13:
                FontWidth = value & 0xFF;
                FontHeight = (value >> 8) & 0xFF;
                break;
            case 14: Attributes = (WindowAttributes)(value & 0x0F); break;
            case 15: LineCount = value; break;
            default:
                return false;
        }

        return true;
    }
}

namespace Rezrov.AaMachine;

/// <summary>
/// [aam story] Where a style class wants its lines.
/// </summary>
public enum AaAlignment
{
    /// <summary>Against the left edge, which is the usual.</summary>
    Start,

    /// <summary>Centered in whatever width the class has.</summary>
    Center,

    /// <summary>Against the right edge.</summary>
    End,
}

/// <summary>
/// [aam story] The unit a measurement in a style sheet is given in.
/// </summary>
/// <remarks>
/// A style sheet is CSS, which measures in a dozen units. These are
/// the ones the games use, and anything else reads as no measurement
/// at all, which is the standing instruction about what a frontend
/// does not understand.
/// </remarks>
public enum AaUnit
{
    /// <summary>There is no such measurement.</summary>
    None,

    /// <summary>A multiple of the size of the text.</summary>
    Em,

    /// <summary>A multiple of the width of a character.</summary>
    Ch,

    /// <summary>A number of pixels.</summary>
    Pixels,

    /// <summary>A proportion of the width there is to fill.</summary>
    Percent,

    /// <summary>
    /// Whatever is left over, which a margin uses to ask for the room
    /// around a block to be shared out evenly.
    /// </summary>
    Auto,
}

/// <summary>
/// [aam story] The sides a margin, a padding or a border has.
/// </summary>
public enum AaSide
{
    /// <summary>Above the block.</summary>
    Top,

    /// <summary>To the right of it.</summary>
    Right,

    /// <summary>Below it.</summary>
    Bottom,

    /// <summary>To the left of it.</summary>
    Left,
}

/// <summary>
/// [aam story] Which way a style class asks a block to be set aside
/// from the run of the text.
/// </summary>
public enum AaFloat
{
    /// <summary>In the text, like everything else.</summary>
    None,

    /// <summary>Against the left edge, with the text beside it.</summary>
    Left,

    /// <summary>Against the right edge.</summary>
    Right,
}

/// <summary>
/// [aam story] What a style class has to say about one color.
/// </summary>
public enum AaColorKind
{
    /// <summary>
    /// Nothing, so the color around it goes on being used. A class
    /// that says nothing and a class that says "inherit" amount to the
    /// same thing.
    /// </summary>
    Inherit,

    /// <summary>A color of its own.</summary>
    Set,

    /// <summary>
    /// The color the frontend was using before the story said
    /// anything, whatever that was.
    /// </summary>
    Initial,
}

/// <summary>
/// [aam story] A measurement from a style sheet: how much, and of
/// what.
/// </summary>
/// <param name="Amount">
/// How many of the unit, which may be a fraction and may be negative.
/// </param>
/// <param name="Unit">What the amount is counted in.</param>
public readonly record struct AaLength(double Amount, AaUnit Unit)
{
    /// <summary>No measurement at all.</summary>
    public static AaLength None => default;

    /// <summary>Whether the style sheet gave no measurement here.</summary>
    public bool IsNone => Unit == AaUnit.None;

    /// <summary>
    /// Whether the measurement is none of anything. CSS lets a zero be
    /// written without a unit, and a zero is the same amount of every
    /// unit anyway, so it answers to all of them.
    /// </summary>
    public bool IsZero => !IsNone && Unit != AaUnit.Auto && Amount == 0;

    /// <summary>
    /// The measurement in whole ems, or <paramref name="fallback"/>
    /// where it is in some other unit. A fraction of an em rounds
    /// down, since a grid of characters has no half lines.
    /// </summary>
    public int WholeEms(int fallback) =>
        Unit == AaUnit.Em || IsZero ? (int)Math.Floor(Amount) : fallback;

    /// <summary>
    /// The measurement in whole characters, or null where it is in
    /// some other unit. This is the one measurement a grid of
    /// characters can take literally.
    /// </summary>
    public int? WholeCharacters =>
        Unit == AaUnit.Ch || IsZero ? (int)Math.Floor(Amount) : null;
}

/// <summary>
/// [aam story] A color from a style sheet, which may be a color, or
/// the frontend's own, or nothing at all.
/// </summary>
/// <param name="Kind">Which of those three it is.</param>
/// <param name="Value">
/// The color itself as 0xAARRGGBB, which means nothing unless
/// <paramref name="Kind"/> says a color was set.
/// </param>
/// <remarks>
/// The games wash a background over a box as often as they fill one,
/// so how much of the color there is has to be carried along with it.
/// A color named or written in digits is all of it.
/// </remarks>
public readonly record struct AaColor(AaColorKind Kind, uint Value)
{
    /// <summary>Nothing said, so the color around it stands.</summary>
    public static AaColor Inherit => default;

    /// <summary>Whether a color of its own was given.</summary>
    public bool IsSet => Kind == AaColorKind.Set;

    /// <summary>How much of it there is, from none to all.</summary>
    public byte Alpha => (byte)(Value >> 24);

    /// <summary>How much red.</summary>
    public byte Red => (byte)(Value >> 16);

    /// <summary>How much green.</summary>
    public byte Green => (byte)(Value >> 8);

    /// <summary>How much blue.</summary>
    public byte Blue => (byte)Value;
}

/// <summary>
/// [aam story] The line a style class asks to be drawn around a block.
/// </summary>
/// <param name="Width">How thick the line is.</param>
/// <param name="Line">
/// What sort of line it is, as CSS spells it: "solid", "dashed",
/// "none" and the rest. Null where the class named none.
/// </param>
/// <param name="Color">What color to draw it in.</param>
public readonly record struct AaBorder(AaLength Width, string? Line, AaColor Color)
{
    /// <summary>Nothing to draw.</summary>
    public static AaBorder None => default;

    /// <summary>
    /// Whether a line is drawn at all. CSS draws nothing for a border
    /// whose style was never named, and nothing for the two styles
    /// that mean there is no line.
    /// </summary>
    public bool IsDrawn =>
        Line is not null
        && !string.Equals(Line, "none", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(Line, "hidden", StringComparison.OrdinalIgnoreCase);
}

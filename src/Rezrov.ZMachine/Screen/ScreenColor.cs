namespace Rezrov.ZMachine.Screen;

/// <summary>
/// The color numbers of [zm 8.3.1].
/// </summary>
/// <remarks>
/// The first three are instructions rather than colors: keep the color
/// under the cursor, keep the current color, or go back to the default.
/// Colors 10, 11, 12, 15, and -1 exist only in Version 6. The names are
/// spelled the American way; only the opcode names keep the standard's
/// British spelling.
/// </remarks>
public enum ScreenColor : short
{
    UnderCursor = -1,

    Current = 0,

    Default = 1,

    Black = 2,

    Red = 3,

    Green = 4,

    Yellow = 5,

    Blue = 6,

    Magenta = 7,

    Cyan = 8,

    White = 9,

    LightGray = 10,

    MediumGray = 11,

    DarkGray = 12,

    Transparent = 15,
}

/// <summary>
/// The correspondence between color numbers and the 15-bit true colors
/// of [zm 8.3.7], and the nearest-color matching a minimal
/// implementation needs.
/// </summary>
public static class ScreenColors
{
    /// <summary>
    /// [zm 8.3.7] The special true color values: default, current, under
    /// the cursor, and transparent.
    /// </summary>
    public const short TrueDefault = -1;

    public const short TrueCurrent = -2;

    public const short TrueUnderCursor = -3;

    public const short TrueTransparent = -4;

    // [zm 8.3.1] The recommended equivalences, in color number order
    // from black (2) to dark gray (12).
    private static readonly ushort[] TrueColors =
    [
        0x0000, 0x001D, 0x0340, 0x03BD, 0x59A0, 0x7C1F, 0x77A0, 0x7FFF, 0x5AD6, 0x4631, 0x2D6B,
    ];

    /// <summary>
    /// [zm 8.3.1] Whether a number names an actual color, from black to
    /// dark gray, as opposed to an instruction like "current".
    /// </summary>
    public static bool IsActualColor(ScreenColor color) =>
        color is >= ScreenColor.Black and <= ScreenColor.DarkGray;

    /// <summary>
    /// [zm 8.3.1] Whether a color number is legal in a version. Colors
    /// 10 to 12, transparent, and under-the-cursor are Version 6 only.
    /// </summary>
    public static bool IsLegal(ScreenColor color, ZMachineVersion version) => color switch
    {
        ScreenColor.Current or ScreenColor.Default => true,
        >= ScreenColor.Black and <= ScreenColor.White => true,
        >= ScreenColor.LightGray and <= ScreenColor.DarkGray => version == ZMachineVersion.V6,
        ScreenColor.Transparent or ScreenColor.UnderCursor => version == ZMachineVersion.V6,
        _ => false,
    };

    /// <summary>
    /// [zm 8.3.1] The true color a color number stands for.
    /// </summary>
    /// <remarks>
    /// [zm 8.3.7] The true color values are 15-bit: bits 0 to 4 red, 5
    /// to 9 green, 10 to 14 blue, with $0000 black and $7FFF white.
    /// </remarks>
    public static ushort ToTrueColor(ScreenColor color)
    {
        if (!IsActualColor(color))
        {
            throw new ArgumentOutOfRangeException(nameof(color), color, "Only an actual color has a true color.");
        }

        return TrueColors[color - ScreenColor.Black];
    }

    /// <summary>
    /// [zm 8.3.7.1] The nearest standard color to a true color, which is
    /// the minimal implementation the standard describes: match to the
    /// closest of the standard colors and proceed as set_colour would.
    /// </summary>
    /// <remarks>
    /// Nearest by straight distance in the 5-bit red, green, and blue
    /// components, which is crude but predictable. Only the colors legal
    /// in the version are candidates.
    /// </remarks>
    public static ScreenColor Nearest(ushort trueColor, ZMachineVersion version)
    {
        var (red, green, blue) = Split(trueColor);
        var best = ScreenColor.Black;
        var bestDistance = int.MaxValue;

        for (var color = ScreenColor.Black; color <= ScreenColor.DarkGray; color++)
        {
            if (!IsLegal(color, version))
            {
                continue;
            }

            var (r, g, b) = Split(ToTrueColor(color));
            var distance = ((red - r) * (red - r)) + ((green - g) * (green - g)) + ((blue - b) * (blue - b));
            if (distance < bestDistance)
            {
                best = color;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static (int Red, int Green, int Blue) Split(ushort trueColor) =>
        (trueColor & 0x1F, (trueColor >> 5) & 0x1F, (trueColor >> 10) & 0x1F);
}

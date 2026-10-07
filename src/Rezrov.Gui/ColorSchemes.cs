using System.Globalization;
using Rezrov.ZMachine.Screen;

namespace Rezrov.Gui;

/// <summary>
/// The colors a player can choose for text and the page behind it, where
/// a game leaves them to the interpreter.
/// </summary>
/// <remarks>
/// What is chosen is a default, never an override. [zm 8.3] A Z-machine
/// game asks for "the default" by number and the interpreter says what
/// that is; [glk #stream_style_hints] a Glk game hints at the colors of
/// the styles it cares about and leaves the rest to the library; [aam
/// story] a Dialog story's style classes give colors of their own or
/// none. In every case a color the game chose for itself is kept, since
/// a game that prints a warning in red means it to be red.
///
/// A color is written the way a web page writes one, a hash and six hex
/// digits, and kept as 0x00RRGGBB, which is how Glk gives its own.
/// </remarks>
public static class ColorSchemes
{
    /// <summary>
    /// What the list says for leaving the colors to each game.
    /// </summary>
    public const string GameOwn = "Each game's own";

    /// <summary>What the list says for colors given by hand.</summary>
    public const string Custom = "Custom";

    /// <summary>
    /// The schemes offered by name, each a text color and the page it is
    /// set on.
    /// </summary>
    public static IReadOnlyList<Scheme> Named { get; } =
    [
        new("Light", 0x1A1A1A, 0xFAFAF7),
        new("Dark", 0xD8D8D8, 0x1E1E1E),
        new("Sepia", 0x5B4636, 0xF4ECD8),
    ];

    /// <summary>
    /// A color written as a hash and six hex digits, or as the six digits
    /// alone, or null for anything else.
    /// </summary>
    public static uint? Parse(string? text)
    {
        var digits = text?.Trim().TrimStart('#');

        return digits is { Length: 6 }
            && uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var color)
            ? color
            : null;
    }

    /// <summary>A color as a hash and six hex digits.</summary>
    public static string Written(uint color) =>
        "#" + (color & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);

    /// <summary>
    /// What the list should show for the colors given: leaving them to
    /// each game where neither is given, the scheme they are where they
    /// are one, and otherwise colors given by hand.
    /// </summary>
    public static string NameOf(uint? ink, uint? paper)
    {
        if (ink is null && paper is null)
        {
            return GameOwn;
        }

        return Named.FirstOrDefault(s => s.Ink == ink && s.Paper == paper)?.Name ?? Custom;
    }

    /// <summary>
    /// Whether a color is dark enough that what is drawn on it should be
    /// light, by how bright it looks rather than by its numbers alone.
    /// </summary>
    public static bool IsDark(uint color)
    {
        var red = (color >> 16) & 0xFF;
        var green = (color >> 8) & 0xFF;
        var blue = color & 0xFF;

        return ((0.2126 * red) + (0.7152 * green) + (0.0722 * blue)) / 255 < 0.4;
    }

    /// <summary>
    /// [zm 8.3.1] The two standard colors a Z-machine game is told are
    /// its defaults, for the colors the player chose.
    /// </summary>
    /// <remarks>
    /// [zm 8.3.3] The header has room only for a color number, so the
    /// game is told the nearest of the eight every version has, and the
    /// window draws the exact colors wherever the defaults are used. A
    /// color not chosen stays what it was, white text on black.
    ///
    /// The two are never told as the same number. A game that read the
    /// same color for both would have every reason to think its text
    /// was invisible, so the text takes the next nearest instead.
    /// </remarks>
    /// <param name="ink">The text color chosen, or null for none.</param>
    /// <param name="paper">The page chosen, or null for none.</param>
    public static (ScreenColor Ink, ScreenColor Paper) Standard(uint? ink, uint? paper)
    {
        var behind = paper is { } page ? Nearest(page, ScreenColor.Current) : ScreenColor.Black;
        var text = ink is { } written ? Nearest(written, behind) : ScreenColor.White;

        if (text == behind)
        {
            text = behind == ScreenColor.White ? ScreenColor.Black : ScreenColor.White;
        }

        return (text, behind);
    }

    /// <summary>
    /// The nearest of the eight standard colors, passing over one.
    /// </summary>
    private static ScreenColor Nearest(uint color, ScreenColor passed)
    {
        // [zm 8.3.7] The standard colors are given as fifteen bits, five
        // to each of red, green, and blue, so the chosen color is brought
        // down to the same five bits to be compared with them.
        var red = (int)(((color >> 16) & 0xFF) * 31 / 255);
        var green = (int)(((color >> 8) & 0xFF) * 31 / 255);
        var blue = (int)((color & 0xFF) * 31 / 255);

        var best = ScreenColor.Black;
        var bestDistance = int.MaxValue;

        for (var candidate = ScreenColor.Black; candidate <= ScreenColor.White; candidate++)
        {
            if (candidate == passed)
            {
                continue;
            }

            var standard = ScreenColors.ToTrueColor(candidate);
            var r = red - (standard & 0x1F);
            var g = green - ((standard >> 5) & 0x1F);
            var b = blue - ((standard >> 10) & 0x1F);
            var distance = (r * r) + (g * g) + (b * b);

            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>A scheme offered by name.</summary>
    /// <param name="Name">What the list calls it.</param>
    /// <param name="Ink">The color of the text, as 0x00RRGGBB.</param>
    /// <param name="Paper">The color of the page behind it.</param>
    public sealed record Scheme(string Name, uint Ink, uint Paper);
}

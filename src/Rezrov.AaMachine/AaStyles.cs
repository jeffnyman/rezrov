using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Rezrov.AaMachine;

/// <summary>
/// [aam story] The LOOK chunk: the style sheet, as classes a game can
/// put around its text.
/// </summary>
/// <remarks>
/// Each class is a handful of CSS key and value pairs, written as the
/// Dialog source wrote them, so the keys have to be matched without
/// regard to case. A frontend that cannot do CSS is expected to ignore
/// what it does not recognize rather than to refuse it, which leaves a
/// plain text frontend reading little more than the margins.
///
/// What is read here is the CSS the games are written in, not CSS at
/// large: measurements in ems, characters, pixels and percentages,
/// colors by the names CSS has always had and by their digits, the
/// margin, padding and border shorthands, and the handful of words
/// that say how text is set. Everything else is read past, which is
/// what the specification asks for, since a later compiler may write
/// things this one has never heard of.
///
/// How much of that a frontend can honor is the frontend's own
/// business. A grid of characters has no borders and no faces; a
/// screen that can draw has all of it.
/// </remarks>
public sealed class AaStyles
{
    /// <summary>
    /// The color names CSS has had since the beginning, which are the
    /// ones the Dialog manual tells authors to use. Anything more
    /// exotic is left to be written in digits.
    /// </summary>
    private static readonly Dictionary<string, uint> Named =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["black"] = 0x000000,
            ["silver"] = 0xC0C0C0,
            ["gray"] = 0x808080,
            ["grey"] = 0x808080,
            ["white"] = 0xFFFFFF,
            ["maroon"] = 0x800000,
            ["red"] = 0xFF0000,
            ["purple"] = 0x800080,
            ["fuchsia"] = 0xFF00FF,
            ["magenta"] = 0xFF00FF,
            ["green"] = 0x008000,
            ["lime"] = 0x00FF00,
            ["olive"] = 0x808000,
            ["yellow"] = 0xFFFF00,
            ["navy"] = 0x000080,
            ["blue"] = 0x0000FF,
            ["teal"] = 0x008080,
            ["aqua"] = 0x00FFFF,
            ["cyan"] = 0x00FFFF,
            ["orange"] = 0xFFA500,
        };

    /// <summary>
    /// [aam story] What CSS writes at the end of a value to say that
    /// it beats anything that would otherwise override it.
    /// </summary>
    /// <remarks>
    /// Nothing here has a cascade for one declaration to win in: a
    /// class is read on its own, and where a class names the same
    /// property twice the last one stands, which is the answer the
    /// word would have given anyway. So it is taken off and the value
    /// underneath is read.
    /// </remarks>
    private const string Important = "!important";

    /// <summary>
    /// The words CSS has for the sort of line a border is drawn with.
    /// A border shorthand is three things in any order, and the only
    /// way to tell which is which is to recognize them.
    /// </summary>
    private static readonly string[] Lines =
        [
            "none", "hidden", "dotted", "dashed", "solid",
            "double", "groove", "ridge", "inset", "outset",
        ];

    private readonly Dictionary<string, string>[] _classes;

    private AaStyles(Dictionary<string, string>[] classes) => _classes = classes;

    /// <summary>A story with no style sheet at all.</summary>
    public static AaStyles None { get; } = new AaStyles([]);

    /// <summary>How many classes the story defines.</summary>
    public int Count => _classes.Length;

    /// <summary>
    /// The value of one property of one class, or null if either the
    /// class or the property is not there.
    /// </summary>
    public string? Property(int styleClass, string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return styleClass >= 0 && styleClass < _classes.Length
            && _classes[styleClass].TryGetValue(key, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// One property read as a measurement, or no measurement at all
    /// where the class does not give one or gives something that is
    /// not a length.
    /// </summary>
    public AaLength Length(int styleClass, string key) =>
        ReadLength(Property(styleClass, key));

    /// <summary>
    /// A property measured in ems, rounded down, or
    /// <paramref name="fallback"/> where there is no such measurement.
    /// A margin is the one part of the style sheet a stream of text
    /// can honor, since a margin of so many ems is so many blank
    /// lines.
    /// </summary>
    public int Ems(int styleClass, string key, int fallback) =>
        Length(styleClass, key).WholeEms(fallback);

    /// <summary>
    /// A property measured in characters, which is the one width a
    /// grid of characters can take literally.
    /// </summary>
    public int? Characters(int styleClass, string key) =>
        Length(styleClass, key).WholeCharacters;

    /// <summary>
    /// The margin on one side of a block, whether the class named that
    /// side on its own or gave the shorthand for all four.
    /// </summary>
    public AaLength Margin(int styleClass, AaSide side) =>
        Around(styleClass, "margin", side);

    /// <summary>
    /// The padding on one side of a block, read the same way as a
    /// margin.
    /// </summary>
    public AaLength Padding(int styleClass, AaSide side) =>
        Around(styleClass, "padding", side);

    /// <summary>
    /// The line drawn around a block. CSS lets the thickness, the sort
    /// of line and the color be given together in any order or each on
    /// its own, and a name on its own wins where both were given.
    /// </summary>
    public AaBorder Border(int styleClass)
    {
        var border = ReadBorder(Property(styleClass, "border"));

        var width = Length(styleClass, "border-width");
        var line = Property(styleClass, "border-style");
        var color = ReadColor(Property(styleClass, "border-color"));

        return new AaBorder(
            width.IsNone ? border.Width : width,
            line ?? border.Line,
            color.Kind == AaColorKind.Inherit ? border.Color : color);
    }

    /// <summary>
    /// [aam story] The name the Dialog source gave a class, which the
    /// compiler keeps for the benefit of whoever is reading the style
    /// sheet, and which nothing depends on.
    /// </summary>
    public string? Name(int styleClass) => Property(styleClass, "style-name");

    /// <summary>
    /// Whether the class asks for heavier text, for ordinary text, or
    /// for whatever the text around it is already set in, which is
    /// what it means to say nothing.
    /// </summary>
    /// <remarks>
    /// The three answers are worth telling apart, because a class that
    /// says "normal" means to cancel the weight of the class outside
    /// it rather than to leave it alone.
    /// </remarks>
    public bool? Bold(int styleClass) =>
        Toggle(Property(styleClass, "font-weight"), "bold", "normal");

    /// <summary>
    /// Whether the class asks for leaning text, upright text, or
    /// whatever is already in force.
    /// </summary>
    /// <remarks>
    /// CSS leans text two ways: an italic is a face drawn for the
    /// purpose and an oblique is an upright face pushed over. No
    /// frontend here has both, so either one is a lean.
    /// </remarks>
    public bool? Italic(int styleClass) => Property(styleClass, "font-style") switch
    {
        null => null,
        var value when value.Equals("italic", StringComparison.OrdinalIgnoreCase) => true,
        var value when value.Equals("oblique", StringComparison.OrdinalIgnoreCase) => true,
        var value when value.Equals("normal", StringComparison.OrdinalIgnoreCase) => false,
        _ => null,
    };

    /// <summary>
    /// [aam story] Whether the class asks for the colors of the text
    /// and the page to be swapped, which is not CSS at all.
    /// </summary>
    /// <remarks>
    /// Some devices can turn their text inside out and have no other
    /// way to mark it, and the property was invented for them. A
    /// screen that can draw has better ways and need not read it.
    /// </remarks>
    public bool? IsReversed(int styleClass) =>
        Toggle(Property(styleClass, "-iftf-reverse-video"), "reverse", "none");

    /// <summary>Whether the class asks for heavier text.</summary>
    public bool IsBold(int styleClass) => Bold(styleClass) == true;

    /// <summary>Whether the class asks for leaning text.</summary>
    public bool IsItalic(int styleClass) => Italic(styleClass) == true;

    /// <summary>
    /// Whether the class asks for the text to be shouted, which is one
    /// of the few transforms a grid of characters can carry out itself.
    /// </summary>
    public bool IsUppercase(int styleClass) =>
        string.Equals(Property(styleClass, "text-transform"), "uppercase", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where the class wants its lines. Justified text is set to the
    /// left, since setting a line against both edges means stretching
    /// its spaces, which none of the frontends do.
    /// </summary>
    public AaAlignment Alignment(int styleClass) => Property(styleClass, "text-align") switch
    {
        null => AaAlignment.Start,
        var value when value.Equals("center", StringComparison.OrdinalIgnoreCase) => AaAlignment.Center,
        var value when value.Equals("right", StringComparison.OrdinalIgnoreCase) => AaAlignment.End,
        _ => AaAlignment.Start,
    };

    /// <summary>
    /// Which edge the class is set against, out of the run of the
    /// text, which is how a score is put at the end of a status line.
    /// </summary>
    public AaFloat FloatsTo(int styleClass) => Property(styleClass, "float") switch
    {
        null => AaFloat.None,
        var value when value.Equals("left", StringComparison.OrdinalIgnoreCase) => AaFloat.Left,
        var value when value.Equals("right", StringComparison.OrdinalIgnoreCase) => AaFloat.Right,
        _ => AaFloat.None,
    };

    /// <summary>
    /// Whether the class is set beside the text rather than in it, at
    /// the right, which is the only side a frontend that cannot flow
    /// text around a block has any use for.
    /// </summary>
    public bool FloatsRight(int styleClass) => FloatsTo(styleClass) == AaFloat.Right;

    /// <summary>
    /// Whether the class asks to be set below whatever has been
    /// floated to the side, rather than beside it.
    /// </summary>
    public bool ClearsFloats(int styleClass) => Property(styleClass, "clear") switch
    {
        null => false,
        var value => value.Equals("left", StringComparison.OrdinalIgnoreCase)
            || value.Equals("right", StringComparison.OrdinalIgnoreCase)
            || value.Equals("both", StringComparison.OrdinalIgnoreCase),
    };

    /// <summary>
    /// Whether the class asks not to be shown at all. What it holds
    /// still goes into a transcript, which is the point: a game can
    /// say something to the record without saying it to the screen.
    /// </summary>
    public bool IsHidden(int styleClass) =>
        string.Equals(Property(styleClass, "display"), "none", StringComparison.OrdinalIgnoreCase);

    /// <summary>The color the class asks its text to be drawn in.</summary>
    public AaColor Color(int styleClass) => ReadColor(Property(styleClass, "color"));

    /// <summary>The color it asks for behind the text.</summary>
    public AaColor BackgroundColor(int styleClass) =>
        ReadColor(Property(styleClass, "background-color"));

    /// <summary>
    /// The faces the class asks for, best first, as CSS writes them: a
    /// list of families ending in one of the generic names that every
    /// machine has something for. It is empty where the class says
    /// nothing, or says to go on with whatever is in use.
    /// </summary>
    public IReadOnlyList<string> Families(int styleClass)
    {
        var value = Property(styleClass, "font-family");

        if (value is null || string.Equals(value, "inherit", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return [.. value
            .Split(',')
            .Select(family => family.Trim().Trim('"', '\''))
            .Where(family => family.Length > 0)];
    }

    /// <summary>
    /// Whether the class asks for a face whose characters are all the
    /// same width, or for a proportional one, or says nothing.
    /// </summary>
    /// <remarks>
    /// A frontend with one face to spare reads no more of a family
    /// than this, which is what the Dialog manual tells authors to
    /// expect of it: the list is searched for the word every such list
    /// ends with.
    /// </remarks>
    public bool? IsMonospace(int styleClass)
    {
        var families = Families(styleClass);

        return families.Count == 0
            ? null
            : families.Any(family => string.Equals(family, "monospace", StringComparison.OrdinalIgnoreCase));
    }

    internal static AaStyles Read(ReadOnlySpan<byte> chunk)
    {
        if (chunk.IsEmpty)
        {
            return None;
        }

        if (chunk.Length < 2)
        {
            throw new InvalidDataException("The LOOK chunk is too short to say how many classes it holds.");
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(chunk);

        if (2 + (count * 2) > chunk.Length)
        {
            throw new InvalidDataException($"The LOOK chunk says it holds {count} classes but has room for fewer.");
        }

        var classes = new Dictionary<string, string>[count];

        for (var i = 0; i < count; i++)
        {
            var offset = BinaryPrimitives.ReadUInt16BigEndian(chunk[(2 + (i * 2))..]);

            if (offset > chunk.Length)
            {
                throw new InvalidDataException($"Style class {i} lies past the end of the LOOK chunk.");
            }

            classes[i] = ReadClass(chunk[offset..], i);
        }

        return new AaStyles(classes);
    }

    private static Dictionary<string, string> ReadClass(ReadOnlySpan<byte> at, int which)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var end = at.IndexOf((byte)0);

            if (end < 0)
            {
                throw new InvalidDataException($"Style class {which} is not terminated.");
            }

            // [aam story] A blank entry, which is the terminating null
            // on its own, ends the class.
            if (end == 0)
            {
                return properties;
            }

            var entry = Encoding.ASCII.GetString(at[..end]);
            at = at[(end + 1)..];

            // [aam story] Each entry is a CSS pair without its
            // semicolon, and anything that is not one is ignored
            // rather than refused, since a later compiler may write
            // things this one has never heard of.
            var colon = entry.IndexOf(':', StringComparison.Ordinal);

            if (colon > 0)
            {
                properties[entry[..colon].Trim()] = Plain(entry[(colon + 1)..]);
            }
        }
    }

    /// <summary>
    /// A value with the insistence taken off the end of it, so that
    /// every reader below sees the same shape of value whether the
    /// author insisted or not.
    /// </summary>
    private static string Plain(string value)
    {
        var trimmed = value.Trim();

        return trimmed.EndsWith(Important, StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^Important.Length].TrimEnd()
            : trimmed;
    }

    /// <summary>
    /// One side of a margin or a padding: the side named on its own if
    /// the class named it, and otherwise whatever the shorthand for
    /// all four sides works out to for that side.
    /// </summary>
    private AaLength Around(int styleClass, string key, AaSide side)
    {
        var named = Length(styleClass, $"{key}-{Edge(side)}");

        return named.IsNone ? Shorthand(Property(styleClass, key), side) : named;
    }

    private static string Edge(AaSide side) => side switch
    {
        AaSide.Top => "top",
        AaSide.Right => "right",
        AaSide.Bottom => "bottom",
        _ => "left",
    };

    /// <summary>
    /// The side a shorthand of one to four measurements gives to one
    /// side: one is every side, two are the pair up and down and the
    /// pair across, three name the top, the sides and the bottom, and
    /// four go round clockwise from the top.
    /// </summary>
    private static AaLength Shorthand(string? value, AaSide side)
    {
        if (value is null)
        {
            return AaLength.None;
        }

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var which = parts.Length switch
        {
            1 => 0,
            2 => (int)side % 2 == 0 ? 0 : 1,
            3 => side switch
            {
                AaSide.Top => 0,
                AaSide.Bottom => 2,
                _ => 1,
            },
            4 => (int)side,
            _ => -1,
        };

        return which < 0 ? AaLength.None : ReadLength(parts[which]);
    }

    /// <summary>
    /// A border shorthand, whose three parts may come in any order and
    /// any number, so each is recognized by what it looks like.
    /// </summary>
    private static AaBorder ReadBorder(string? value)
    {
        if (value is null)
        {
            return AaBorder.None;
        }

        var width = AaLength.None;
        var line = (string?)null;
        var color = AaColor.Inherit;

        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Lines.Contains(part, StringComparer.OrdinalIgnoreCase))
            {
                line = part;
            }
            else if (ReadLength(part) is { IsNone: false } measured)
            {
                width = measured;
            }
            else if (ReadColor(part) is { Kind: not AaColorKind.Inherit } read)
            {
                color = read;
            }
        }

        return new AaBorder(width, line, color);
    }

    /// <summary>
    /// A measurement, or none where the value is in a unit nothing
    /// here measures in or is not a measurement at all.
    /// </summary>
    private static AaLength ReadLength(string? value)
    {
        if (value is null)
        {
            return AaLength.None;
        }

        var text = value.Trim();

        if (string.Equals(text, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return new AaLength(0, AaUnit.Auto);
        }

        var at = 0;

        if (at < text.Length && text[at] is '-' or '+')
        {
            at++;
        }

        var start = at;

        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            at++;
        }

        var digits = at > start;

        if (at < text.Length && text[at] == '.')
        {
            at++;

            while (at < text.Length && char.IsAsciiDigit(text[at]))
            {
                at++;
                digits = true;
            }
        }

        if (!digits
            || !double.TryParse(text[..at], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
        {
            return AaLength.None;
        }

        return text[at..].Trim() switch
        {
            "%" => new AaLength(amount, AaUnit.Percent),

            // CSS lets a zero be written with no unit at all, and it
            // is the one amount that means the same in every unit.
            "" => amount == 0 ? new AaLength(0, AaUnit.Pixels) : AaLength.None,
            var unit when unit.Equals("em", StringComparison.OrdinalIgnoreCase) =>
                new AaLength(amount, AaUnit.Em),
            var unit when unit.Equals("ch", StringComparison.OrdinalIgnoreCase) =>
                new AaLength(amount, AaUnit.Ch),
            var unit when unit.Equals("px", StringComparison.OrdinalIgnoreCase) =>
                new AaLength(amount, AaUnit.Pixels),
            _ => AaLength.None,
        };
    }

    /// <summary>
    /// A color by one of the names CSS has always had, by its digits
    /// in three or six of them, or by its parts in a call to rgb or
    /// rgba. Anything else says nothing, which leaves the color around
    /// it standing.
    /// </summary>
    private static AaColor ReadColor(string? value)
    {
        if (value is null)
        {
            return AaColor.Inherit;
        }

        var text = value.Trim();

        if (string.Equals(text, "initial", StringComparison.OrdinalIgnoreCase))
        {
            return new AaColor(AaColorKind.Initial, 0);
        }

        if (Named.TryGetValue(text, out var named))
        {
            return new AaColor(AaColorKind.Set, Opaque(named));
        }

        if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return ReadParts(text);
        }

        if (!text.StartsWith('#'))
        {
            return AaColor.Inherit;
        }

        var digits = text[1..];

        if (!uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
        {
            return AaColor.Inherit;
        }

        // Three digits are the six with each one doubled, so that
        // "#888" and "#888888" are the same gray.
        return digits.Length switch
        {
            3 => new AaColor(
                AaColorKind.Set,
                Opaque((((packed >> 8) & 0xF) * 0x110000) + (((packed >> 4) & 0xF) * 0x1100) + ((packed & 0xF) * 0x11))),
            6 => new AaColor(AaColorKind.Set, Opaque(packed)),
            _ => AaColor.Inherit,
        };
    }

    /// <summary>
    /// A color written as its parts: three numbers from nought to two
    /// hundred and fifty-five, and for rgba a fourth saying how much
    /// of the color there is, from nought to one.
    /// </summary>
    private static AaColor ReadParts(string text)
    {
        var open = text.IndexOf('(', StringComparison.Ordinal);
        var close = text.LastIndexOf(')');

        if (open < 0 || close < open)
        {
            return AaColor.Inherit;
        }

        var parts = text[(open + 1)..close]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length is not (3 or 4))
        {
            return AaColor.Inherit;
        }

        var numbers = new double[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return AaColor.Inherit;
            }
        }

        var alpha = parts.Length == 4 ? Math.Clamp(numbers[3], 0, 1) : 1;

        return new AaColor(
            AaColorKind.Set,
            ((uint)Math.Round(alpha * 255) << 24)
            + (Part(numbers[0]) << 16)
            + (Part(numbers[1]) << 8)
            + Part(numbers[2]));
    }

    private static uint Part(double value) => (uint)Math.Clamp(Math.Round(value), 0, 255);

    /// <summary>
    /// A color with no washing out: a name or a run of digits is all
    /// of the color there is.
    /// </summary>
    private static uint Opaque(uint color) => 0xFF000000 + color;

    /// <summary>
    /// A property that is one word or its opposite: true for the one,
    /// false for the other, and null both for a class that says
    /// nothing and for one that says to go on as before.
    /// </summary>
    private static bool? Toggle(string? value, string yes, string no)
    {
        if (value is null)
        {
            return null;
        }

        if (string.Equals(value, yes, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(value, no, StringComparison.OrdinalIgnoreCase) ? false : null;
    }
}

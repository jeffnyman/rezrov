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
/// What a character cell display can honor is a small part of what the
/// games ask for: weight, slant, alignment, the margins, a width in
/// characters, and whether the text is to be shouted. Sizes in points
/// or ems, faces, borders, padding and rounded corners all belong to a
/// screen that can draw, and are read past here.
/// </remarks>
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

public sealed class AaStyles
{
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
    /// A property measured in ems, rounded down, or
    /// <paramref name="fallback"/> where there is no such measurement.
    /// A margin is the one part of the style sheet a stream of text
    /// can honor, since a margin of so many ems is so many blank
    /// lines.
    /// </summary>
    public int Ems(int styleClass, string key, int fallback)
    {
        return Measure(Property(styleClass, key), "em") ?? fallback;
    }

    // A whole number of some unit at the start of a value, or nothing
    // where the value is in another unit or is not a measurement at
    // all. A fraction of an em rounds down to none, which is what a
    // margin of a third of a line comes to on a grid of characters.
    private static int? Measure(string? value, string unit)
    {
        if (value is null)
        {
            return null;
        }

        var at = 0;

        while (at < value.Length && value[at] == ' ')
        {
            at++;
        }

        var start = at;

        while (at < value.Length && char.IsAsciiDigit(value[at]))
        {
            at++;
        }

        if (at == start)
        {
            // Something like ".3em", which is less than a line.
            return at < value.Length && value[at] == '.' ? 0 : null;
        }

        var rest = value[at..].TrimStart();

        if (rest.StartsWith('.'))
        {
            // A whole part and a fraction: the fraction is dropped.
            rest = rest[1..].TrimStart(['0', '1', '2', '3', '4', '5', '6', '7', '8', '9']).TrimStart();
        }

        return rest.StartsWith(unit, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value[start..at], CultureInfo.InvariantCulture, out var measure)
                ? measure
                : null;
    }

    /// <summary>
    /// [aam story] The name the Dialog source gave a class, which the
    /// compiler keeps for the benefit of whoever is reading the style
    /// sheet, and which nothing depends on.
    /// </summary>
    public string? Name(int styleClass) => Property(styleClass, "style-name");

    /// <summary>Whether the class asks for heavier text.</summary>
    public bool IsBold(int styleClass) =>
        string.Equals(Property(styleClass, "font-weight"), "bold", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the class asks for leaning text.</summary>
    public bool IsItalic(int styleClass) =>
        string.Equals(Property(styleClass, "font-style"), "italic", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the class asks for the text to be shouted, which is one
    /// of the few transforms a grid of characters can carry out itself.
    /// </summary>
    public bool IsUppercase(int styleClass) =>
        string.Equals(Property(styleClass, "text-transform"), "uppercase", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where the class wants its lines. Justified text is set to the
    /// left, since a terminal has no way to stretch the spaces.
    /// </summary>
    public AaAlignment Alignment(int styleClass) => Property(styleClass, "text-align") switch
    {
        null => AaAlignment.Start,
        var value when value.Equals("center", StringComparison.OrdinalIgnoreCase) => AaAlignment.Center,
        var value when value.Equals("right", StringComparison.OrdinalIgnoreCase) => AaAlignment.End,
        _ => AaAlignment.Start,
    };

    /// <summary>
    /// Whether the class is set beside the text rather than in it,
    /// which is how a score is put at the end of a status line.
    /// </summary>
    public bool FloatsRight(int styleClass) =>
        string.Equals(Property(styleClass, "float"), "right", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A property measured in characters, which is the one width a
    /// grid of characters can take literally.
    /// </summary>
    public int? Characters(int styleClass, string key) =>
        Measure(Property(styleClass, key), "ch");

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
                properties[entry[..colon].Trim()] = entry[(colon + 1)..].Trim();
            }
        }
    }
}

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
/// </remarks>
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
        if (Property(styleClass, key) is not { } value)
        {
            return fallback;
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

        return at > start
            && at < value.Length
            && value[at..].TrimStart().StartsWith("em", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value[start..at], CultureInfo.InvariantCulture, out var ems)
                ? ems
                : fallback;
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
                properties[entry[..colon].Trim()] = entry[(colon + 1)..].Trim();
            }
        }
    }
}

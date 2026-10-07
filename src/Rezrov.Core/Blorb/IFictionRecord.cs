using System.Globalization;
using System.Text;

namespace Rezrov.Core.Blorb;

/// <summary>
/// [babel #bibliographic] What a story's own iFiction record says about
/// it: its title and author, and whatever else its author chose to say.
/// </summary>
/// <remarks>
/// [babel #the-ifiction-format] A record is XML, but of a deliberately
/// narrow kind, and this reads that kind rather than XML in general. The
/// treaty allows a value only three escapes, for the ampersand and the
/// two angle brackets, and no tags at all except the paragraph breaks a
/// description may carry, so a value is text between a tag and its end.
/// A parser for XML in general would cost the published program three
/// and a third megabytes to read six fields. Being generous costs
/// nothing, though, so the other two named escapes and numeric
/// references are read as well, for any record that used them anyway.
///
/// Only the first story in a record is read, since a record kept in a
/// story's own Blorb file is about that story.
/// </remarks>
public sealed class IFictionRecord
{
    private IFictionRecord()
    {
    }

    /// <summary>[babel #ifid] The story's identifier.</summary>
    public string? Ifid { get; private init; }

    /// <summary>The title, which the treaty requires.</summary>
    public string? Title { get; private init; }

    /// <summary>The author, which the treaty requires.</summary>
    public string? Author { get; private init; }

    /// <summary>The line that goes under the title, if any.</summary>
    public string? Headline { get; private init; }

    /// <summary>
    /// When the story first appeared, as a year or a date.
    /// </summary>
    public string? FirstPublished { get; private init; }

    /// <summary>What kind of story it is, in the author's words.</summary>
    public string? Genre { get; private init; }

    /// <summary>
    /// The author's description, with its paragraphs parted by a line
    /// feed each.
    /// </summary>
    public string? Description { get; private init; }

    /// <summary>
    /// Reads a record, or returns null where there is no story in it.
    /// </summary>
    public static IFictionRecord? Read(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        if (Inside(xml, "story") is not { } story)
        {
            return null;
        }

        var identification = Inside(story, "identification") ?? string.Empty;
        var bibliographic = Inside(story, "bibliographic") ?? string.Empty;

        return new IFictionRecord
        {
            Ifid = Value(identification, "ifid"),
            Title = Value(bibliographic, "title"),
            Author = Value(bibliographic, "author"),
            Headline = Value(bibliographic, "headline"),
            FirstPublished = Value(bibliographic, "firstpublished"),
            Genre = Value(bibliographic, "genre"),
            Description = Paragraphs(Inside(bibliographic, "description")),
        };
    }

    /// <summary>
    /// What lies between the first opening of a tag and its close, or null
    /// where the tag is not there. An opening tag may carry attributes.
    /// </summary>
    private static string? Inside(string xml, string tag)
    {
        var at = 0;

        while ((at = xml.IndexOf("<" + tag, at, StringComparison.Ordinal)) >= 0)
        {
            var after = at + tag.Length + 1;

            // A tag whose name only begins with this one is another tag.
            if (after < xml.Length && xml[after] is '>' or ' ' or '\t' or '\r' or '\n')
            {
                var open = xml.IndexOf('>', after);
                if (open < 0)
                {
                    return null;
                }

                var close = xml.IndexOf("</" + tag + ">", open + 1, StringComparison.Ordinal);
                return close < 0 ? null : xml[(open + 1)..close];
            }

            at = after;
        }

        return null;
    }

    /// <summary>
    /// [babel #bibliographic] A single-line value: its escapes undone and
    /// its spacing made single, or null where it is missing or empty.
    /// </summary>
    private static string? Value(string section, string tag) =>
        Inside(section, tag) is { } raw && Spaced(Unescaped(raw)) is { Length: > 0 } value ? value : null;

    /// <summary>
    /// [babel #bibliographic] A description: paragraphs parted where the
    /// record says, and any run of white space within one read as a
    /// single space, as the treaty says it always is.
    /// </summary>
    private static string? Paragraphs(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var paragraphs = raw
            .Replace("<br />", "<br/>", StringComparison.Ordinal)
            .Replace("<br>", "<br/>", StringComparison.Ordinal)
            .Split("<br/>")
            .Select(paragraph => Spaced(Unescaped(paragraph)))
            .Where(paragraph => paragraph.Length > 0);

        var description = string.Join('\n', paragraphs);
        return description.Length > 0 ? description : null;
    }

    private static string Spaced(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// The escapes in a value undone: the three the treaty allows, the two
    /// other names XML gives, and numeric references.
    /// </summary>
    private static string Unescaped(string text)
    {
        if (!text.Contains('&', StringComparison.Ordinal))
        {
            return text;
        }

        var built = new StringBuilder(text.Length);
        var at = 0;

        while (at < text.Length)
        {
            var amp = text.IndexOf('&', at);
            var semi = amp < 0 ? -1 : text.IndexOf(';', amp);

            if (amp < 0 || semi < 0)
            {
                built.Append(text, at, text.Length - at);
                break;
            }

            built.Append(text, at, amp - at);
            var name = text[(amp + 1)..semi];

            if (Named(name) is { } named)
            {
                built.Append(named);
            }
            else if (Numbered(name) is { } numbered)
            {
                built.Append(numbered);
            }
            else
            {
                // Not an escape after all, so it is kept as it was.
                built.Append(text, amp, semi + 1 - amp);
            }

            at = semi + 1;
        }

        return built.ToString();
    }

    private static string? Named(string name) => name switch
    {
        "amp" => "&",
        "lt" => "<",
        "gt" => ">",
        "quot" => "\"",
        "apos" => "'",
        _ => null,
    };

    private static string? Numbered(string name)
    {
        if (name.Length < 2 || name[0] != '#')
        {
            return null;
        }

        var hex = name[1] is 'x' or 'X';
        var digits = hex ? name[2..] : name[1..];
        var style = hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None;

        return int.TryParse(digits, style, CultureInfo.InvariantCulture, out var code)
            && code is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF)
            ? char.ConvertFromUtf32(code)
            : null;
    }
}

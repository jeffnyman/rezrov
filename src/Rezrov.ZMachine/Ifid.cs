using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Rezrov.ZMachine;

/// <summary>
/// [babel legacy Z-code IFID] The name a Z-code story is known by
/// outside itself.
/// </summary>
/// <remarks>
/// A story file says a great deal about its own shape and nothing at
/// all about which game it is. The Treaty of Babel settles that by
/// giving every story an identifier: modern ones carry a branded
/// string, and older ones are named from three numbers in their
/// headers instead.
///
/// The treaty prefers the numbers to a hash of the file, and says why:
/// they read as something a person can recognize, and Infocom's own
/// files turn up with spurious tails, padded out to disc page
/// boundaries with zeros, which would give the same game two different
/// hashes.
/// </remarks>
public static class Ifid
{
    /// <summary>What a branded identifier is written between.</summary>
    private const string Brand = "UUID://";

    /// <summary>
    /// The three serial codes that name no date and earn no checksum,
    /// which are test or modified copies rather than releases.
    /// </summary>
    private static readonly string[] Untrusted = ["000000", "999999", "------"];

    /// <summary>
    /// The identifier of a story file, or null where the file is too
    /// short to have a header at all.
    /// </summary>
    /// <param name="story">The whole of the story file.</param>
    public static string? Of(ReadOnlySpan<byte> story)
    {
        if (story.Length < 0x1E)
        {
            return null;
        }

        var release = BinaryPrimitives.ReadUInt16BigEndian(story[0x02..]);
        var serial = Serial(story[0x12..0x18]);
        var checksum = BinaryPrimitives.ReadUInt16BigEndian(story[0x1C..]);

        // [babel #embed-formats] A story compiled after 2006 may carry
        // its identifier branded into it, and the treaty says where it
        // cannot be: a serial that names the eighties, the nineties or
        // the first six years of the century predates the practice, so
        // there is nothing to look for and the whole of the file need
        // not be searched.
        if (!Dated(serial) && Branded(story) is { } branded)
        {
            return branded;
        }

        // [babel legacy Z-code IFID] A serial that begins with a digit
        // other than eight is a compilation date from 1990 onward, and
        // those files carry a checksum worth trusting. The rest are
        // Infocom's, or are copies whose serial was altered, and are
        // named without one.
        var trusted = serial[0] is >= '0' and <= '9'
            && serial[0] != '8'
            && !Untrusted.Contains(serial, StringComparer.Ordinal);

        return trusted
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"ZCODE-{release}-{serial}-{checksum:X4}")
            : string.Create(CultureInfo.InvariantCulture, $"ZCODE-{release}-{serial}");
    }

    /// <summary>
    /// The six characters of the serial code, with anything that is not
    /// a letter or a digit written as a hyphen. A null is the usual
    /// case, and a few files carry characters that are not ASCII at
    /// all.
    /// </summary>
    private static string Serial(ReadOnlySpan<byte> code)
    {
        var serial = new StringBuilder(6);

        foreach (var value in code)
        {
            var character = (char)value;

            serial.Append(char.IsAsciiLetterOrDigit(character) ? character : '-');
        }

        return serial.ToString();
    }

    /// <summary>
    /// Whether the serial code names a date from a time before stories
    /// were branded with their own identifiers.
    /// </summary>
    private static bool Dated(string serial) =>
        serial[0] is '8' or '9'
        || (serial[0] == '0' && serial[1] is >= '0' and <= '5');

    /// <summary>
    /// [babel #embed-formats] The identifier branded into the story, or
    /// null where there is none. The treaty says its place cannot be
    /// relied on, so the whole file is searched, and that what lies
    /// between the slashes is digits, capital letters and hyphens.
    /// </summary>
    private static string? Branded(ReadOnlySpan<byte> story)
    {
        var mark = Encoding.ASCII.GetBytes(Brand);
        var at = story.IndexOf(mark);

        if (at < 0)
        {
            return null;
        }

        var rest = story[(at + mark.Length)..];
        var end = 0;

        while (end < rest.Length && Branded((char)rest[end]))
        {
            end++;
        }

        // The closing slashes have to be there, or this was some other
        // run of characters that began the same way.
        return end > 0 && end + 1 < rest.Length && rest[end] == '/' && rest[end + 1] == '/'
            ? Encoding.ASCII.GetString(rest[..end])
            : null;
    }

    private static bool Branded(char character) =>
        char.IsAsciiDigit(character) || char.IsAsciiLetterUpper(character) || character == '-';
}

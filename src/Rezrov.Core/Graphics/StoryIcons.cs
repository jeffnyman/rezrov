using System.Reflection;

namespace Rezrov.Core.Graphics;

/// <summary>
/// The marks a frontend can show for a story, carried inside the
/// program.
/// </summary>
/// <remarks>
/// A story file says nothing about itself that a player would
/// recognize, so a frontend with a window says it instead: whose game
/// it is, or failing that which machine it runs on. The marks are
/// classic icons of sixteen colors, white lettering on navy, in one
/// style, and the program's own is a modern one with an alpha.
///
/// They are built into the assembly rather than read from beside it,
/// because a published program is one file and because whoever runs it
/// has no reason to have the folder they came from.
/// </remarks>
public static class StoryIcons
{
    /// <summary>The mark of the company whose games these were.</summary>
    public const string Infocom = "infocom";

    /// <summary>The program's own mark, for anything else.</summary>
    public const string Rezrov = "rezrov";

    private static readonly Dictionary<string, byte[]?> Kept = [];

    /// <summary>
    /// The mark for a Z-machine version, which is the machine's own
    /// name and the number.
    /// </summary>
    public static string ForVersion(int version) => $"z{version}";

    /// <summary>
    /// The bytes of a mark, or null where there is no such one. They
    /// are an icon file, which <see cref="IcoReader"/> reads.
    /// </summary>
    public static byte[]? Read(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (Kept)
        {
            if (Kept.TryGetValue(name, out var known))
            {
                return known;
            }

            var bytes = Load(name);

            Kept[name] = bytes;
            return bytes;
        }
    }

    /// <summary>
    /// A mark decoded at the size asked for, or null where there is no
    /// such mark.
    /// </summary>
    public static Pixels? Pixels(string name, int size) =>
        Read(name) is { } bytes ? IcoReader.Read(bytes, size) : null;

    private static byte[]? Load(string name)
    {
        using var stream = typeof(StoryIcons).Assembly.GetManifestResourceStream($"{name}.ico");

        if (stream is null)
        {
            return null;
        }

        using var bytes = new MemoryStream();

        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}

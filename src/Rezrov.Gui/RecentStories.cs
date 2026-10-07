using Rezrov.Watching;

namespace Rezrov.Gui;

/// <summary>
/// The stories played most recently, newest first, for the Game menu to
/// offer again.
/// </summary>
/// <remarks>
/// Kept as a file of whole paths, one to a line, beside the kept options
/// and the maps, so that a player who wants to see or trim the list can
/// read it as easily as the program can.
///
/// A story is remembered as it opens, by the window playing it, so every
/// way of starting one counts the same: a double click, a command line,
/// Open Story, or this list itself. A story that has since been moved or
/// deleted stays in the file but is not offered, since choosing it could
/// only fail, and it drops off the end in time as others are played.
///
/// The list says what someone has been playing, which is theirs to keep
/// or not, so it can be cleared. Clearing forgets the list and nothing
/// else: the stories, their saves, and their maps are left alone.
/// </remarks>
public static class RecentStories
{
    /// <summary>How many stories the list holds.</summary>
    public const int Most = 10;

    /// <summary>
    /// What the file says first, for anyone who opens it.
    /// </summary>
    private const string Heading =
        "# Stories rezrov-gui opened most recently, newest first, one to a\n"
        + "# line. Clear Recent in the Game menu empties it.\n";

    /// <summary>
    /// Whether two paths name the same story. Windows and macOS do not
    /// tell a capital from a small letter in a file's name, so neither
    /// does this there.
    /// </summary>
    private static StringComparer Same =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>Where the list is kept.</summary>
    public static string Path() => System.IO.Path.Combine(MapStore.Home(), "recent");

    /// <summary>
    /// The stories in a file's text, newest first.
    /// </summary>
    /// <remarks>
    /// Only whole paths are taken, since a path relative to wherever some
    /// earlier window happened to be started is no way to find a story
    /// again. A comment, a blank line, a story named twice, and anything
    /// past the most the list holds are passed over, so a file edited by
    /// hand can be wrong without stopping the program.
    /// </remarks>
    public static IReadOnlyList<string> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#') && System.IO.Path.IsPathFullyQualified(line))
            .Distinct(Same)
            .Take(Most)
            .ToList();
    }

    /// <summary>
    /// A file's text for the given stories, in the order given.
    /// </summary>
    public static string Write(IEnumerable<string> stories)
    {
        ArgumentNullException.ThrowIfNull(stories);

        return Heading + string.Concat(stories.Select(story => story + "\n"));
    }

    /// <summary>
    /// The list with a story just opened put first, and taken out of
    /// wherever else it was, so that playing a story again moves it up
    /// rather than naming it twice.
    /// </summary>
    public static IReadOnlyList<string> With(IReadOnlyList<string> stories, string story)
    {
        ArgumentNullException.ThrowIfNull(stories);
        ArgumentNullException.ThrowIfNull(story);

        return [.. new[] { story }.Concat(stories.Where(s => !Same.Equals(s, story))).Take(Most)];
    }

    /// <summary>
    /// The stories kept on this machine, or none where nothing has been
    /// kept or the file cannot be read.
    /// </summary>
    public static IReadOnlyList<string> Load()
    {
        try
        {
            var path = Path();
            return File.Exists(path) ? Read(File.ReadAllText(path)) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The kept stories that can still be opened, which are the ones the
    /// menu offers.
    /// </summary>
    public static IReadOnlyList<string> Offered() => [.. Load().Where(File.Exists)];

    /// <summary>
    /// Puts a story at the top of the list as it opens.
    /// </summary>
    /// <remarks>
    /// A list that cannot be written is not worth stopping a game over,
    /// or even mentioning: the story plays all the same, and the worst
    /// that happens is that the menu does not offer it next time.
    /// </remarks>
    public static void Remember(string story)
    {
        ArgumentNullException.ThrowIfNull(story);

        try
        {
            Directory.CreateDirectory(MapStore.Home());
            File.WriteAllText(Path(), Write(With(Load(), System.IO.Path.GetFullPath(story))));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing to tell the player, for the reason above.
        }
    }

    /// <summary>Forgets every story on the list.</summary>
    public static void Clear()
    {
        try
        {
            File.Delete(Path());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be deleted can sometimes still be written,
            // and an empty list says the same thing.
            try
            {
                File.WriteAllText(Path(), Write([]));
            }
            catch (Exception again) when (again is IOException or UnauthorizedAccessException)
            {
                // Nothing more can be done, and the list is only a
                // convenience.
            }
        }
    }
}

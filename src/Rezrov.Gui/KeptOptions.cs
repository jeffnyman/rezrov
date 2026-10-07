using Rezrov.Watching;

namespace Rezrov.Gui;

/// <summary>
/// The options a player keeps from one session to the next, written as
/// they would be typed at a command line, one to a line.
/// </summary>
/// <remarks>
/// Kept in the words of the command line rather than in a format of
/// their own, so that they are read by the very code that reads a
/// command line and can never be checked differently from it, and so
/// that a player who opens the file finds options they already know.
///
/// Only the options about how the program looks and which machine it
/// says it is can be kept. Those are a player's preferences. The others
/// belong to one story or one session, a seed or a commands file or the
/// debugger, and a file that kept them would quietly do something to
/// every story opened afterwards. A line naming one of those is passed
/// over, as is anything else the file holds that is not a kept option,
/// so a file edited by hand can be wrong without stopping the program.
///
/// Whatever is typed at the command line comes after, and so wins, for
/// that one window.
/// </remarks>
public static class KeptOptions
{
    /// <summary>
    /// The options that can be kept, and whether each is given a value.
    /// </summary>
    public static IReadOnlyDictionary<string, bool> Keepable { get; } = new Dictionary<string, bool>
    {
        ["--font"] = true,
        ["--sans"] = true,
        ["--fixed"] = true,
        ["--size"] = true,
        ["--smoothing"] = true,
        ["--padding"] = true,
        ["--interpreter"] = true,
        ["--map"] = false,
        ["--tandy"] = false,
    };

    /// <summary>
    /// What the file says first, for anyone who opens it.
    /// </summary>
    private const string Heading =
        "# Options rezrov-gui keeps between sessions, one to a line, as they\n"
        + "# would be typed at a command line. Options typed there win.\n";

    /// <summary>Where the options are kept.</summary>
    public static string Path() => System.IO.Path.Combine(MapStore.Home(), "options");

    /// <summary>
    /// The kept options in a file's text, each as the arguments it would
    /// be at a command line.
    /// </summary>
    public static IReadOnlyList<string[]> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var options = new List<string[]>();

        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            // A comment or a blank line names no option that can be kept,
            // so it is passed over by the same test as anything else that
            // does not, and needs no rule of its own.
            var line = raw.Trim();

            // The option is the first word, and its value is the whole of
            // the rest, so that a font list keeps its commas and spaces.
            var space = line.IndexOfAny([' ', '\t']);
            var option = space < 0 ? line : line[..space];
            var value = space < 0 ? string.Empty : line[space..].Trim();

            if (!Keepable.TryGetValue(option, out var valued))
            {
                continue;
            }

            if (valued && value.Length > 0)
            {
                options.Add([option, value]);
            }
            else if (!valued)
            {
                options.Add([option]);
            }
        }

        return options;
    }

    /// <summary>
    /// A file's text for the given options, which are written in the
    /// order given and only if they can be kept.
    /// </summary>
    public static string Write(IEnumerable<string[]> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var text = new System.Text.StringBuilder(Heading);

        foreach (var option in options)
        {
            if (option.Length > 0 && Keepable.TryGetValue(option[0], out var valued))
            {
                text.Append(valued && option.Length > 1 ? $"{option[0]} {option[1].Trim()}" : option[0]);
                text.Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// The options kept on this machine, or none where nothing has been
    /// kept or the file cannot be read.
    /// </summary>
    public static IReadOnlyList<string[]> Load()
    {
        try
        {
            var path = Path();
            return File.Exists(path) ? Read(File.ReadAllText(path)) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be read keeps nothing, and the program
            // starts as it would have before any were kept.
            return [];
        }
    }

    /// <summary>
    /// Keeps the given options, and says what went wrong if they could not
    /// be kept.
    /// </summary>
    public static string? Save(IEnumerable<string[]> options)
    {
        try
        {
            var path = Path();
            Directory.CreateDirectory(MapStore.Home());
            File.WriteAllText(path, Write(options));
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }
}

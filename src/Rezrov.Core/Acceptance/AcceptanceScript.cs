namespace Rezrov.Core.Acceptance;

/// <summary>
/// An acceptance script: a game, a seed, and the commands to play, in a
/// small text file that a run can be repeated from exactly.
/// </summary>
/// <remarks>
/// The file is plain text. A line beginning with <c>!</c> is a
/// directive, written <c>! NAME=value</c>, and six are known: GAME
/// names the story file, SEED gives the session seed that makes the
/// game's dice repeat, BLORB names a resource file, INTERPRETER names
/// the machine the game should take itself to be on, since some games
/// behave differently by it, TANDY set to yes gives the game the
/// Tandy bit, which a few early ones read, and UPPER set to yes puts
/// the upper window into the play, for a game that draws its text
/// there rather than printing it. Paths are relative to the
/// script, not to wherever the interpreter happens to be run from, so a
/// script can sit beside the games it plays and still work from
/// anywhere. A SEED line after some commands is a change of seed that
/// takes effect when the play reaches it, so that each stretch of a
/// long script can have the seed that makes it go the way it should,
/// and settling one stretch never disturbs the ones before it. A line
/// beginning with <c>#</c> is a comment and a blank line is nothing.
/// A line of three backticks opens a fenced section, with a label after
/// the backticks if one helps, and nothing inside it is read until a
/// bare line of three backticks closes it, or the file ends. That lets
/// a script keep an alternative path through the game, or the rest of
/// a path still being worked out, without playing it. Every other line
/// is a command, in the format the Z-Machine's command files use, so a
/// key that is not a printable character is written as its code in
/// square brackets, or by name for the ones a menu is worked with:
/// <c>&lt;up&gt;</c>, <c>&lt;down&gt;</c>, <c>&lt;left&gt;</c>,
/// <c>&lt;right&gt;</c>, <c>&lt;escape&gt;</c>, and <c>&lt;space&gt;</c>,
/// one press per line. A command may be written with the prompt in
/// front, as <c>&gt; look</c>, which is how a transcript reads and how
/// a command that itself begins with <c>#</c>, <c>!</c>, or <c>&lt;</c>
/// is given.
///
/// The script's expected output lives beside it in a file of the same
/// name with the extension <c>.expected</c>, which the interpreter
/// writes on the first run and compares against on every run after.
/// </remarks>
public sealed class AcceptanceScript
{
    // The keys a script may name instead of numbering, as their ZSCII
    // codes: the cursor keys, escape, and the space bar.
    /// <summary>
    /// [glk #mouse_events] and [glk #link_events] A touch of a window
    /// or a link, as the command form a display understands: a click at
    /// a column and a row of the window, or the value of a link. Null
    /// for anything that is not one of the two.
    /// </summary>
    private static string? Pointer(string line)
    {
        var inside = line[1..^1].Trim();

        if (inside.StartsWith("click ", StringComparison.OrdinalIgnoreCase))
        {
            var at = inside[6..].Split(',');
            if (at.Length != 2 || !uint.TryParse(at[0].Trim(), out var column) || !uint.TryParse(at[1].Trim(), out var row))
            {
                throw new InvalidDataException($"{line} is not a click; a click is written <click column,row>, counting from zero at the window's top left corner");
            }

            return $"[click {column},{row}]";
        }

        if (inside.StartsWith("link ", StringComparison.OrdinalIgnoreCase))
        {
            if (!uint.TryParse(inside[5..].Trim(), out var value) || value == 0)
            {
                throw new InvalidDataException($"{line} is not a link; a link is written <link value>, and a link value is never zero");
            }

            return $"[link {value}]";
        }

        return null;
    }

    private static readonly Dictionary<string, int> KeyNames = new(StringComparer.Ordinal)
    {
        ["<up>"] = 129,
        ["<down>"] = 130,
        ["<left>"] = 131,
        ["<right>"] = 132,
        ["<escape>"] = 27,
        ["<space>"] = 32,
    };

    private AcceptanceScript(string scriptPath, string gamePath, string? blorbPath, string? interpreter, bool tandy, bool upper, bool graphics, int seed, IReadOnlyList<string> commands, IReadOnlyList<int> commandLines, IReadOnlyDictionary<int, int> seedChanges)
    {
        ScriptPath = scriptPath;
        GamePath = gamePath;
        BlorbPath = blorbPath;
        Interpreter = interpreter;
        Tandy = tandy;
        Upper = upper;
        Graphics = graphics;
        Seed = seed;
        Commands = commands;
        CommandLines = commandLines;
        SeedChanges = seedChanges;
    }

    /// <summary>The script file itself.</summary>
    public string ScriptPath { get; }

    /// <summary>The story file to play, as a full path.</summary>
    public string GamePath { get; }

    /// <summary>The resource file to use, as a full path, if any.</summary>
    public string? BlorbPath { get; }

    /// <summary>
    /// The machine the game should take itself to be on, as written,
    /// or null for the interpreter's own choice. The runner reads it,
    /// since which names exist is the story format's business.
    /// </summary>
    public string? Interpreter { get; }

    /// <summary>
    /// Whether the game is told the Tandy bit is set, as
    /// <c>--tandy</c> does.
    /// </summary>
    public bool Tandy { get; }

    /// <summary>
    /// Whether the upper window is put into the play whenever the game
    /// pauses for input and has changed it, for a game that draws its
    /// text there. Off, the play is the lower window alone, as on a
    /// console.
    /// </summary>
    public bool Upper { get; }

    /// <summary>
    /// [glk #graphics_testing] Whether the game is told it may open a
    /// graphics window and draw in it. What it draws cannot be shown as
    /// text, so the play records what the game says about its drawing
    /// rather than the drawing; off, the game is told there are no
    /// graphics at all.
    /// </summary>
    public bool Graphics { get; }

    /// <summary>The seed for the game's random numbers at the start.</summary>
    public int Seed { get; }

    /// <summary>
    /// Seeds to change to partway through, by the index of the command
    /// they come before. An index equal to the number of commands is a
    /// change that comes after the last of them, for a game that goes on
    /// from where the script ends.
    /// </summary>
    public IReadOnlyDictionary<int, int> SeedChanges { get; }

    /// <summary>The commands to play, in order.</summary>
    public IReadOnlyList<string> Commands { get; }

    /// <summary>
    /// The line of the script each command came from, counting from 1,
    /// so that a difference in the play can be traced back.
    /// </summary>
    public IReadOnlyList<int> CommandLines { get; }

    /// <summary>Where the expected output lives, beside the script.</summary>
    public string ExpectedPath => Path.ChangeExtension(ScriptPath, ".expected");

    /// <summary>
    /// Reads a script from a file.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The script is missing a directive or has one it cannot use.
    /// </exception>
    public static AcceptanceScript Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Parse(File.ReadAllText(path), path);
    }

    /// <summary>
    /// Parses a script's text, resolving its paths against the directory
    /// the script is in.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The script is missing a directive or has one it cannot use.
    /// </exception>
    public static AcceptanceScript Parse(string text, string path)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(path);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? fullPath;
        var name = Path.GetFileName(path);

        string? game = null;
        string? blorb = null;
        string? interpreter = null;
        var tandy = false;
        var upper = false;
        var graphics = false;
        int? seed = null;
        var commands = new List<string>();
        var commandLines = new List<int>();
        var seedChanges = new Dictionary<int, int>();
        int? fencedFrom = null;

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var label = trimmed[3..].Trim();

                if (fencedFrom is null)
                {
                    fencedFrom = i + 1;
                }
                else if (label.Length == 0)
                {
                    fencedFrom = null;
                }
                else
                {
                    throw new InvalidDataException($"{name}, line {i + 1}: a fenced section is still open from line {fencedFrom}; close it with a bare ``` first");
                }

                continue;
            }

            if (fencedFrom is not null || trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            if (trimmed[0] == '>')
            {
                commands.Add(trimmed[1..].TrimStart());
                commandLines.Add(i + 1);
                continue;
            }

            // A named key is written as its code, which is what the
            // command file format takes; a name that is not a key is
            // more likely a typo than a command, and is refused so that
            // its letters are not typed into the game.
            if (trimmed[0] == '<' && trimmed[^1] == '>')
            {
                // [glk #mouse_events] and [glk #link_events] A game may
                // be waiting for the pointer rather than the keyboard,
                // so a script can touch a window or select a link.
                if (Pointer(trimmed) is { } pointer)
                {
                    commands.Add(pointer);
                    commandLines.Add(i + 1);
                    continue;
                }

                if (!KeyNames.TryGetValue(trimmed.ToLowerInvariant(), out var code))
                {
                    throw new InvalidDataException($"{name}, line {i + 1}: {trimmed} is not a key; the keys are {string.Join(", ", KeyNames.Keys)}, or <click column,row> and <link value>, and a command that begins with < is written with the prompt in front");
                }

                commands.Add($"[{code}]");
                commandLines.Add(i + 1);
                continue;
            }

            if (trimmed[0] != '!')
            {
                commands.Add(line);
                commandLines.Add(i + 1);
                continue;
            }

            var directive = trimmed[1..].Trim();
            var equals = directive.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                throw new InvalidDataException($"{name}, line {i + 1}: a directive is written ! NAME=value");
            }

            var key = directive[..equals].Trim();
            var value = directive[(equals + 1)..].Trim();

            switch (key.ToUpperInvariant())
            {
                case "GAME":
                    game = Path.GetFullPath(Path.Combine(directory, value));
                    break;
                case "BLORB":
                    blorb = Path.GetFullPath(Path.Combine(directory, value));
                    break;
                case "INTERPRETER":
                    interpreter = value;
                    break;
                case "TANDY":
                    tandy = value.ToUpperInvariant() switch
                    {
                        "1" or "YES" or "ON" or "TRUE" => true,
                        "0" or "NO" or "OFF" or "FALSE" => false,
                        _ => throw new InvalidDataException($"{name}, line {i + 1}: TANDY must be yes or no"),
                    };
                    break;
                case "UPPER":
                    upper = value.ToUpperInvariant() switch
                    {
                        "1" or "YES" or "ON" or "TRUE" => true,
                        "0" or "NO" or "OFF" or "FALSE" => false,
                        _ => throw new InvalidDataException($"{name}, line {i + 1}: UPPER must be yes or no"),
                    };
                    break;
                case "GRAPHICS":
                    graphics = value.ToUpperInvariant() switch
                    {
                        "1" or "YES" or "ON" or "TRUE" => true,
                        "0" or "NO" or "OFF" or "FALSE" => false,
                        _ => throw new InvalidDataException($"{name}, line {i + 1}: GRAPHICS must be yes or no"),
                    };
                    break;
                case "SEED":
                    if (!int.TryParse(value, out var parsed) || parsed < 1)
                    {
                        throw new InvalidDataException($"{name}, line {i + 1}: SEED must be a whole number of 1 or more");
                    }

                    if (commands.Count == 0)
                    {
                        seed = parsed;
                    }
                    else if (seed is null)
                    {
                        throw new InvalidDataException($"{name}, line {i + 1}: the first SEED directive must come before the first command");
                    }
                    else
                    {
                        seedChanges[commands.Count] = parsed;
                    }

                    break;
                default:
                    throw new InvalidDataException($"{name}, line {i + 1}: there is no {key} directive; the directives are GAME, SEED, BLORB, INTERPRETER, TANDY, UPPER, and GRAPHICS");
            }
        }

        if (game is null)
        {
            throw new InvalidDataException($"{name}: no GAME directive names the story file");
        }

        if (seed is null)
        {
            throw new InvalidDataException($"{name}: no SEED directive gives the random number seed");
        }

        return new AcceptanceScript(fullPath, game, blorb, interpreter, tandy, upper, graphics, seed.Value, commands, commandLines, seedChanges);
    }
}

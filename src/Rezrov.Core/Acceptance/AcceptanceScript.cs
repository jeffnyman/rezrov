namespace Rezrov.Core.Acceptance;

/// <summary>
/// An acceptance script: a game, a seed, and the commands to play, in a
/// small text file that a run can be repeated from exactly.
/// </summary>
/// <remarks>
/// The file is plain text. A line beginning with <c>!</c> is a
/// directive, written <c>! NAME=value</c>, and four are known: GAME
/// names the story file, SEED gives the session seed that makes the
/// game's dice repeat, BLORB names a resource file, and INTERPRETER
/// names the machine the game should take itself to be on, since some
/// games behave differently by it. Paths are relative to the script, not to
/// wherever the interpreter happens to be run from, so a script can sit
/// beside the games it plays and still work from anywhere. A SEED line
/// after some commands is a change of seed that takes effect when the
/// play reaches it, so that each stretch of a long script can have the
/// seed that makes it go the way it should, and settling one stretch
/// never disturbs the ones before it. A line
/// beginning with <c>#</c> is a comment and a blank line is nothing.
/// A line of three backticks opens a fenced section, with a label after
/// the backticks if one helps, and nothing inside it is read until a
/// bare line of three backticks closes it, or the file ends. That lets
/// a script keep an alternative path through the game, or the rest of
/// a path still being worked out, without playing it. Every other line
/// is a command, in the format the Z-Machine's command files use, so a
/// key that is not a printable character is written as its code in
/// square brackets. A command may be written with the prompt in front,
/// as <c>&gt; look</c>, which is how a transcript reads and how a
/// command that itself begins with <c>#</c> or <c>!</c> is given.
///
/// The script's expected output lives beside it in a file of the same
/// name with the extension <c>.expected</c>, which the interpreter
/// writes on the first run and compares against on every run after.
/// </remarks>
public sealed class AcceptanceScript
{
    private AcceptanceScript(string scriptPath, string gamePath, string? blorbPath, string? interpreter, int seed, IReadOnlyList<string> commands, IReadOnlyList<int> commandLines, IReadOnlyDictionary<int, int> seedChanges)
    {
        ScriptPath = scriptPath;
        GamePath = gamePath;
        BlorbPath = blorbPath;
        Interpreter = interpreter;
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
                    throw new InvalidDataException($"{name}, line {i + 1}: there is no {key} directive; the directives are GAME, SEED, BLORB, and INTERPRETER");
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

        return new AcceptanceScript(fullPath, game, blorb, interpreter, seed.Value, commands, commandLines, seedChanges);
    }
}

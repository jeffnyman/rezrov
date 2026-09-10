using Rezrov.Core.Acceptance;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Sound;
using Rezrov.ZMachine.Streams;

namespace Rezrov.Cli;

/// <summary>
/// How an acceptance run came to an end.
/// </summary>
public enum AcceptanceEnding
{
    /// <summary>The game quit.</summary>
    Quit,

    /// <summary>The script ran out while the game wanted more.</summary>
    ScriptEnded,

    /// <summary>The game reached something the interpreter cannot do yet.</summary>
    NotSupported,

    /// <summary>The game or the interpreter failed.</summary>
    Failed,
}

/// <summary>
/// What an acceptance run produced: the text of the play, how it ended,
/// and what the interpreter noticed along the way.
/// </summary>
/// <param name="Output">Everything the game printed, the commands included.</param>
/// <param name="Ending">How the run stopped.</param>
/// <param name="Message">Why, when the ending was not a normal one.</param>
/// <param name="RuntimeErrors">[zm A] What the game did that it should not have.</param>
/// <param name="CommandOffsets">
/// How long <paramref name="Output"/> was when each command was read,
/// which is how a line of the play is traced to a line of the script.
/// </param>
public sealed record AcceptanceResult(string Output, AcceptanceEnding Ending, string? Message, IReadOnlyList<string> RuntimeErrors, IReadOnlyList<int> CommandOffsets)
{
    /// <summary>
    /// The whole record of the run as one text: the output, and after
    /// it a line for an abnormal ending and one for each runtime error.
    /// This is what an expected file holds, so that a game that starts
    /// failing, or stops failing, changes the text and shows up.
    /// </summary>
    public string Text
    {
        get
        {
            var text = new System.Text.StringBuilder(Output);

            if (Ending is AcceptanceEnding.NotSupported or AcceptanceEnding.Failed || RuntimeErrors.Count > 0)
            {
                if (text.Length > 0 && text[^1] != '\n')
                {
                    text.Append('\n');
                }

                if (Ending == AcceptanceEnding.NotSupported)
                {
                    text.Append("[rezrov: stopped: ").Append(Message).Append("]\n");
                }
                else if (Ending == AcceptanceEnding.Failed)
                {
                    text.Append("[rezrov: error: ").Append(Message).Append("]\n");
                }

                foreach (var error in RuntimeErrors)
                {
                    text.Append("[rezrov: ").Append(error).Append("]\n");
                }
            }

            return text.ToString();
        }
    }
}

/// <summary>
/// Where an expected text and a play first part company.
/// </summary>
/// <param name="Line">The line of the expected text, counting from 1.</param>
/// <param name="Description">What was expected there and what came instead.</param>
public sealed record AcceptanceDifference(int Line, string Description);

/// <summary>
/// Plays an acceptance script through the interpreter and compares what
/// came out with what was expected.
/// </summary>
/// <remarks>
/// The run uses the plainest frontend there is, the same text stream
/// the console program prints, with the script's commands played as
/// [zm 10.2] the file of commands and nothing behind it, so that a
/// script that runs out ends the game rather than waiting. The random
/// numbers come from the script's seed, [zm 2.4.2] so the play is the
/// same every time, which is the whole point.
/// </remarks>
public static class AcceptanceRun
{
    /// <summary>
    /// Plays the script and returns what happened, or null after saying
    /// on <paramref name="errors"/> why the game could not be loaded.
    /// With a <paramref name="display"/>, the play is shown there as it
    /// happens, which is how someone building a script up sees where a
    /// command went wrong. With a <paramref name="keyboard"/>, the game
    /// goes on from where the script ends, taking commands from there,
    /// which is how someone finds out what the next line of the script
    /// should be.
    /// </summary>
    public static AcceptanceResult? Play(AcceptanceScript script, TextWriter errors, TextWriter? display = null, IInput? keyboard = null)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(errors);

        if (StoryLoader.Load(script.GamePath, script.BlorbPath, errors) is not { } story)
        {
            return null;
        }

        InterpreterNumber? machine = null;
        if (script.Interpreter is { } wanted)
        {
            if (!InterpreterNumbers.TryParse(wanted, out var number))
            {
                errors.WriteLine($"rezrov: {Path.GetFileName(script.ScriptPath)}: there is no interpreter called {wanted}; the machines are {string.Join(", ", InterpreterNumbers.AllNames)}");
                return null;
            }

            machine = number;
        }

        var memory = new ZMemory(story.Bytes);
        var header = new StoryHeader(memory);
        var output = new StringWriter();
        using var files = new AcceptanceFiles();
        using var screenWriter = display is null ? null : new TeeWriter(output, display);
        var random = new RandomGenerator(script.Seed);

        // [zm 10.2] With nothing behind the file of commands, the run
        // ends when the script does; with a keyboard behind it, the
        // player carries on from there and is told so.
        var interpreter = new Interpreter(
            memory,
            new TextWriterScreen((TextWriter?)screenWriter ?? output),
            keyboard is null
                ? new TextReaderInput(TextReader.Null, header, memory)
                : new AnnouncedInput(keyboard, errors),
            random,
            files,
            new SilentSound(),
            machine);

        if (story.Resources is not null)
        {
            try
            {
                interpreter.UseResources(story.Resources);
            }
            catch (InvalidDataException e)
            {
                // [blorb 6] Complain righteously, then carry on without.
                errors.WriteLine($"rezrov: ignoring the resource file: {e.Message}");
            }
        }

        var commands = new MarkedCommands(script, output, random);
        interpreter.PlayCommands(commands);

        var ending = AcceptanceEnding.Quit;
        string? message = null;

        try
        {
            interpreter.Run();
        }
        catch (NotSupportedException e)
        {
            ending = AcceptanceEnding.NotSupported;
            message = e.Message;
        }
        catch (EndOfStreamException)
        {
            ending = AcceptanceEnding.ScriptEnded;
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException)
        {
            ending = AcceptanceEnding.Failed;
            message = e.Message;
        }
        finally
        {
            interpreter.Streams.Flush();
            screenWriter?.Flush();
        }

        return new AcceptanceResult(output.ToString(), ending, message, interpreter.RuntimeErrors, commands.Offsets);
    }

    /// <summary>
    /// Finds the first place two texts differ, line by line, or returns
    /// null when they are the same. Line endings do not count, since an
    /// expected file may have been through a tool that changed them.
    /// </summary>
    public static AcceptanceDifference? FirstDifference(string expected, string actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        var expectedLines = Lines(expected);
        var actualLines = Lines(actual);
        var common = Math.Min(expectedLines.Length, actualLines.Length);

        for (var i = 0; i < common; i++)
        {
            if (!string.Equals(expectedLines[i], actualLines[i], StringComparison.Ordinal))
            {
                return new AcceptanceDifference(i + 1, $"  expected: {expectedLines[i]}\n  actual:   {actualLines[i]}");
            }
        }

        if (expectedLines.Length > actualLines.Length)
        {
            return new AcceptanceDifference(common + 1, $"the play ends after line {actualLines.Length}, but {expectedLines.Length - actualLines.Length} more were expected, the first being\n  expected: {expectedLines[common]}");
        }

        if (actualLines.Length > expectedLines.Length)
        {
            return new AcceptanceDifference(common + 1, $"the play goes on for {actualLines.Length - expectedLines.Length} lines past the end, starting with\n  actual:   {actualLines[common]}");
        }

        return null;
    }

    /// <summary>
    /// Says where a difference is in both files: the line of the
    /// expected file, and the line of the script whose command the play
    /// was answering at that point.
    /// </summary>
    public static string Describe(AcceptanceDifference difference, AcceptanceResult result, AcceptanceScript script)
    {
        ArgumentNullException.ThrowIfNull(difference);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(script);

        var expectedName = Path.GetFileName(script.ExpectedPath);
        var where = $"line {difference.Line} of {expectedName} differs";

        // The end of the differing line in the play, and the last
        // command read before that point. The echo of a command follows
        // the prompt on the same line, so the end of the line is what
        // places it.
        var lines = Lines(result.Text);
        var end = 0;
        for (var i = 0; i < Math.Min(difference.Line, lines.Length); i++)
        {
            end += lines[i].Length + 1;
        }

        var command = -1;
        for (var i = 0; i < result.CommandOffsets.Count && result.CommandOffsets[i] <= end; i++)
        {
            command = i;
        }

        if (command >= 0 && command < script.CommandLines.Count)
        {
            where += $", while playing line {script.CommandLines[command]} of the script, \"{script.Commands[command]}\"";
        }
        else if (command < 0)
        {
            where += ", before the first command";
        }

        return where + "\n" + difference.Description;
    }

    // The lines of a text, not counting the end of the last one as the
    // start of an empty line after it.
    private static string[] Lines(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (normalized.EndsWith('\n'))
        {
            normalized = normalized[..^1];
        }

        return normalized.Split('\n');
    }

    /// <summary>
    /// The script's commands as the file of commands, noting how much
    /// output there was when each one was read, and changing the seed
    /// where the script says to, just before the command it comes
    /// before is handed over.
    /// </summary>
    private sealed class MarkedCommands(AcceptanceScript script, StringWriter output, RandomGenerator random) : TextReader
    {
        private int _next;

        public List<int> Offsets { get; } = [];

        public override string? ReadLine()
        {
            // [zm 2.4.2] Reseeding as the next command is read means the
            // game's rolls for that command onward come from the new
            // sequence, and nothing before it is touched.
            if (_next <= script.Commands.Count && script.SeedChanges.TryGetValue(_next, out var seed))
            {
                random.Seed(seed);
            }

            if (_next >= script.Commands.Count)
            {
                // Past the end for good, so a change after the last
                // command is applied once and not on every later read.
                _next = script.Commands.Count + 1;
                return null;
            }

            Offsets.Add(output.GetStringBuilder().Length);
            return script.Commands[_next++];
        }
    }

    /// <summary>
    /// The keyboard the game falls back on when the script ends, which
    /// says so the first time it is asked, so the player knows where
    /// the script stopped and they began.
    /// </summary>
    private sealed class AnnouncedInput(IInput inner, TextWriter errors) : IInput
    {
        private bool _announced;

        public bool SupportsTimedInput => inner.SupportsTimedInput;

        public LineInput ReadLine(LineInputRequest request)
        {
            Announce();
            return inner.ReadLine(request);
        }

        public ushort ReadKey(InputTimer? timer)
        {
            Announce();
            return inner.ReadKey(timer);
        }

        private void Announce()
        {
            if (!_announced)
            {
                _announced = true;
                errors.WriteLine();
                errors.WriteLine("rezrov: the script has ended; the game is yours from here");
            }
        }
    }

    /// <summary>
    /// [zm 9.1] Nothing to hear in a run nobody is listening to.
    /// </summary>
    private sealed class SilentSound : ISound
    {
        public bool CanPlaySounds => false;

        public void Bleep(int number)
        {
        }

        public void Prepare(SoundResource sound)
        {
        }

        public void Play(SoundResource sound, int volume, int repeats, Action cycleEnded)
        {
        }

        public void StopPlaying(SoundResource sound)
        {
        }

        public void Finish(SoundResource sound)
        {
        }
    }
}

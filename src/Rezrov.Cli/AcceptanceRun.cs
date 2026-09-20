using Rezrov.AaMachine;
using Rezrov.AaMachine.Execution;
using Rezrov.Core;
using Rezrov.Core.Acceptance;
using Rezrov.Glulx;
using Rezrov.Glulx.Glk;
using Rezrov.ZMachine;
using GlulxMachine = Rezrov.Glulx.Execution.GlulxMachine;
using GlulxRandom = Rezrov.Glulx.Execution.GlulxRandom;
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
/// <param name="RuntimeErrors">
/// What the game did that it should not have: [zm A] the Z-Machine's
/// runtime errors, or the Glk library's warnings.
/// </param>
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
/// <param name="RecordingEnded">
/// Whether the two agree as far as the expected text goes and the play
/// simply carries on past it, as happens when a script grows after its
/// recording was made.
/// </param>
public sealed record AcceptanceDifference(int Line, string Description, bool RecordingEnded = false);

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
/// same every time, which is the whole point. A Glulx game plays the
/// same way through Glk: the commands are the lines its display reads,
/// its files live in memory for the run, and the seed goes to the
/// machine's random number generator.
/// </remarks>
public static class AcceptanceRun
{
    // [aam text] What a bare return means to a story waiting on a
    // single keypress.
    private const int Return = 0x0d;

    /// <summary>
    /// Plays the script and returns what happened, or null after saying
    /// on <paramref name="errors"/> why the game could not be loaded.
    /// With a <paramref name="display"/>, the play is shown there as it
    /// happens, which is how someone building a script up sees where a
    /// command went wrong. With <paramref name="resume"/>, the game goes
    /// on from where the script ends, taking commands from the console,
    /// which is how someone finds out what the next line of the script
    /// should be.
    /// </summary>
    public static AcceptanceResult? Play(AcceptanceScript script, TextWriter errors, TextWriter? display = null, bool resume = false)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(errors);

        if (StoryLoader.Load(script.GamePath, script.BlorbPath, errors) is not { } story)
        {
            return null;
        }

        if (story.Format == StoryFormat.Glulx)
        {
            return PlayGlulx(script, story, errors, display, resume);
        }

        if (story.Format == StoryFormat.AaMachine)
        {
            return PlayAaMachine(script, story, errors, display, resume);
        }

        if (story.Format != StoryFormat.ZMachine)
        {
            errors.WriteLine($"rezrov: {Path.GetFileName(script.ScriptPath)}: acceptance scripts play Z-machine, Glulx and Aa-machine games, and this game is {story.Format}");
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
        // ends when the script does; with the console behind it, the
        // player carries on from there and is told so.
        // [zm 8.8.6] A screen that says it has pictures measures in
        // pixels, so it is given a size in them: 80 by 25 cells of 8 by
        // 16 is 640 by 400, exactly twice the screen the artwork was
        // drawn for. Without pictures the old shape is kept, so every
        // recording that existed before reads the same.
        var screen = script.Pictures
            ? new TextWriterScreen(
                (TextWriter?)screenWriter ?? output,
                width: 80,
                height: 25,
                showUpperWindow: script.Upper,
                hasPictures: true)
            : new TextWriterScreen((TextWriter?)screenWriter ?? output, showUpperWindow: script.Upper);

        var interpreter = new Interpreter(
            memory,
            screen,
            resume
                ? new AnnouncedInput(new ConsoleInput(header, memory), errors)
                : new TextReaderInput(TextReader.Null, header, memory),
            random,
            files,
            new SilentSound(),
            machine,
            tandy: script.Tandy);

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

        var commands = new MarkedCommands(script, output, random, screen);
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

    // A Glulx game: the display prints to the run's output and reads
    // the script's commands, each echoed after its prompt as a console
    // would show it typed; the files stay in memory; and the run ends
    // when the script does, unless the console takes over.
    private static AcceptanceResult? PlayGlulx(AcceptanceScript script, LoadedStory story, TextWriter errors, TextWriter? display, bool resume)
    {
        var output = new StringWriter();
        using var screenWriter = display is null ? null : new TeeWriter(output, display);
        var sink = (TextWriter?)screenWriter ?? output;
        var random = new GlulxRandom((uint)script.Seed);
        var commands = new GlulxCommands(script, output, sink, random, resume ? Console.In : null, errors);

        // [glk #mouse_events] A script may touch a window or select a
        // link, which a player at a console cannot, so this display
        // says it has a pointer where the console program does not.
        var glk = new GlkLibrary(new TextWriterGlkDisplay(sink, commands, hasPointer: true, hasGraphics: script.Graphics), new MemoryGlkFileSystem()) { Resources = story.Resources };

        GlulxMachine machine;
        try
        {
            machine = new GlulxMachine(new GlulxMemory(story.Bytes), random, glk);
        }
        catch (InvalidDataException e)
        {
            errors.WriteLine($"rezrov: {Path.GetFileName(script.ScriptPath)}: {e.Message}");
            return null;
        }

        var ending = AcceptanceEnding.Quit;
        string? message = null;

        try
        {
            machine.Run();
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
        catch (GlulxException e)
        {
            ending = AcceptanceEnding.Failed;
            message = e.Message;
        }
        finally
        {
            glk.CloseFiles();
            sink.Flush();
        }

        var warnings = glk.Warnings.Select(w => "glk: " + w).ToList();
        return new AcceptanceResult(output.ToString(), ending, message, warnings, commands.Offsets);
    }

    // An Aa-machine game: the same shape as the others, except that
    // the machine asks for input rather than being read from, so the
    // script is fed to it a line at a time rather than handed over as
    // a reader.
    private static AcceptanceResult? PlayAaMachine(AcceptanceScript script, LoadedStory story, TextWriter errors, TextWriter? display, bool resume)
    {
        var name = Path.GetFileName(script.ScriptPath);

        AaStory aa;

        try
        {
            aa = AaStory.Read(story.Bytes);
        }
        catch (InvalidDataException e)
        {
            errors.WriteLine($"rezrov: {name}: {e.Message}");
            return null;
        }

        // The directives that belong to the other two machines mean
        // nothing here, and a script that carries one is told so
        // rather than left to wonder why it did nothing.
        foreach (var option in Inapplicable(script))
        {
            errors.WriteLine($"rezrov: {name}: the {option} directive does not apply to the Aa-machine");
        }

        var output = new StringWriter();
        using var screenWriter = display is null ? null : new TeeWriter(output, display);
        var sink = (TextWriter?)screenWriter ?? output;
        var text = new TextOutput(sink, aa.Styles, resource => AaMachinePlayer.AltText(aa, resource));
        var machine = new Machine(aa, text, script.Seed);

        var offsets = new List<int>();
        var ending = AcceptanceEnding.Quit;
        string? message = null;
        var next = 0;
        var announced = false;

        try
        {
            var status = machine.Start();

            while (status != AaStatus.Quit)
            {
                if (next <= script.Commands.Count && script.SeedChanges.TryGetValue(next, out var seed))
                {
                    machine.Reseed(seed);
                }

                string? line;

                if (next < script.Commands.Count)
                {
                    offsets.Add(output.GetStringBuilder().Length);
                    line = script.Commands[next++];

                    // The newline is the game's kind, not the
                    // platform's, so the play reads the same
                    // everywhere.
                    sink.Write(line);
                    sink.Write('\n');
                }
                else
                {
                    next = script.Commands.Count + 1;

                    if (!resume)
                    {
                        ending = AcceptanceEnding.ScriptEnded;
                        break;
                    }

                    if (!announced)
                    {
                        announced = true;
                        errors.WriteLine();
                        errors.WriteLine("rezrov: the script has ended; the game is yours from here");
                    }

                    if (Console.In.ReadLine() is not { } typed)
                    {
                        ending = AcceptanceEnding.ScriptEnded;
                        break;
                    }

                    line = typed;
                }

                status = Answer(machine, text, status, line);
            }
        }
        catch (AaMachineException e)
        {
            ending = AcceptanceEnding.Failed;
            message = e.Message;
        }
        finally
        {
            text.Sync();
            sink.Flush();
        }

        return new AcceptanceResult(output.ToString(), ending, message, machine.RuntimeErrors, offsets);
    }

    // One line of the script, given to whatever the machine is waiting
    // for. A game waiting on a single key is given the line one key at
    // a time, and the return at the end of it counts as one.
    private static AaStatus Answer(Machine machine, TextOutput text, AaStatus status, string line)
    {
        if (status == AaStatus.GetInput)
        {
            text.Typed();
            return machine.ProceedWithInput(line);
        }

        if (Key(line) is { } single)
        {
            return machine.ProceedWithKey(single);
        }

        foreach (var key in line)
        {
            if (status != AaStatus.GetKey)
            {
                break;
            }

            status = machine.ProceedWithKey(key);
        }

        return status == AaStatus.GetKey ? machine.ProceedWithKey(Return) : status;
    }

    /// <summary>
    /// [aam text] A whole line that is one bracketed code is a single
    /// keypress. The codes are the Z-Machine's, since that is what a
    /// script writes and what a command file records, so the four
    /// cursor keys are translated to the ones the Aa-machine knows.
    /// </summary>
    private static int? Key(string line) =>
        line.Length > 2 && line[0] == '[' && line[^1] == ']' && int.TryParse(line[1..^1], out var code)
            ? code switch
            {
                129 => 0x10,
                130 => 0x11,
                131 => 0x12,
                132 => 0x13,
                10 or 13 => Return,
                _ => code,
            }
            : null;

    private static IEnumerable<string> Inapplicable(AcceptanceScript script)
    {
        if (script.Interpreter is not null)
        {
            yield return "INTERPRETER";
        }

        if (script.Tandy)
        {
            yield return "TANDY";
        }

        if (script.Upper)
        {
            yield return "UPPER";
        }

        if (script.Pictures)
        {
            yield return "PICTURES";
        }

        if (script.BlorbPath is not null)
        {
            yield return "BLORB";
        }
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
            if (string.Equals(expectedLines[i], actualLines[i], StringComparison.Ordinal))
            {
                continue;
            }

            // A recording made from a shorter script ends with a bare
            // prompt, and the longer play has a command after that
            // prompt on the same line. That is the recording running
            // out, not the play going wrong, and it is said so.
            if (i == expectedLines.Length - 1 && actualLines[i].StartsWith(expectedLines[i], StringComparison.Ordinal))
            {
                return new AcceptanceDifference(i + 1, $"the recording ends there, and the play goes on for {actualLines.Length - expectedLines.Length} more lines\n  recorded: {expectedLines[i]}\n  played:   {actualLines[i]}", RecordingEnded: true);
            }

            return new AcceptanceDifference(i + 1, $"  expected: {expectedLines[i]}\n  actual:   {actualLines[i]}");
        }

        if (expectedLines.Length > actualLines.Length)
        {
            return new AcceptanceDifference(common + 1, $"the play ends after line {actualLines.Length}, but {expectedLines.Length - actualLines.Length} more were expected, the first being\n  expected: {expectedLines[common]}");
        }

        if (actualLines.Length > expectedLines.Length)
        {
            return new AcceptanceDifference(common + 1, $"the recording ends there, and the play goes on for {actualLines.Length - expectedLines.Length} more lines, starting with\n  played:   {actualLines[common]}", RecordingEnded: true);
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
        var where = difference.RecordingEnded
            ? $"line {difference.Line} of {expectedName} is its last"
            : $"line {difference.Line} of {expectedName} differs";

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
    private sealed class MarkedCommands(
        AcceptanceScript script,
        StringWriter output,
        RandomGenerator random,
        TextWriterScreen screen) : TextReader
    {
        private int _next;

        public List<int> Offsets { get; } = [];

        public override string? ReadLine()
        {
            // [zm 8.8.6] The game has paused for a command, so this is
            // the moment the player would be looking at the screen, and
            // the moment to record which pictures are on it.
            screen.ShowPictures();

            // Reseeding as the next command is read means the game's
            // rolls for that command onward come from the new stream,
            // and nothing before it is touched. It is the session's kind
            // of seed, not [zm 2.4.2] the predictable state.
            if (_next <= script.Commands.Count && script.SeedChanges.TryGetValue(_next, out var seed))
            {
                random.Reseed(seed);
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
    /// The script's commands as the lines a Glk display reads, echoed
    /// to the play as a console shows what is typed, with the offsets
    /// and seed changes kept as for the Z-Machine, and the console
    /// behind them when the game is to go on from where the script
    /// ends.
    /// </summary>
    private sealed class GlulxCommands(AcceptanceScript script, StringWriter output, TextWriter echo, GlulxRandom random, TextReader? console, TextWriter errors) : TextReader
    {
        private int _next;
        private bool _announced;

        public List<int> Offsets { get; } = [];

        public override string? ReadLine()
        {
            if (_next <= script.Commands.Count && script.SeedChanges.TryGetValue(_next, out var seed))
            {
                random.Seed((uint)seed);
            }

            if (_next >= script.Commands.Count)
            {
                _next = script.Commands.Count + 1;
                if (console is null)
                {
                    return null;
                }

                if (!_announced)
                {
                    _announced = true;
                    errors.WriteLine();
                    errors.WriteLine("rezrov: the script has ended; the game is yours from here");
                }

                return console.ReadLine();
            }

            // The newline is the game's kind, not the platform's, so
            // the play reads the same on every system.
            Offsets.Add(output.GetStringBuilder().Length);
            var command = script.Commands[_next++];
            echo.Write(command);
            echo.Write('\n');
            return command;
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

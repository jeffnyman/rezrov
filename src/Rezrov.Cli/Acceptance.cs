using Rezrov.Core.Acceptance;
using Rezrov.ZMachine;

namespace Rezrov.Cli;

/// <summary>
/// What the <c>--accept</c> command does with a script once it has
/// played it.
/// </summary>
internal enum AcceptanceMode
{
    /// <summary>Compare with the expected file, recording it if there is none.</summary>
    Check,

    /// <summary>Record the expected file afresh.</summary>
    Update,

    /// <summary>Hand the game to the player where the script ends.</summary>
    Resume,
}

/// <summary>
/// The <c>--accept</c> command: plays a script and checks the play
/// against the expected file beside it.
/// </summary>
/// <remarks>
/// The play is shown as it happens, so that someone building a script
/// up sees where a command went wrong, and the verdict comes at the
/// end. The first run of a script has nothing to check against, so it
/// records what happened as the expected file and says so; every run
/// after compares, and reports the first line that differs. When the
/// difference is a deliberate change, <c>--update</c> records afresh.
/// While a script is being written, <c>--resume</c> plays it and then
/// leaves the game running at the console, so the next commands can be
/// tried before they go into the file; nothing is checked or recorded.
/// </remarks>
internal static class Acceptance
{
    /// <summary>
    /// Runs the script and returns the program's exit code: 0 when the
    /// play matched or was recorded, or the resumed game ended, and 1
    /// when the play differed or the script could not be run at all.
    /// </summary>
    public static int Run(string scriptPath, AcceptanceMode mode)
    {
        AcceptanceScript script;
        try
        {
            script = AcceptanceScript.Load(scriptPath);
        }
        catch (FileNotFoundException)
        {
            Console.Error.WriteLine($"rezrov: no such file: {scriptPath}");
            return 1;
        }
        catch (DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"rezrov: no such file: {scriptPath}");
            return 1;
        }
        catch (InvalidDataException e)
        {
            Console.Error.WriteLine($"rezrov: {e.Message}");
            return 1;
        }

        var memory = new ZMemory(File.ReadAllBytes(script.GamePath));
        var keyboard = mode == AcceptanceMode.Resume ? new ConsoleInput(new StoryHeader(memory), memory) : null;

        if (AcceptanceRun.Play(script, Console.Error, Console.Out, keyboard) is not { } result)
        {
            return 1;
        }

        // The rest of the record: what the interpreter noticed, which
        // the play itself does not show.
        Console.Out.Write(result.Text[result.Output.Length..]);
        if (result.Text.Length > 0 && result.Text[^1] != (char)0x0A)
        {
            Console.Out.WriteLine();
        }

        if (mode == AcceptanceMode.Resume)
        {
            return 0;
        }

        // A gap before the verdict.
        Console.Out.WriteLine();

        var name = Path.GetFileName(script.ScriptPath);
        var expectedName = Path.GetFileName(script.ExpectedPath);

        if (mode == AcceptanceMode.Update || !File.Exists(script.ExpectedPath))
        {
            File.WriteAllText(script.ExpectedPath, result.Text);
            Console.WriteLine(mode == AcceptanceMode.Update
                ? $"rezrov: {name}: updated {expectedName}"
                : $"rezrov: {name}: recorded {expectedName}, which later runs will be checked against");
            return 0;
        }

        var expected = File.ReadAllText(script.ExpectedPath);

        if (AcceptanceRun.FirstDifference(expected, result.Text) is { } difference)
        {
            Console.Error.WriteLine($"rezrov: {name}: the play differs from what was recorded");
            Console.Error.WriteLine(AcceptanceRun.Describe(difference, result, script));
            Console.Error.WriteLine("If the change is meant, run again with --update.");
            return 1;
        }

        Console.WriteLine($"rezrov: {name}: passed");
        return 0;
    }
}

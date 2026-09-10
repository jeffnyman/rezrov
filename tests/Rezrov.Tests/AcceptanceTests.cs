using Rezrov.Cli;
using Rezrov.Core.Acceptance;

namespace Rezrov.Tests;

/// <summary>
/// Acceptance scripts: how the file is read, how a run is compared, and
/// that every script in the repository still plays as it was recorded.
/// </summary>
public class AcceptanceTests
{
    private static readonly string ScriptPath = Path.Combine(Path.GetTempPath(), "rezrov", "scripts", "test.accept");

    [Fact]
    public void AScriptHasDirectivesCommentsAndCommands()
    {
        var script = AcceptanceScript.Parse(
            "# Zork, briefly.\n! SEED=20\n!GAME = ../games/zork1.z3\n\nn. n. u\n> get egg\nlook[13]\n>#help\n",
            ScriptPath);

        Assert.Equal(20, script.Seed);
        Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ScriptPath)!, "..", "games", "zork1.z3")), script.GamePath);
        Assert.Null(script.BlorbPath);
        Assert.Equal(["n. n. u", "get egg", "look[13]", "#help"], script.Commands);
        Assert.Equal([5, 6, 7, 8], script.CommandLines);
        Assert.Equal(Path.ChangeExtension(ScriptPath, ".expected"), script.ExpectedPath);
    }

    [Fact]
    public void ASeedAfterCommandsIsAChangeOfSeed()
    {
        var script = AcceptanceScript.Parse(
            "! SEED=1000\n! GAME=zork1.z3\nd. w\nkill troll\n! SEED=1010\ns\n! SEED=1020\n! SEED=1021\nget all\n! SEED=1030\n",
            ScriptPath);

        Assert.Equal(1000, script.Seed);
        Assert.Equal(["d. w", "kill troll", "s", "get all"], script.Commands);
        Assert.Equal(new Dictionary<int, int> { [2] = 1010, [3] = 1021, [4] = 1030 }, script.SeedChanges);
    }

    [Fact]
    public void TheFirstSeedMustComeBeforeTheFirstCommand()
    {
        var e = Assert.Throws<InvalidDataException>(() => AcceptanceScript.Parse("! GAME=zork1.z3\nlook\n! SEED=1000\n", ScriptPath));

        Assert.Equal("test.accept, line 3: the first SEED directive must come before the first command", e.Message);
    }

    [Fact]
    public void AFencedSectionIsNotRead()
    {
        var script = AcceptanceScript.Parse(
            "! SEED=1\n! GAME=zork1.z3\ngo north\n``` PATH 1\nkill troll\n! SEED=99\n```\nsave troll\n```\ntake axe\n",
            ScriptPath);

        Assert.Equal(1, script.Seed);
        Assert.Equal(["go north", "save troll"], script.Commands);
        Assert.Equal([3, 8], script.CommandLines);
    }

    [Fact]
    public void ALabeledFenceInsideAFencedSectionIsAMistake()
    {
        var e = Assert.Throws<InvalidDataException>(() => AcceptanceScript.Parse(
            "! SEED=1\n! GAME=zork1.z3\n``` PATH 1\nkill troll\n``` PATH 2\ntake axe\n",
            ScriptPath));

        Assert.Equal("test.accept, line 5: a fenced section is still open from line 3; close it with a bare ``` first", e.Message);
    }

    [Fact]
    public void ABlorbDirectiveIsRelativeToTheScriptToo()
    {
        var script = AcceptanceScript.Parse("! SEED=1\n! GAME=lurking.z3\n! BLORB=sounds/lurking.blb\n", ScriptPath);

        Assert.Equal(Path.Combine(Path.GetDirectoryName(ScriptPath)!, "sounds", "lurking.blb"), script.BlorbPath);
    }

    [Theory]
    [InlineData("! SEED=20\n", "no GAME directive")]
    [InlineData("! GAME=zork1.z3\n", "no SEED directive")]
    [InlineData("! GAME=zork1.z3\n! SEED=0\n", "line 2: SEED must be")]
    [InlineData("! GAME=zork1.z3\n! SEED=soon\n", "line 2: SEED must be")]
    [InlineData("! GAME=zork1.z3\n! SEED=1\n! SAVE=here\n", "line 3: there is no SAVE directive")]
    [InlineData("! GAME=zork1.z3\n! SEED=1\n! hello\n", "line 3: a directive is written")]
    public void AScriptThatCannotBeUsedSaysWhy(string text, string reason)
    {
        var e = Assert.Throws<InvalidDataException>(() => AcceptanceScript.Parse(text, ScriptPath));

        Assert.StartsWith("test.accept", e.Message, StringComparison.Ordinal);
        Assert.Contains(reason, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheResultTextAddsHowAnAbnormalRunEnded()
    {
        var quit = new AcceptanceResult("Bye.\n", AcceptanceEnding.Quit, null, [], []);
        var ended = new AcceptanceResult(">", AcceptanceEnding.ScriptEnded, null, [], []);
        var stopped = new AcceptanceResult(">", AcceptanceEnding.NotSupported, "no Version 6 yet", [], []);
        var errors = new AcceptanceResult("Bye.\n", AcceptanceEnding.Quit, null, ["At 1234, get_prop: object 0"], []);

        Assert.Equal("Bye.\n", quit.Text);
        Assert.Equal(">", ended.Text);
        Assert.Equal(">\n[rezrov: stopped: no Version 6 yet]\n", stopped.Text);
        Assert.Equal("Bye.\n[rezrov: At 1234, get_prop: object 0]\n", errors.Text);
    }

    [Fact]
    public void TheFirstDifferenceIsFoundByLine()
    {
        Assert.Null(AcceptanceRun.FirstDifference("a\nb\n", "a\r\nb\r\n"));
        Assert.Equal(2, AcceptanceRun.FirstDifference("a\nb\n", "a\nc\n")!.Line);
        Assert.Contains("1 more were expected", AcceptanceRun.FirstDifference("a\nb\nc\n", "a\nb\n")!.Description, StringComparison.Ordinal);
        Assert.Contains("goes on for 1 lines past the end", AcceptanceRun.FirstDifference("a\nb\n", "a\nb\nc\n")!.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ADifferenceIsPlacedInBothFiles()
    {
        // The play: a banner, then two commands and their replies. The
        // offsets are where the output stood when each command was
        // read, which is just after its prompt.
        var play = "West of House\n>look\nYou see a house.\n>north\nNorth of House\n";
        var script = AcceptanceScript.Parse("! SEED=1\n! GAME=zork1.z3\n\nlook\n\nnorth\n", ScriptPath);
        var result = new AcceptanceResult(play, AcceptanceEnding.ScriptEnded, null, [], [play.IndexOf(">look", StringComparison.Ordinal) + 1, play.IndexOf(">north", StringComparison.Ordinal) + 1]);

        var banner = AcceptanceRun.Describe(new AcceptanceDifference(1, "x"), result, script);
        var reply = AcceptanceRun.Describe(new AcceptanceDifference(3, "x"), result, script);
        var echo = AcceptanceRun.Describe(new AcceptanceDifference(4, "x"), result, script);

        Assert.StartsWith("line 1 of test.expected differs, before the first command", banner, StringComparison.Ordinal);
        Assert.StartsWith("line 3 of test.expected differs, while playing line 4 of the script, \"look\"", reply, StringComparison.Ordinal);
        Assert.StartsWith("line 4 of test.expected differs, while playing line 6 of the script, \"north\"", echo, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every script under acceptance/ plays the way its expected file
    /// says. The games live in the entharion submodule, which CI does
    /// not fetch, so a script whose game is absent is skipped, and the
    /// test skips as a whole when none can run.
    /// </summary>
    [Fact]
    public void EveryAcceptanceScriptPlaysAsRecorded()
    {
        var root = Corpus.FindRepositoryRoot();
        var directory = root is null ? null : Path.Combine(root, "acceptance");
        var scripts = directory is not null && Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.accept").Order(StringComparer.Ordinal).ToList()
            : [];

        var failures = new List<string>();
        var played = 0;

        foreach (var path in scripts)
        {
            var name = Path.GetFileName(path);
            AcceptanceScript script;
            try
            {
                script = AcceptanceScript.Load(path);
            }
            catch (InvalidDataException e)
            {
                failures.Add(e.Message);
                continue;
            }

            if (!File.Exists(script.GamePath))
            {
                continue;
            }

            played++;

            if (!File.Exists(script.ExpectedPath))
            {
                failures.Add($"{name}: nothing recorded yet; run rezrov --accept on it first");
                continue;
            }

            var errors = new StringWriter();
            if (AcceptanceRun.Play(script, errors) is not { } result)
            {
                failures.Add($"{name}: {errors.ToString().Trim()}");
                continue;
            }

            if (AcceptanceRun.FirstDifference(File.ReadAllText(script.ExpectedPath), result.Text) is { } difference)
            {
                failures.Add($"{name}: {AcceptanceRun.Describe(difference, result, script)}");
            }
        }

        Assert.SkipUnless(played > 0, "No acceptance script has its game available.");
        Assert.Empty(failures);
    }
}

using System.Text;
using Rezrov.AaMachine;
using Rezrov.AaMachine.Execution;

namespace Rezrov.Tests;

/// <summary>
/// [aam opcode] Running an Aa-machine story, against the transcripts
/// the reference interpreter recorded.
/// </summary>
/// <remarks>
/// The Aa-machine comes with its own conformance suite: a story, the
/// input to feed it, and the transcript that should come out. Those
/// transcripts were recorded through the reference interpreter's plain
/// text frontend, so matching them means matching the machine and the
/// wrapping of the text both.
/// </remarks>
public class AaMachineTests
{
    // The seed the suite's own Makefile runs every story with. The
    // generator is the reference one, so a story that asks for random
    // numbers asks for the same ones.
    private const int Seed = 1234;

    // [aam opcode] What a bare return means to a story waiting on a
    // single keypress.
    private const int Return = 0x0d;

    [Theory]
    [InlineData("aa-exercise")]
    [InlineData("codepoints")]
    [InlineData("gosling")]
    [InlineData("impossible")]
    [InlineData("body_not_status")]
    public void AStoryPlaysAsTheReferenceInterpreterRecordedIt(string name)
    {
        var directory = Suite(name);
        Assert.SkipUnless(directory is not null, "The entharion submodule is not populated.");

        // Most suites keep a transcript for each engine; the ones
        // where the two must agree keep only the one.
        var gold = Path.Combine(directory!, $"{name}.js.gold");

        if (!File.Exists(gold))
        {
            gold = Path.Combine(directory!, $"{name}.gold");
        }

        var expected = File.ReadAllText(gold);
        var typed = File.ReadAllText(Path.Combine(directory!, $"{name}.in")).ReplaceLineEndings("\n");

        // Whether the last line of the input ends in a newline is
        // not a detail. A terminal echoes the line ending it was
        // given, so an input file without one at the end leaves the
        // transcript a line shorter than it would otherwise be.
        var ended = typed.EndsWith('\n');
        var lines = typed.Split('\n');

        Assert.Equal(
            expected.ReplaceLineEndings("\n"),
            Play(directory!, name, ended ? lines[..^1] : lines, ended).ReplaceLineEndings("\n"));
    }

    private static string? Suite(string name)
    {
        var root = Corpus.FindRepositoryRoot();
        var directory = root is null
            ? null
            : Path.Combine(root, "entharion", "vendor", "aamachine", "test", name);

        return directory is not null && File.Exists(Path.Combine(directory, $"{name}.aastory"))
            ? directory
            : null;
    }

    private static string Play(string directory, string name, string[] input, bool ended)
    {
        var story = AaStory.Read(File.ReadAllBytes(Path.Combine(directory, $"{name}.aastory")));
        var written = new StringWriter { NewLine = "\n" };
        var output = new TextOutput(written, story.Styles, resource => Alt(story, resource));
        var machine = new Machine(story, output, Seed);

        var status = machine.Start();
        var line = 0;

        while (status != AaStatus.Quit && line < input.Length)
        {
            var typed = input[line++];

            // The reference frontend reads through a terminal, which
            // echoes what is typed. A recorded transcript therefore
            // has the player's own words in it, right after the
            // prompt, and a play that does not echo cannot match one.
            written.Write(typed);

            if (ended || line < input.Length)
            {
                written.Write('\n');
            }

            if (status == AaStatus.GetInput)
            {
                // The player's own typing has already moved the
                // terminal on to a fresh line.
                output.Typed();
                status = machine.ProceedWithInput(typed);
                continue;
            }

            // A line typed at a story waiting for one key is fed to it
            // one key at a time, and the return at the end of it counts
            // as a key of its own.
            foreach (var key in typed)
            {
                if (status != AaStatus.GetKey)
                {
                    break;
                }

                status = machine.ProceedWithKey(key);
            }

            if (status == AaStatus.GetKey)
            {
                status = machine.ProceedWithKey(Return);
            }
        }

        output.Finish();

        return written.ToString();
    }

    private static string Alt(AaStory story, int resource) =>
        resource >= 0 && resource < story.Resources.Count
            ? story.Text.At(story.Resources[resource].AltText)
            : string.Empty;
}

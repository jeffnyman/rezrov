using Rezrov.AaMachine;
using Rezrov.AaMachine.Execution;
using Rezrov.Tui;
using Rezrov.ZMachine.Screen;

namespace Rezrov.Tests;

/// <summary>
/// [aam output] An Aa-machine story shown on a terminal: the status
/// area across the top, the style sheet honored as far as a grid of
/// characters can, and the text laid out to the width.
/// </summary>
/// <remarks>
/// The display is driven by a real machine playing a real story, but
/// without a terminal under it, so what the player would see is read
/// straight off the grid of cells.
/// </remarks>
public class TerminalAaDisplayTests
{
    private const int Width = 80;
    private const int Height = 24;

    [Fact]
    public void AGameGetsItsStatusAreaAcrossTheTopOfTheScreen()
    {
        var path = Corpus.AaStoryFile("picture-test.aastory");
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        var story = AaStory.Read(File.ReadAllBytes(path!));
        var display = Play(story, ["look"]);

        display.Repaint();

        // [aam output] The story's status class asks for one line, and
        // its score class is set against the right of it. The room
        // name goes at the left, so the two share the row.
        Assert.Equal(1, display.Screen.StatusRows);

        var status = display.Screen.Row(0);

        Assert.Contains("Foyer of the Opera House", status, StringComparison.Ordinal);
        Assert.Contains("Score:", status, StringComparison.Ordinal);

        // The score is at the right rather than after the room name.
        Assert.True(
            status.IndexOf("Score:", StringComparison.Ordinal) > Width / 2,
            $"the score is at column {status.IndexOf("Score:", StringComparison.Ordinal)}");

        // And it reads as a bar rather than as ordinary text.
        Assert.True(display.Screen[0, 0].Attributes.Style.HasFlag(TextStyle.ReverseVideo));
    }

    [Fact]
    public void TheGamesTextIsBelowTheStatusAreaAndWrappedToTheWidth()
    {
        var path = Corpus.AaStoryFile("picture-test.aastory");
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        var story = AaStory.Read(File.ReadAllBytes(path!));
        var display = Play(story, ["look"]);

        display.Repaint();

        var screen = Enumerable.Range(0, Height).Select(display.Screen.Row).ToList();

        // Nothing is wider than the terminal, which is the whole point
        // of laying the text out rather than printing it.
        Assert.All(screen, row => Assert.True(row.Length <= Width, $"a row is {row.Length} wide"));

        // The resource the game embeds cannot be drawn, so the words
        // the story carries in its place are shown instead.
        Assert.Contains(screen, row => row.Contains("a test image of four coloured bands", StringComparison.Ordinal));

        // The room description is in the main area, below the status.
        var room = screen.FindIndex(row => row.Contains("splendidly decorated", StringComparison.Ordinal));

        Assert.True(room > 0, "the room description is not on the screen");
    }

    [Fact]
    public void AClassThatAsksForCapitalsAndCenteringGetsThem()
    {
        var path = Corpus.AaStoryFile("tethered.aastory");
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        var story = AaStory.Read(File.ReadAllBytes(path!));

        // The prologue ends with the act title, which the story sets
        // in a class asking for capitals, centering and weight.
        var display = Play(story, [
            " ",
            "x harness",
            "x tool belt",
            "take knife",
            "cut rope with knife",
            " ",
            " ",
        ]);

        display.Repaint();

        var screen = Enumerable.Range(0, Height).Select(display.Screen.Row).ToList();
        var title = screen.FindIndex(row => row.Contains("TETHERED", StringComparison.Ordinal));

        Assert.True(title >= 0, "the act title is not on the screen in capitals");

        // Centered means there is as much blank in front of it as the
        // width leaves over, near enough.
        var line = screen[title];
        var before = line.Length - line.TrimStart().Length;

        Assert.True(before > 10, $"the title starts at column {before}, which is not centered");
    }

    // Plays a story on a display with no terminal under it, feeding it
    // the given lines and stopping when it runs out of them.
    private static TerminalAaDisplay Play(AaStory story, string[] input)
    {
        var display = new TerminalAaDisplay(story, Width, Height, () => { }, new StringReader(string.Join('\n', input)));
        var machine = new Machine(story, display, seed: 1);
        var status = machine.Start();
        var fed = 0;

        while (status != AaStatus.Quit && fed++ < input.Length)
        {
            status = status == AaStatus.GetInput
                ? machine.ProceedWithInput(display.ReadLine())
                : machine.ProceedWithKey(display.ReadKey());
        }

        return display;
    }
}

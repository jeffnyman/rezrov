using Rezrov.ZMachine;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

/// <summary>
/// What becomes of the lower window's text when the upper window is
/// grown over it, and when it is shrunk off it again.
/// </summary>
/// <remarks>
/// [zm 8.7.2.1] The standard describes what interpreters usually do
/// and requires nothing: "It is usual for interpreters to print the
/// upper window on the top n lines of the screen, overlaying any text
/// which is already there". Overlaying and keeping are both
/// conformant, so this is a choice rather than a rule, and the choice
/// here is to keep the text, which is what the Glk interpreters do
/// because their two windows are separate panes.
/// </remarks>
public class UpperWindowGrowthTests
{
    [Fact]
    public void TextUnderAGrowingUpperWindowIsKeptRatherThanCoveredOver()
    {
        // The sequence from the intfiction thread on enlarging an
        // upper window that already has text under it.
        var (screen, model) = Grid(40, 10, ZMachineVersion.V5);

        Say(model, "Main window one.");

        Upper(model, 1, "First line.");
        Upper(model, 2, "Second line.");
        Upper(model, 3, "Third line.");

        model.SetWindow(ScreenModel.Lower);
        Say(model, "Main window two.");

        Assert.Equal("First line.", Row(screen, 0));
        Assert.Equal("Second line.", Row(screen, 1));
        Assert.Equal("Third line.", Row(screen, 2));

        // The line the upper window grew over is still readable, one
        // row further down, and the game's next line follows it.
        Assert.Equal("Main window one.", Row(screen, 3));
        Assert.Equal("Main window two.", Row(screen, 4));
    }

    [Fact]
    public void AnUpperWindowThatShrinksLeavesNothingOfItselfBehind()
    {
        // The rows it gives back belong to the lower window again, so
        // what the upper window drew in them may not stay there.
        var (screen, model) = Grid(40, 10, ZMachineVersion.V5);

        Say(model, "Before.");

        Upper(model, 1, "Quote one.");
        Upper(model, 2, "Quote two.");
        Upper(model, 3, "Quote three.");

        model.SetWindow(ScreenModel.Lower);
        model.SplitWindow(0);
        model.Flush();

        Assert.Equal("Before.", Row(screen, 0));
        Assert.Equal(string.Empty, Row(screen, 1));
        Assert.Equal(string.Empty, Row(screen, 2));
    }

    [Fact]
    public void AnUpperWindowThatDoesNotChangeSizeMovesNothing()
    {
        // A game that splits to the same height every turn, which is
        // most of them, must not shuffle the screen for it.
        var (screen, model) = Grid(40, 10, ZMachineVersion.V5);

        Upper(model, 1, "Status.");

        model.SetWindow(ScreenModel.Lower);
        Say(model, "One.");
        Say(model, "Two.");

        model.SplitWindow(1);
        model.Flush();

        Assert.Equal("Status.", Row(screen, 0));
        Assert.Equal("One.", Row(screen, 1));
        Assert.Equal("Two.", Row(screen, 2));
    }

    [Fact]
    public void AFullLowerWindowLosesItsOldestLineRatherThanItsNewest()
    {
        // There is nowhere to push to once the window is full, so the
        // top goes and the newest line stays above the prompt, which
        // is what a shrinking pane does everywhere else.
        var (screen, model) = Grid(40, 6, ZMachineVersion.V5);

        for (var line = 0; line < 6; line++)
        {
            Say(model, $"line {line}");
        }

        Upper(model, 2, "Status.");
        model.Flush();

        // The upper window's cursor was put on its second row, so
        // that is where its text is.
        Assert.Equal("Status.", Row(screen, 1));

        // Four rows are left for seven lines of text, counting the
        // empty one the cursor sits on, so the three oldest go.
        Assert.Equal("line 3", Row(screen, 2));
        Assert.Equal("line 5", Row(screen, 4));
        Assert.Equal(string.Empty, Row(screen, 5));
    }

    private static (BufferedScreen Screen, ScreenModel Model) Grid(int width, int height, ZMachineVersion version)
    {
        var screen = new BufferedScreen(
            width,
            height,
            cursorStartsAtBottom: version <= ZMachineVersion.V4,
            repaint: () => { },
            waitForKey: () => Zscii.Newline,
            fontWidth: 1,
            fontHeight: 1,
            capabilities: ScreenCapabilities.UpperWindow | ScreenCapabilities.FixedGrid
                | ScreenCapabilities.Colors);

        var (memory, header) = DebugStory.Of([0x00], version);

        return (screen, new ScreenModel(screen, header, memory));
    }

    private static void Say(ScreenModel model, string text)
    {
        foreach (var character in text)
        {
            model.Print(character);
        }

        model.NewLine();
        model.Flush();
    }

    private static void Upper(ScreenModel model, int lines, string text)
    {
        model.SplitWindow(lines);
        model.SetWindow(ScreenModel.Upper);
        model.SetCursor(lines, 1);

        foreach (var character in text)
        {
            model.Print(character);
        }

        model.Flush();
    }

    private static string Row(BufferedScreen screen, int row) => screen.Buffer.RowText(row).TrimEnd();
}

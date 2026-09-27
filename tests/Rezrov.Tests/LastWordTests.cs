using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

/// <summary>
/// What a frontend says when a game ends, and the wait that keeps a
/// window up long enough for it to be read.
/// </summary>
/// <remarks>
/// A game may end without asking for anything first, and a window
/// that closed the moment it did would take the ending with it. Worse
/// where a game stopped rather than ended: a window program on
/// Windows has no console, so the reason written to standard error is
/// written to nobody.
/// </remarks>
public class LastWordTests
{
    [Fact]
    public void AGameThatEndedSaysSoAndWaitsForAKey()
    {
        var (screen, input) = Grid(40, 6);

        screen.Print("The end.", Plain);
        input.Enqueue(Zscii.Newline);

        LastWord.Show(screen, input, null);

        // On its own line, under whatever the game said last.
        Assert.Equal("The end.", screen.Buffer.RowText(0).TrimEnd());
        Assert.Equal("[The game has ended. Press a key to leave.]"[..40], screen.Buffer.RowText(1));
    }

    [Fact]
    public void AGameThatStoppedSaysWhyBeforeItSaysThat()
    {
        var (screen, input) = Grid(60, 6);

        input.Enqueue(Zscii.Newline);

        LastWord.Show(screen, input, "Stopped: sound_effect is not built yet.");

        // The reason has nowhere else to go on a window program, so
        // it goes where the game's own words went.
        Assert.Equal("Stopped: sound_effect is not built yet.", screen.Buffer.RowText(1).TrimEnd());
        Assert.StartsWith("[The game has ended.", screen.Buffer.RowText(2), StringComparison.Ordinal);
    }

    [Fact]
    public void TheNoticeIsReversedSoItReadsAsTheProgramSpeaking()
    {
        var (screen, input) = Grid(60, 6);

        input.Enqueue(Zscii.Newline);

        LastWord.Show(screen, input, null);

        Assert.True(screen.Buffer[1, 0].Attributes.Style.HasFlag(TextStyle.ReverseVideo));
    }

    [Fact]
    public void TheWaitIsForOneKeyAndNoMore()
    {
        var (screen, input) = Grid(40, 6);

        input.Enqueue(Zscii.Newline);
        input.Enqueue((ushort)'x');

        LastWord.Show(screen, input, null);

        // The second key is still there, which is what says the wait
        // took exactly one and did not swallow the queue.
        Assert.Equal((ushort)'x', input.WaitForAnyKey());
    }

    private static (BufferedScreen Screen, BufferedInput Input) Grid(int width, int height)
    {
        var screen = new BufferedScreen(
            width,
            height,
            cursorStartsAtBottom: false,
            repaint: () => { },
            waitForKey: () => Zscii.Newline,
            fontWidth: 1,
            fontHeight: 1,
            capabilities: ScreenCapabilities.FixedGrid | ScreenCapabilities.Colors);

        return (screen, new BufferedInput(screen));
    }

    private static TextAttributes Plain =>
        new(TextStyle.Roman, ScreenColor.Default, ScreenColor.Default, TextAttributes.NormalFont);
}

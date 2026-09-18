using Rezrov.Glulx.Glk;
using Rezrov.Gui;

namespace Rezrov.Tests;

/// <summary>
/// [glk #line_events] What the graphical frontend makes of the player's
/// input, and in particular of text pasted in rather than typed.
/// </summary>
public class GuiInputTests
{
    [Fact]
    public void PastingSeveralLinesRunsEveryOneOfThem()
    {
        // Pasting a walkthrough is how a game gets played through
        // without anyone typing five hundred commands, so everything
        // after the first line ending has to wait its turn rather than
        // be thrown away.
        var display = new GuiGlkDisplay(new Cells(), () => { }, 800, 600);
        var story = new GlkLibrary(display).OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;

        display.Typed("north\ntake torque\nwear torque\n");

        Assert.Equal("north", Read(display, story));
        Assert.Equal("take torque", Read(display, story));
        Assert.Equal("wear torque", Read(display, story));
    }

    [Fact]
    public void ALineEndingWrittenTwoWaysIsStillOneEnding()
    {
        // A file written on Windows ends its lines with a carriage
        // return and a newline. Taking those as two endings would type
        // a blank command between every pair of real ones.
        var display = new GuiGlkDisplay(new Cells(), () => { }, 800, 600);
        var story = new GlkLibrary(display).OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;

        display.Typed("north\r\nsouth\r\n");

        Assert.Equal("north", Read(display, story));
        Assert.Equal("south", Read(display, story));
    }

    [Fact]
    public void AKeyTakenFromAPasteLeavesTheRestOfIt()
    {
        // [glk #char_events] A game that asks for a single key in the
        // middle of a walkthrough takes one character and the rest goes
        // on to the next request, whatever kind that is.
        var display = new GuiGlkDisplay(new Cells(), () => { }, 800, 600);
        var story = new GlkLibrary(display).OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;

        display.Typed("ylook\n");

        var key = display.WaitForInput([], [story], TimeSpan.FromSeconds(2));

        Assert.Equal(GlkInputKind.Key, key.Kind);
        Assert.Equal('y', (char)key.Key);
        Assert.Equal("look", Read(display, story));
    }

    /// <summary>
    /// The next line the display has for the game. A timeout rather
    /// than a wait without end, so that a display which has lost the
    /// rest of a paste fails the test instead of hanging it.
    /// </summary>
    private static string? Read(GuiGlkDisplay display, GlkWindow window) =>
        display.WaitForInput([window], [], TimeSpan.FromSeconds(2)).Text;

    /// <summary>
    /// A font of whole cells, which is all the display needs to work
    /// out how many of them the window holds.
    /// </summary>
    private sealed class Cells : IGlyphs
    {
        public double CellWidth => 10;

        public double CellHeight => 20;

        public double Width(string text, GlkStyle style) => (text?.Length ?? 0) * 10;

        public double LineHeight(GlkStyle style) => 20;
    }
}

using Rezrov.AaMachine;
using Rezrov.AaMachine.Execution;
using Rezrov.Grid;
using Rezrov.Gtui;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

/// <summary>
/// [aam output] An Aa-machine story in the grid frontend: the keys the
/// window reports turned into the ones the machine reads, and the cells
/// the machine fills turned into pixels.
/// </summary>
/// <remarks>
/// Nothing here needs a window. The keys are translated by a function
/// with no state, and the drawing fills an array of pixels that a test
/// can read, which is why the drawing was kept apart from the window in
/// the first place.
/// </remarks>
public class GridAaTests
{
    private const int Width = 80;
    private const int Height = 24;

    [Fact]
    public void TheKeysBothMachinesKnowComeThroughAsThemselves()
    {
        // The two machines agree about ordinary characters, which both
        // write as themselves, and about the keys a player moves a
        // menu with.
        Assert.Equal('x', AaGridKeys.FromZscii('x'));
        Assert.Equal(' ', AaGridKeys.FromZscii(' '));
        Assert.Equal(AaKeys.Return, AaGridKeys.FromZscii(Zscii.Newline));
        Assert.Equal(AaKeys.Backspace, AaGridKeys.FromZscii(Zscii.Delete));
        Assert.Equal(AaKeys.Up, AaGridKeys.FromZscii(Zscii.CursorUp));
        Assert.Equal(AaKeys.Down, AaGridKeys.FromZscii(Zscii.CursorDown));
        Assert.Equal(AaKeys.Left, AaGridKeys.FromZscii(Zscii.CursorLeft));
        Assert.Equal(AaKeys.Right, AaGridKeys.FromZscii(Zscii.CursorRight));

        // [zm 3.8] An accented letter is numbered differently by the
        // two machines, so it goes through the table rather than
        // straight across. The table begins at 155 with a lower case
        // a-diaeresis and reaches its capital three places later.
        Assert.Equal('ä', AaGridKeys.FromZscii(155));
        Assert.Equal('Ä', AaGridKeys.FromZscii(158));
    }

    [Fact]
    public void AKeyOnlyOneMachineNamesIsNotPassedOn()
    {
        // [aam text] The Aa-machine has no escape key and no function
        // keys. A story sees nothing rather than seeing some other
        // character that happens to share the number.
        Assert.Null(AaGridKeys.FromZscii(Zscii.Escape));
        Assert.Null(AaGridKeys.FromZscii(Zscii.F1));
        Assert.Null(AaGridKeys.FromZscii(Zscii.F12));
        Assert.Null(AaGridKeys.FromZscii(0));

        // The keypad is a set of digits wherever it is, so those do
        // come through, as the digits they stand for.
        Assert.Equal('0', AaGridKeys.FromZscii(Zscii.Keypad0));
        Assert.Equal('9', AaGridKeys.FromZscii(Zscii.Keypad9));
    }

    [Fact]
    public void AStoryIsDrawnIntoThePixelsOfTheWindow()
    {
        var path = Corpus.AaStoryFile("picture-test.aastory");
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        var story = AaStory.Read(File.ReadAllBytes(path!));
        var display = Play(story, ["look"]);
        var surface = new Surface(Width * Paint.CellWidth, Height * Paint.CellHeight);

        lock (display.Sync)
        {
            display.Repaint();
            Paint.Picture(surface, display);
        }

        // [aam story] The status area is drawn in reverse, so its row
        // is mostly ink where a row of ordinary text is mostly page.
        // That is the one thing about this picture a count of pixels
        // can tell without reading the letters back.
        var status = Lit(surface, 0);
        var rows = Enumerable.Range(1, Height - 1).Select(row => Lit(surface, row)).ToList();
        var text = rows.Max();

        Assert.True(text > 0, "nothing was drawn in the main text");
        Assert.True(status > text * 2, $"the status row has {status} lit pixels and the fullest text row {text}");

        // And the text sits below the status area rather than over it,
        // which is the whole of the layout a grid has to get right.
        Assert.True(rows.Count(lit => lit > 0) > 3, "the story printed almost nothing");
    }

    [Fact]
    public void TheGridIsTheSizeItWasGivenAndChangesWithTheWindow()
    {
        var path = Corpus.AaStoryFile("tethered.aastory");
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        var story = AaStory.Read(File.ReadAllBytes(path!));
        var display = Play(story, []);

        Assert.Equal(Width, display.Width);
        Assert.Equal(Height, display.Height);

        // A window that changes size changes the grid, and the text
        // lays itself out again to the new width.
        display.Resize(40, 12);

        Assert.Equal(40, display.Width);
        Assert.Equal(12, display.Height);

        var surface = new Surface(40 * Paint.CellWidth, 12 * Paint.CellHeight);

        lock (display.Sync)
        {
            display.Repaint();
            Paint.Picture(surface, display);
        }

        // Nothing was drawn past the edge of the narrower window,
        // which is what laying out again is for.
        Assert.Equal(40 * Paint.CellWidth, surface.Width);
    }

    /// <summary>
    /// How many pixels of one row of cells are not the page color.
    /// </summary>
    private static int Lit(Surface surface, int row)
    {
        var count = 0;

        for (var y = row * Paint.CellHeight; y < (row + 1) * Paint.CellHeight; y++)
        {
            for (var x = 0; x < surface.Width; x++)
            {
                if (surface[x, y] != Surface.Black)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Plays a story on a display with no window under it, feeding it
    /// the lines given and stopping when it runs out of them.
    /// </summary>
    private static AaGridDisplay Play(AaStory story, string[] input)
    {
        var display = new AaGridDisplay(
            story, Width, Height, () => { }, new StringReader(string.Join('\n', input)));

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

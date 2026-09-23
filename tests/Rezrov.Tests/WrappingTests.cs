using Rezrov.ZMachine;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

/// <summary>
/// [zm 7.2] Where the lower window's text breaks, pinned end to end.
/// </summary>
/// <remarks>
/// The other wrapping tests watch what the screen model hands a
/// frontend, which is the right thing to check while the model is the
/// one deciding the breaks. This one deliberately does not: it prints
/// through a real buffered screen and reads the finished grid, so it
/// says only what a player would see and nothing at all about which
/// side of the seam decided it.
///
/// That is the point of it. Wrapping has no acceptance coverage,
/// because the command line frontend is a stream with no fixed grid
/// and never wraps, so nothing outside these tests would notice a
/// break moving. This is the net under any change to where the
/// wrapping happens: the grid before and the grid after must be the
/// same grid.
/// </remarks>
public class WrappingTests
{
    /// <summary>
    /// A passage chosen to reach every rule at once: ordinary prose, a
    /// word longer than the narrow screens, lines the game ends itself,
    /// and a line that comes out exactly the width of the screen.
    /// </summary>
    private const string Passage =
        "You are standing in an open field west of a white house, with a "
        + "boarded front door.\n"
        + "There is a small mailbox here.\n"
        + "A rubber mat saying 'Welcome to Zork!' lies by the door.\n"
        + "The antidisestablishmentarianism of it all is remarkable.\n";

    [Theory]
    [InlineData(80)]
    [InlineData(64)]
    [InlineData(40)]
    [InlineData(20)]
    [InlineData(12)]
    public void TheLowerWindowBreaksWhereItAlwaysHas(int width)
    {
        Assert.Equal(Expected(width), Grid(width));
    }

    [Fact]
    public void AnUnbufferedPassageBreaksWhereTheWidthRunsOut()
    {
        // [zm 8.8.3.1.2.2] With buffering off there is no word to keep
        // whole, so the break falls wherever the line ends.
        var (screen, model) = Make(16, 8);
        model.SetBuffering(false);

        Say(model, "Here is an abacus and a bead");

        Assert.Equal(["Here is an abacu", "s and a bead"], Written(screen));
    }

    /// <summary>The whole of the lower window, as lines of text.</summary>
    private static string Grid(int width)
    {
        var (screen, model) = Make(width, 40);

        Say(model, Passage);

        return string.Join("\n", Written(screen));
    }

    /// <summary>
    /// Prints a passage the way a game does: one character at a time,
    /// with [zm op:new_line] for the end of a line rather than a
    /// newline character in the stream, which the model would take for
    /// part of a word.
    /// </summary>
    private static void Say(ScreenModel model, string passage)
    {
        foreach (var character in passage)
        {
            if (character == '\n')
            {
                model.NewLine();
            }
            else
            {
                model.Print(character);
            }
        }

        model.Flush();
    }

    /// <summary>
    /// The grid's rows, with the blank to the right of each taken off
    /// and the empty rows above and below dropped.
    /// </summary>
    private static string[] Written(BufferedScreen screen) =>
        Enumerable.Range(0, screen.Buffer.Height)
            .Select(row => screen.Buffer.RowText(row).TrimEnd())
            .SkipWhile(line => line.Length == 0)
            .Reverse()
            .SkipWhile(line => line.Length == 0)
            .Reverse()
            .ToArray();

    private static (BufferedScreen Screen, ScreenModel Model) Make(int width, int height)
    {
        var bytes = new byte[2048];
        bytes[0] = (byte)ZMachineVersion.V5;
        bytes[0x04] = 0x04;
        bytes[0x06] = 0x04;
        bytes[0x08] = 0x03;
        bytes[0x09] = 0x80;
        bytes[0x0A] = 0x02;
        bytes[0x0C] = 0x01;
        bytes[0x0E] = 0x04;
        bytes[0x0381] = 6;

        var memory = new ZMemory(bytes);
        var header = new StoryHeader(memory);

        var screen = new BufferedScreen(
            width,
            height,
            cursorStartsAtBottom: false,
            repaint: () => { },
            waitForKey: () => Zscii.Newline,
            fontWidth: 1,
            fontHeight: 1,
            capabilities: ScreenCapabilities.FixedGrid | ScreenCapabilities.FixedPitch);

        return (screen, new ScreenModel(screen, header, memory));
    }

    /// <summary>
    /// What the passage comes out as at each width. Measured from the
    /// code as it stands rather than worked out by hand, because the
    /// job of this test is to notice a change, not to argue that the
    /// current answer is the best one.
    /// </summary>
    private static string Expected(int width) => width switch
    {
        80 => """
            You are standing in an open field west of a white house, with a boarded front
            door.
            There is a small mailbox here.
            A rubber mat saying 'Welcome to Zork!' lies by the door.
            The antidisestablishmentarianism of it all is remarkable.
            """,

        64 => """
            You are standing in an open field west of a white house, with a
            boarded front door.
            There is a small mailbox here.
            A rubber mat saying 'Welcome to Zork!' lies by the door.
            The antidisestablishmentarianism of it all is remarkable.
            """,

        40 => """
            You are standing in an open field west
            of a white house, with a boarded front
            door.
            There is a small mailbox here.
            A rubber mat saying 'Welcome to Zork!'
            lies by the door.
            The antidisestablishmentarianism of it
            all is remarkable.
            """,

        // [zm 7.2] The long word is wider than this screen, so the
        // promise to keep a word whole does not cover it and it breaks
        // where the line runs out, from wherever it happened to start.
        20 => """
            You are standing in
            an open field west
            of a white house,
            with a boarded front
            door.
            There is a small
            mailbox here.
            A rubber mat saying
            'Welcome to Zork!'
            lies by the door.
            The antidisestablish
            mentarianism of it
            all is remarkable.
            """,

        12 => """
            You are
            standing in
            an open
            field west
            of a white
            house, with
            a boarded
            front door.
            There is a
            small
            mailbox
            here.
            A rubber mat
            saying
            'Welcome to
            Zork!' lies
            by the door.
            The antidise
            stablishment
            arianism of
            it all is
            remarkable.
            """,

        _ => throw new ArgumentOutOfRangeException(nameof(width)),
    };
}

using Rezrov.AaMachine;
using Rezrov.AaMachine.Execution;
using Rezrov.Gui;

namespace Rezrov.Tests;

/// <summary>
/// [aam output] Whole games played on the graphical display, with the
/// page laid out at every step and while the machine is still writing
/// it.
/// </summary>
/// <remarks>
/// The other tests of this display print a few lines and look at where
/// they landed. These play the games the acceptance scripts play,
/// beginning to end, and lay the page out after every command, because
/// a layout that throws on the one paragraph in the game that asks for
/// something unusual is a crash in front of the player and nothing at
/// all in a test that never reaches it.
///
/// The second of them is about the thread rather than the text. The
/// machine writes on its own thread while the toolkit lays the page out
/// on another, and a page half written is not a page that can be laid
/// out. Nothing here checks what is painted; what it checks is that
/// laying out and writing at the same time is allowed to happen.
/// </remarks>
public class GuiAaPlayTests
{
    private const double Width = 800;
    private const double Height = 600;

    [Theory]
    [InlineData("tethered-r4-s191125.accept")]
    [InlineData("picture-test-r2-s260920.accept")]
    public void AWholeGameLaysOutAtEveryStepOfItsScript(string script)
    {
        var story = Story(script);
        var commands = Script(script);
        var display = Open(story);
        var machine = new Machine(story, display, seed: 1);
        var status = machine.Start();
        var laid = 0;

        Assert.NotEmpty(Lay(display));

        foreach (var command in commands)
        {
            if (status == AaStatus.Quit)
            {
                break;
            }

            status = Answer(machine, display, status, command);
            laid++;

            // The page is laid out after every command, exactly as the
            // window would lay it out to paint it.
            Lay(display);
        }

        Assert.True(laid > 5, $"{script} ran only {laid} commands");

        // The game reached its ending rather than stopping somewhere
        // in the middle of the script.
        Assert.Equal(AaStatus.Quit, status);
    }

    [Fact]
    public void ThePageCanBeLaidOutWhileTheMachineIsStillWritingIt()
    {
        var story = Story("tethered-r4-s191125.accept");
        var commands = Script("tethered-r4-s191125.accept");
        var display = Open(story);
        var machine = new Machine(story, display, seed: 1);
        var trouble = (Exception?)null;
        var playing = true;

        // A second thread doing what the toolkit does: laying the page
        // out over and over while the game writes it.
        var painter = new Thread(() =>
        {
            try
            {
                while (Volatile.Read(ref playing))
                {
                    Lay(display);
                }
            }
            catch (Exception e)
            {
                trouble = e;
            }
        })
        {
            IsBackground = true,
            Name = "painter",
        };

        painter.Start();

        try
        {
            var status = machine.Start();

            foreach (var command in commands)
            {
                if (status == AaStatus.Quit || trouble is not null)
                {
                    break;
                }

                status = Answer(machine, display, status, command);
            }
        }
        finally
        {
            Volatile.Write(ref playing, false);
            painter.Join(TimeSpan.FromSeconds(30));
        }

        Assert.Null(trouble);
    }

    /// <summary>
    /// Lays the page out the way the window does, holding what the
    /// window holds while it does.
    /// </summary>
    private static IReadOnlyList<AaLine> Lay(GuiAaDisplay display)
    {
        lock (display.Sync)
        {
            var column = display.Column;

            display.Status.Lay(column);
            return display.Main.Lay(column).Lines;
        }
    }

    /// <summary>
    /// Feeds the display one line of a script, as a command or as a
    /// keypress depending on what the machine is waiting for.
    /// </summary>
    private static AaStatus Answer(Machine machine, GuiAaDisplay display, AaStatus status, string command)
    {
        if (status == AaStatus.GetKey)
        {
            display.Enqueue(command.Length > 0 ? command[0] : AaKeys.Return);
            return machine.ProceedWithKey(display.ReadKey());
        }

        foreach (var character in command)
        {
            display.Enqueue(character);
        }

        display.Enqueue(AaKeys.Return);
        return machine.ProceedWithInput(display.ReadLine());
    }

    private static GuiAaDisplay Open(AaStory story)
    {
        var display = new GuiAaDisplay(story, new AaRuler(), AaRuler.Plain, () => { }, () => null);

        display.Resize(Width, Height);
        return display;
    }

    /// <summary>
    /// The commands of an acceptance script, with the keypresses it
    /// writes in angle brackets turned back into the keys they stand
    /// for.
    /// </summary>
    private static List<string> Script(string name)
    {
        var root = Corpus.FindRepositoryRoot();
        var path = root is null ? null : Path.Combine(root, "acceptance", name);

        Assert.SkipUnless(path is not null && File.Exists(path), $"{name} is not in the repository.");

        return [.. File.ReadAllLines(path!)
            .Where(line => line.Length > 0 && line[0] is not ('!' or '#'))
            .Select(line => line == "<space>" ? " " : line)];
    }

    /// <summary>The game a script plays.</summary>
    private static AaStory Story(string script)
    {
        var name = script.Split('-')[0] switch
        {
            "picture" => "picture-test.aastory",
            _ => "tethered.aastory",
        };

        var path = Corpus.AaStoryFile(name);
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        return AaStory.Read(File.ReadAllBytes(path!));
    }
}

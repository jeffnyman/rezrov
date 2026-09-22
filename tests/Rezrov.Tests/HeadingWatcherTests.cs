using Rezrov.Cli;
using Rezrov.Core.Graphics;
using Rezrov.Glulx.Glk;
using Rezrov.Mapping;

namespace Rezrov.Tests;

/// <summary>
/// [mapping] Finding the room a Glulx story is in, which it says only by
/// printing the name as a heading.
/// </summary>
public class HeadingWatcherTests
{
    /// <summary>
    /// A display that does nothing and answers with whatever input it
    /// was handed, so that a turn can be played out without a game.
    /// </summary>
    private sealed class Stub : IGlkDisplay
    {
        public GlkInput Next { get; set; } = GlkInput.Ended;

        public int Width => 80;

        public int Height => 24;

        public void Print(GlkWindow window, uint character, GlkStyle style, uint link)
        {
        }

        public void Clear(GlkWindow window)
        {
        }

        public void Arranged(GlkWindow? root)
        {
        }

        public void Wake()
        {
        }

        public GlkInput WaitForInput(
            IReadOnlyList<GlkWindow> lineRequests,
            IReadOnlyList<GlkWindow> charRequests,
            TimeSpan? timeout) => Next;
    }

    private sealed record Turn(HeadingWatcher Display, Stub Inner, GlkWindow Window);

    private static Turn Story()
    {
        var stub = new Stub();
        var window = new GlkLibrary(stub).OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;
        return new Turn(new HeadingWatcher(stub, new RoomWatcher()), stub, window);
    }

    private static void Say(Turn turn, string text, GlkStyle style)
    {
        foreach (var character in text)
        {
            turn.Display.Print(turn.Window, character, style, 0);
        }
    }

    /// <summary>
    /// Ends the turn at a command prompt, with what the player typed.
    /// </summary>
    private static void Prompt(Turn turn, string typed)
    {
        turn.Inner.Next = GlkInput.Line(turn.Window, typed);
        turn.Display.WaitForInput([turn.Window], [], null);
    }

    [Fact]
    public void AHeadingWithTheDescriptionUnderItIsARoom()
    {
        var turn = Story();
        Say(turn, "West of House\n", GlkStyle.Subheader);
        Say(turn, "You are standing in an open field.\n>", GlkStyle.Normal);
        Prompt(turn, "north");

        var graph = turn.Display.Watcher.Graph;

        Assert.Single(graph.Rooms);
        Assert.Equal("West of House", graph.Rooms[0].Name);
    }

    [Fact]
    public void AHeadingWithNothingUnderItIsNotARoom()
    {
        // A title, an act list, a content warning: bolded exactly as a
        // room heading is, and standing on its own. The shape of the
        // page is what tells them apart, not the words.
        var turn = Story();
        Say(turn, "ANCHORHEAD\n", GlkStyle.Subheader);
        Prompt(turn, "");

        Assert.Empty(turn.Display.Watcher.Graph.Rooms);
    }

    [Fact]
    public void ABoldWordInTheMiddleOfALineIsNotAHeading()
    {
        // A story that prints every object's name in the heading style
        // would otherwise fill the map with rooms called "lamp".
        var turn = Story();
        Say(turn, "You can see a ", GlkStyle.Normal);
        Say(turn, "brass lamp", GlkStyle.Subheader);
        Say(turn, " here.\n", GlkStyle.Normal);
        Say(turn, "It is dark.\n>", GlkStyle.Normal);
        Prompt(turn, "take lamp");

        Assert.Empty(turn.Display.Watcher.Graph.Rooms);
    }

    [Fact]
    public void AHeadingOwningItsLineIsAHeadingHoweverItIsSpaced()
    {
        var turn = Story();
        Say(turn, "   Forest Path   \n", GlkStyle.Subheader);
        Say(turn, "This is a path.\n>", GlkStyle.Normal);
        Prompt(turn, "north");

        Assert.Equal("Forest Path", turn.Display.Watcher.Graph.Rooms[0].Name);
    }

    [Fact]
    public void TheDirectionTypedJoinsTwoRoomsAcrossTheTurnBetween()
    {
        var turn = Story();

        Say(turn, "West of House\n", GlkStyle.Subheader);
        Say(turn, "An open field.\n>", GlkStyle.Normal);
        Prompt(turn, "north");

        Say(turn, "North of House\n", GlkStyle.Subheader);
        Say(turn, "The north side.\n>", GlkStyle.Normal);
        Prompt(turn, "south");

        var graph = turn.Display.Watcher.Graph;

        Assert.Equal(2, graph.Rooms.Count);
        Assert.Equal(1, graph.Rooms[0].Exits[Direction.North]);
        Assert.Equal((0, -1), graph.Rooms[1].Position);
    }

    [Fact]
    public void AStoryAskingForOneKeyHasNotFinishedItsTurn()
    {
        // A menu or a [MORE] prompt. The story has not necessarily said
        // where the player is, and taking a key for the end of a turn
        // would spend the direction they typed on the wrong room.
        var turn = Story();
        Say(turn, "PRESS ANY KEY\n", GlkStyle.Subheader);
        Say(turn, "to begin.\n", GlkStyle.Normal);

        turn.Inner.Next = GlkInput.KeyPress(turn.Window, ' ');
        turn.Display.WaitForInput([], [turn.Window], null);

        Assert.Empty(turn.Display.Watcher.Graph.Rooms);

        // And what it said still counts once a command is asked for.
        Say(turn, ">", GlkStyle.Normal);
        Prompt(turn, "look");

        Assert.Equal("PRESS ANY KEY", turn.Display.Watcher.Graph.Rooms[0].Name);
    }

    [Fact]
    public void ARoomNamedTwiceIsTheSameRoom()
    {
        // Nothing behind the name in a Glulx story, so two rooms a game
        // calls the same thing are one room here.
        var turn = Story();

        Say(turn, "Maze\n", GlkStyle.Subheader);
        Say(turn, "All alike.\n>", GlkStyle.Normal);
        Prompt(turn, "north");

        Say(turn, "Maze\n", GlkStyle.Subheader);
        Say(turn, "All alike.\n>", GlkStyle.Normal);
        Prompt(turn, "north");

        var graph = turn.Display.Watcher.Graph;

        Assert.Single(graph.Rooms);
        Assert.Equal([Direction.North], graph.Rooms[0].Tried);
    }
}

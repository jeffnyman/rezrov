using Rezrov.Cli;
using Rezrov.Mapping;

namespace Rezrov.Tests;

/// <summary>
/// [mapping] Turning the two halves of a turn, where the player is
/// standing and what they typed, into a map.
/// </summary>
public class RoomWatcherTests
{
    [Fact]
    public void ADirectionIsSpentOnTheRoomSeenAfterIt()
    {
        // The direction is typed at one prompt and answered at the next.
        // Spending it on the room it was typed in would draw every
        // passage a turn early, and out of the wrong room.
        var watcher = new RoomWatcher();
        watcher.Standing(1, "West of House");
        watcher.Typed("north");
        watcher.Standing(2, "North of House");

        var start = watcher.Graph.Rooms[0];

        Assert.Equal(2, watcher.Graph.Rooms.Count);
        Assert.Equal(1, start.Exits[Direction.North]);
        Assert.Equal("North of House", watcher.Graph.Current!.Name);
    }

    [Fact]
    public void ADirectionIsSpentOnceAndThenForgotten()
    {
        // A turn that types nothing readable must not reuse the last
        // direction that was.
        var watcher = new RoomWatcher();
        watcher.Standing(1, "West of House");
        watcher.Typed("north");
        watcher.Standing(2, "North of House");
        watcher.Typed("take leaflet");
        watcher.Standing(3, "Behind House");

        var second = watcher.Graph.Rooms[1];

        Assert.Equal(3, watcher.Graph.Rooms.Count);
        Assert.Empty(second.Exits);
        Assert.Empty(second.Tried);
    }

    [Fact]
    public void ALineOfSeveralCommandsDrawsNoPassage()
    {
        // The parser walked three rooms and only the last one was seen.
        // Reading the first word would draw a passage north from the
        // room they started in to somewhere three rooms away.
        var watcher = new RoomWatcher();
        watcher.Standing(1, "West of House");
        watcher.Typed("n. n. u");
        watcher.Standing(2, "Up a Tree");

        Assert.Equal(2, watcher.Graph.Rooms.Count);
        Assert.Empty(watcher.Graph.Rooms[0].Exits);
        Assert.Empty(watcher.Graph.Rooms[0].Tried);
    }

    [Fact]
    public void ADirectionThatLeavesThePlayerWhereTheyWereIsOnlyTried()
    {
        var watcher = new RoomWatcher();
        watcher.Standing(1, "West of House");
        watcher.Typed("west");
        watcher.Standing(1, "West of House");

        Assert.Single(watcher.Graph.Rooms);
        Assert.Empty(watcher.Graph.Rooms[0].Exits);
        Assert.Equal([Direction.West], watcher.Graph.Rooms[0].Tried);
    }

    [Fact]
    public void NothingIsMappedBeforeTheFirstPrompt()
    {
        var watcher = new RoomWatcher();
        watcher.Typed("north");

        Assert.Empty(watcher.Graph.Rooms);
        Assert.Null(watcher.Graph.Current);
    }
}

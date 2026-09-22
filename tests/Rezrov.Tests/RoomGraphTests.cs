using Rezrov.Mapping;

namespace Rezrov.Tests;

/// <summary>
/// [mapping] What a stream of rooms and typed directions is allowed to
/// become, and what it is not.
/// </summary>
public class RoomGraphTests
{
    [Fact]
    public void TheFirstRoomIsWhereTheMapStarts()
    {
        var walk = new MapWalk();

        var start = walk.To("West of House");

        Assert.Equal((0, 0), start.Position);
        Assert.Same(start, walk.Graph.Current);
        Assert.Empty(start.Exits);
    }

    [Fact]
    public void WalkingSomewhereNewMintsOnePassageOutOfTheRoomLeft()
    {
        var walk = new MapWalk();
        walk.To("West of House");
        var north = walk.To("North of House", "north");

        var start = walk.Graph.Rooms[0];

        Assert.Equal(north.Id, start.Exits[Direction.North]);
        Assert.Equal((0, -1), north.Position);
        Assert.Same(north, walk.Graph.Current);

        // And nothing at all in the other direction. Walking north into a
        // room says nothing about whether south comes back out of it, and
        // these games are full of passages that run one way.
        Assert.Empty(north.Exits);
    }

    [Fact]
    public void WalkingBackClosesTheLoop()
    {
        var walk = new MapWalk();
        walk.To("West of House");
        walk.To("North of House", "north");
        var back = walk.To("West of House", "south");

        Assert.Equal(0, back.Id);
        Assert.Equal(back.Id, walk.Graph.Rooms[1].Exits[Direction.South]);
        Assert.Equal((0, 0), back.Position);
        Assert.Equal(2, walk.Graph.Rooms.Count);
    }

    [Fact]
    public void ADirectionThatDoesNotMoveYouIsOnlyRecordedAsTried()
    {
        var walk = new MapWalk();
        var start = walk.To("West of House");
        walk.To("West of House", "north");

        Assert.Empty(start.Exits);
        Assert.Equal([Direction.North], start.Tried);
        Assert.Single(walk.Graph.Rooms);
    }

    [Fact]
    public void ATriedDirectionThatLaterWorksBecomesAPassage()
    {
        // The window was shut, and then it was open.
        var walk = new MapWalk();
        var start = walk.To("Behind House");
        walk.To("Behind House", "in");
        Assert.Equal([Direction.In], start.Tried);

        var kitchen = walk.To("Kitchen", "in");

        Assert.Equal(kitchen.Id, start.Exits[Direction.In]);
        Assert.Empty(start.Tried);
    }

    [Fact]
    public void APassageAlreadyKnownIsNotUndoneByADoorThatWillNotOpen()
    {
        var walk = new MapWalk();
        var start = walk.To("Behind House");
        var kitchen = walk.To("Kitchen", "in");
        walk.To("Behind House", "out");
        walk.To("Behind House", "in");

        Assert.Equal(kitchen.Id, start.Exits[Direction.In]);
        Assert.Empty(start.Tried);
    }

    [Fact]
    public void AMoveWithNoDirectionNamedMintsNoPassage()
    {
        // The trap door, the spell, the cutscene. The player got
        // somewhere, and no passage was walked to do it.
        var walk = new MapWalk();
        var start = walk.To("Living Room");
        var cellar = walk.To("Cellar", "open trap door");

        Assert.Empty(start.Exits);
        Assert.Empty(start.Tried);
        Assert.NotNull(cellar.Position);
        Assert.NotEqual(start.Position, cellar.Position);
        Assert.Same(cellar, walk.Graph.Current);
    }

    [Fact]
    public void TwoRoomsThatShareANameStayApartWhenTheGameCanTellThem()
    {
        var walk = new MapWalk();
        walk.ToObject(1, "Maze");
        walk.ToObject(2, "Maze", "north");

        Assert.Equal(2, walk.Graph.Rooms.Count);
        Assert.Equal(1, walk.Graph.Rooms[0].Exits[Direction.North]);
    }

    [Fact]
    public void ARoomKnownOnlyByNameFoldsItsNamesakesIntoIt()
    {
        // The loss belongs to the game, which named two rooms the same
        // thing and gave nothing else to tell them apart. Inventing a
        // difference would be worse than recording the one it admits to.
        var graph = new RoomGraph();
        graph.Observe(RoomKey.ForName("Maze"), "Maze", null);
        graph.Observe(RoomKey.ForName("Maze"), "Maze", Direction.North);

        Assert.Single(graph.Rooms);
        Assert.Equal([Direction.North], graph.Rooms[0].Tried);
    }

    [Fact]
    public void ARoomKeepsTheNameItWasFirstSeenUnder()
    {
        // A status line that carries the time of day renames the room
        // every turn without the player going anywhere.
        var graph = new RoomGraph();
        graph.Observe(RoomKey.ForObject(7), "Back Alley", null);
        graph.Observe(RoomKey.ForObject(7), "Back Alley, noon", Direction.North);

        Assert.Single(graph.Rooms);
        Assert.Equal("Back Alley", graph.Rooms[0].Name);
    }

    [Fact]
    public void ARoomAlreadyOnTheMapIsNeverPickedUpAndPutDownAgain()
    {
        // Walking a loop must not rearrange the map under the player.
        var walk = new MapWalk();
        walk.To("A");
        walk.To("B", "east");
        walk.To("C", "south");
        var before = walk.Where("A");

        walk.To("A", "northwest");

        Assert.Equal(before, walk.Where("A"));
    }
}

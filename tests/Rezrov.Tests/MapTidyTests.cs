using Rezrov.Mapping;

namespace Rezrov.Tests;

/// <summary>
/// [mapping] Putting rooms back where their passages say they belong,
/// once the map has stopped being added to.
/// </summary>
public class MapTidyTests
{
    /// <summary>
    /// How many passages have their two rooms exactly one cell apart in
    /// the direction they were walked, which is what the tidy pass is
    /// trying to raise and what the drawing can join with a line.
    /// </summary>
    private static int Adjacent(RoomGraph graph)
    {
        var adjacent = 0;

        foreach (var room in graph.Rooms)
        {
            foreach (var (direction, id) in room.Exits)
            {
                if (Directions.Cell(direction) is not { } step
                    || room.Position is not { } here
                    || graph.Rooms[id].Position is not { } there)
                {
                    continue;
                }

                if (there.X - here.X == step.X && there.Y - here.Y == step.Y)
                {
                    adjacent++;
                }
            }
        }

        return adjacent;
    }

    [Fact]
    public void ARoomPutDownBeforeItsPassageWasKnownIsBroughtBack()
    {
        // Walking in through a window places the study wherever there is
        // room, because nothing yet says where it belongs. The way back
        // out says: it is west of the hall. Nothing moved it at the time,
        // because a room already on the map is never moved while the map
        // is being added to.
        var walk = new MapWalk();
        walk.To("Hall");
        walk.To("Study", "in");
        walk.To("Hall", "east");

        Assert.Equal((-1, -1), walk.Where("Study"));
        Assert.Equal(0, Adjacent(walk.Graph));

        var moves = MapTidy.Tidy(walk.Graph);

        Assert.Equal(1, moves);
        Assert.Equal(1, Adjacent(walk.Graph));

        // Which of the two rooms moved is not the point and not fixed:
        // either end may give way. What the pass promises is the
        // arrangement, so that is what is asked for.
        Assert.Equal((1, 0), Apart(walk, "Study", "Hall"));
    }

    [Fact]
    public void TidyingNeverLosesGroundOnAnyWalk()
    {
        // The one thing it must never do. Every candidate is taken only
        // when the count really rises, so this holds by construction and
        // is checked because a later change could quietly break it.
        foreach (var walk in Walks())
        {
            var before = Adjacent(walk.Graph);
            MapTidy.Tidy(walk.Graph);

            Assert.True(Adjacent(walk.Graph) >= before);
        }
    }

    [Fact]
    public void EveryRoomStillHasACellOfItsOwn()
    {
        foreach (var walk in Walks())
        {
            MapTidy.Tidy(walk.Graph);

            var cells = walk.Graph.Rooms
                .Where(room => room.Position is not null)
                .Select(room => room.Position!.Value)
                .ToList();

            Assert.Equal(cells.Count, cells.Distinct().Count());
        }
    }

    [Fact]
    public void TidyingTwiceChangesNothingTheSecondTime()
    {
        foreach (var walk in Walks())
        {
            MapTidy.Tidy(walk.Graph);
            var settled = Cells(walk.Graph);

            Assert.Equal(0, MapTidy.Tidy(walk.Graph));
            Assert.Equal(settled, Cells(walk.Graph));
        }
    }

    [Fact]
    public void TheSameWalkAlwaysTidiesToTheSameMap()
    {
        var first = Walks().Select(walk => { MapTidy.Tidy(walk.Graph); return Cells(walk.Graph); });
        var again = Walks().Select(walk => { MapTidy.Tidy(walk.Graph); return Cells(walk.Graph); });

        Assert.Equal(first, again);
    }

    [Fact]
    public void AMapWithNothingToArrangeIsLeftAlone()
    {
        var walk = new MapWalk();
        walk.To("Hall");
        walk.To("Study", "in");

        Assert.Equal(0, MapTidy.Tidy(walk.Graph));
        Assert.Equal(0, MapTidy.Tidy(new RoomGraph()));
    }

    [Fact]
    public void AStaircaseCountsTowardsTheArrangementLikeAnyOtherPassage()
    {
        // Up is laid out as north, so a cellar reached down a staircase
        // wants the cell below just as a passage south would.
        var walk = new MapWalk();
        walk.To("Kitchen");
        walk.To("Cellar", "in");
        walk.To("Kitchen", "up");

        MapTidy.Tidy(walk.Graph);

        Assert.Equal((0, -1), Apart(walk, "Cellar", "Kitchen"));
    }

    [Fact]
    public void TwoRoomsInEachOthersPlacesChangeOver()
    {
        // The crossing is held where it is by two passages that are
        // already right, so it cannot be the one to give way. The hall
        // and the attic were put down before anything said where they
        // belonged, and they landed in each other's cells. Nothing but
        // trading places fixes that.
        var walk = Crossed();

        Assert.Equal(4, Adjacent(walk.Graph));

        MapTidy.Tidy(walk.Graph);

        Assert.Equal(7, Adjacent(walk.Graph));
        Assert.Equal((0, -1), Apart(walk, "Crossing", "Hall"));
        Assert.Equal((-1, -1), Apart(walk, "Crossing", "Attic"));

        // And the crossing itself never moved.
        Assert.Equal((1, 0), Apart(walk, "Crossing", "East Room"));
        Assert.Equal((-1, 0), Apart(walk, "Crossing", "West Room"));
    }

    /// <summary>
    /// A map whose only repair is a swap: see
    /// <see cref="TwoRoomsInEachOthersPlacesChangeOver"/>.
    /// </summary>
    private static MapWalk Crossed()
    {
        var walk = new MapWalk();
        walk.To("Crossing");
        walk.To("East Room", "east");
        walk.To("Crossing", "west");
        walk.To("West Room", "west");
        walk.To("Crossing", "east");
        walk.To("Hall", "in");
        walk.To("Crossing", "out");
        walk.To("Attic", "in");
        walk.To("Crossing", "out");
        walk.To("Hall", "north");
        walk.To("Crossing", "south");
        walk.To("Attic", "northwest");
        return walk;
    }

    /// <summary>How far the second room sits from the first.</summary>
    private static (int X, int Y) Apart(MapWalk walk, string from, string to)
    {
        var here = walk.Where(from);
        var there = walk.Where(to);
        return (there.X - here.X, there.Y - here.Y);
    }

    private static List<(int X, int Y)> Cells(RoomGraph graph) =>
        [.. graph.Rooms.Select(room => room.Position ?? (0, 0))];

    /// <summary>
    /// A handful of walks with drift in them, for the checks that have to
    /// hold whatever the map looks like.
    /// </summary>
    private static List<MapWalk> Walks()
    {
        var walks = new List<MapWalk>();

        var loop = new MapWalk();
        loop.To("A");
        loop.To("B", "east");
        loop.To("C", "north");
        loop.To("D", "west");
        loop.To("E", "south");
        walks.Add(loop);

        var portal = new MapWalk();
        portal.To("Hall");
        portal.To("Study", "in");
        portal.To("Hall", "east");
        portal.To("Tower", "up");
        portal.To("Hall", "down");
        walks.Add(portal);

        var maze = new MapWalk();
        maze.To("One");
        maze.To("Two", "north");
        maze.To("Three", "northeast");
        maze.To("One", "southwest");
        maze.To("Four", "west");
        maze.To("Five", "south");
        maze.To("One", "east");
        walks.Add(maze);

        var stranded = new MapWalk();
        stranded.To("Start");
        stranded.To("Adrift", "say xyzzy");
        stranded.To("Back", "north");
        walks.Add(stranded);

        walks.Add(Crossed());

        // A sprawl with contradictions in it: the same seventeen rooms
        // walked into over and over from whatever direction comes next,
        // which is what a real map looks like once a maze is in it.
        // Nothing here is random; the sequence is the same every run.
        string[] compass =
            ["north", "east", "south", "west", "northeast", "up", "in", "down", "southwest", "west"];

        var sprawl = new MapWalk();
        sprawl.To("R0");
        for (var step = 1; step <= 80; step++)
        {
            sprawl.To("R" + (step % 17), compass[step % compass.Length]);
        }

        walks.Add(sprawl);

        return walks;
    }
}

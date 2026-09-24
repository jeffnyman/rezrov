using Rezrov.Mapping;

namespace Rezrov.Tests;

/// <summary>
/// [mapping] Keeping a map between one session and the next.
/// </summary>
public class MapFileTests
{
    [Fact]
    public void AMapComesBackAsTheMapItWas()
    {
        var walk = new MapWalk();
        walk.To("West of House");
        walk.To("North of House", "north");
        walk.To("Behind House", "east");
        walk.To("Kitchen", "in");
        walk.To("Attic", "up");
        walk.To("Kitchen", "down");
        walk.To("Kitchen", "north");

        var before = walk.Graph;
        var after = MapFile.Read(new StringReader(MapFile.Written(before)));

        Assert.NotNull(after);
        Assert.Equal(MapFile.Written(before), MapFile.Written(after));
        Assert.Equal(before.Rooms.Count, after.Rooms.Count);

        for (var i = 0; i < before.Rooms.Count; i++)
        {
            Assert.Equal(before.Rooms[i].Name, after.Rooms[i].Name);
            Assert.Equal(before.Rooms[i].Key, after.Rooms[i].Key);
            Assert.Equal(before.Rooms[i].Position, after.Rooms[i].Position);
            Assert.Equal(before.Rooms[i].Exits, after.Rooms[i].Exits);
            Assert.Equal(before.Rooms[i].Tried.Order(), after.Rooms[i].Tried.Order());
        }

        Assert.Equal(before.Current!.Name, after.Current!.Name);
    }

    [Fact]
    public void AMapReadBackIsStillTheSameMapToWalkOn()
    {
        // The point of keeping the cells rather than working them out
        // again: a room walked into after the map came back has to
        // land where it would have landed had the game never stopped.
        var walk = new MapWalk();
        walk.To("Hall");
        walk.To("Study", "west");

        var carried = MapFile.Read(new StringReader(MapFile.Written(walk.Graph)))!;
        var kept = walk.Graph;

        carried.Observe(RoomKey.ForObject(99), "Cellar", Direction.Down);
        kept.Observe(RoomKey.ForObject(99), "Cellar", Direction.Down);

        Assert.Equal(MapFile.Written(kept), MapFile.Written(carried));
    }

    [Fact]
    public void ARoomKnownOnlyByItsNameKeepsThatName()
    {
        // What a Glulx story leaves to go on, and the one case where
        // the key is the name rather than a number.
        var graph = new RoomGraph();
        graph.Observe(RoomKey.ForName("At End Of Road"), "At End Of Road", null);
        graph.Observe(RoomKey.ForName("Inside Building"), "Inside Building", Direction.In);

        var after = MapFile.Read(new StringReader(MapFile.Written(graph)))!;

        Assert.Equal(RoomKey.ForName("At End Of Road"), after.Rooms[0].Key);
        Assert.Equal("Inside Building", after.Rooms[1].Name);

        // And walking back into a room it already knows finds that
        // room rather than minting a second one.
        after.Observe(RoomKey.ForName("At End Of Road"), "At End Of Road", Direction.Out);

        Assert.Equal(2, after.Rooms.Count);
    }

    [Fact]
    public void ARoomNameWithSpacesInItSurvives()
    {
        var graph = new RoomGraph();
        graph.Observe(RoomKey.ForObject(4), "The Great Underground Empire", null);

        var after = MapFile.Read(new StringReader(MapFile.Written(graph)))!;

        Assert.Equal("The Great Underground Empire", after.Rooms[0].Name);
    }

    [Fact]
    public void SomethingThatIsNotAMapIsNotRead()
    {
        // A map that cannot be read is not a failure of the run: the
        // player gets an empty one and fills it in by playing, which
        // is what they would have had anyway.
        Assert.Null(MapFile.Read(new StringReader("")));
        Assert.Null(MapFile.Read(new StringReader("ZORK I: The Great Underground Empire")));
        Assert.Null(MapFile.Read(new StringReader("rezrov map 99\nroom 0 0 0 1 Somewhere\n")));
    }

    [Fact]
    public void ALineThisVersionDoesNotKnowIsPassedOver()
    {
        var text = "rezrov map 1\nroom 0 0 0 1 Clearing\nweather sunny\nhere 0\n";

        var after = MapFile.Read(new StringReader(text));

        Assert.NotNull(after);
        Assert.Equal("Clearing", Assert.Single(after.Rooms).Name);
        Assert.Equal("Clearing", after.Current!.Name);
    }

    [Fact]
    public void ARoomThatWasNeverPlacedStaysUnplaced()
    {
        // Placement can run out of anywhere to put a room, and a cell
        // it never had must not be invented on the way back in.
        var after = MapFile.Read(new StringReader("rezrov map 1\nroom 0 - - 1 Nowhere\n"))!;

        Assert.Null(Assert.Single(after.Rooms).Position);
    }
}

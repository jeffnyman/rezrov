using Rezrov.Mapping;

namespace Rezrov.Tests;

/// <summary>
/// [mapping] What the grid can show honestly, and what it says instead
/// of improvising the rest.
/// </summary>
public class MapDrawingTests
{
    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    [Fact]
    public void AnEmptyMapSaysSo()
    {
        Assert.Equal("No rooms have been mapped.\n", MapDrawing.Draw(new RoomGraph()));
    }

    [Fact]
    public void APassageWalkedBothWaysIsAPlainLine()
    {
        var walk = new MapWalk();
        walk.To("Clearing");
        walk.To("Grating Room", "east");
        walk.To("Clearing", "west");

        Assert.Equal(
            Lines(
                "+------------------+     +------------------+",
                "|*Clearing         |-----| Grating Room     |",
                "+------------------+     +------------------+"),
            MapDrawing.Draw(walk.Graph));
    }

    [Fact]
    public void APassageWalkedOneWayWearsAnArrowheadWhereItPoints()
    {
        // Nothing known brings you back, and the map says that rather
        // than assuming the passage runs both ways. Half these games
        // turn on a drop or a door that opens from one side.
        var walk = new MapWalk();
        walk.To("Clearing");
        walk.To("Grating Room", "east");

        Assert.Equal(
            Lines(
                "+------------------+     +------------------+",
                "| Clearing         |---->|*Grating Room     |",
                "+------------------+     +------------------+"),
            MapDrawing.Draw(walk.Graph));
    }

    [Fact]
    public void AStaircaseIsDottedRatherThanDrawnAsAPassageNorth()
    {
        // The rooms are stacked north and south because that is the only
        // readable place to put them, so the line has to be the thing
        // that says they are not really north and south of each other.
        var walk = new MapWalk();
        walk.To("Kitchen");
        walk.To("Attic", "up");
        walk.To("Kitchen", "down");
        walk.To("Cellar", "down");

        Assert.Equal(
            Lines(
                "+------------------+",
                "| Attic            |",
                "+------------------+",
                "          :",
                "+------------------+",
                "| Kitchen          |",
                "+------------------+",
                "          :",
                "+------------------+",
                "|*Cellar           |",
                "+------------------+"),
            MapDrawing.Draw(walk.Graph));
    }

    [Fact]
    public void TwoPassagesWantingOneLineLeaveTheSecondNamed()
    {
        // One gap between two boxes, and a room that is both north of
        // here and up from here. The first line drawn keeps it.
        var walk = new MapWalk();
        walk.To("Gallery");
        walk.To("Studio", "north");
        walk.To("Gallery", "south");
        walk.To("Studio", "up");

        Assert.Equal(
            Lines(
                "+------------------+",
                "|*Studio           |",
                "+------------------+",
                "          |",
                "+------------------+",
                "| Gallery          |",
                "+------------------+",
                "",
                "Passages not drawn:",
                "  Gallery             u    Studio              (line already taken)"),
            MapDrawing.Draw(walk.Graph));
    }

    [Fact]
    public void ANameTooLongIsCutAndGivenInFullUnderneath()
    {
        var walk = new MapWalk();
        walk.To("The Loud Room Beneath the Reservoir");
        walk.To("Damp Cave", "east");

        Assert.Equal(
            Lines(
                "+------------------+     +------------------+",
                "| The Loud Room Ben|---->|*Damp Cave        |",
                "+------------------+     +------------------+",
                "",
                "Names cut to fit:",
                "  The Loud Room Beneath the Reservoir"),
            MapDrawing.Draw(walk.Graph));
    }

    [Fact]
    public void APassageWithNowhereToPointIsNamedUnderneath()
    {
        var walk = new MapWalk();
        walk.To("Behind House");
        walk.To("Kitchen", "in");

        var drawing = MapDrawing.Draw(walk.Graph);

        Assert.Contains("Behind House", drawing, StringComparison.Ordinal);
        Assert.Contains("(nowhere to point on a grid)", drawing, StringComparison.Ordinal);

        // And no line was invented for it anywhere on the grid.
        var grid = drawing[..drawing.IndexOf("Passages not drawn:", StringComparison.Ordinal)];
        Assert.DoesNotContain('/', grid);
        Assert.DoesNotContain('\\', grid);
        Assert.DoesNotContain(':', grid);
    }

    [Fact]
    public void APassageWhoseRoomsDriftedOutOfLineIsNamedRatherThanDrawn()
    {
        // The diagonal yielded when the cell it wanted was taken, so it
        // no longer points at the room it leads to. A line would have to
        // bend to get there, and a bent line reads as a passage through
        // whatever it bends around.
        var walk = new MapWalk();
        walk.To("Clearing");
        walk.To("Forest", "north");
        walk.To("Grating", "east");
        walk.To("Path", "south");
        walk.To("Clearing", "west");
        walk.To("Canyon", "northeast");

        Assert.Contains(
            "  Clearing            ne   Canyon              (no clear line)",
            MapDrawing.Draw(walk.Graph),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TwoRoomsNoLongerNeighborsAreStillJoinedAcrossTheEmptyCells()
    {
        // Rooms drift apart as the map is shoved aside to make space
        // elsewhere. A straight line over the cells between them is
        // still the truth: it goes the way the player walked and passes
        // through nothing.
        var walk = new MapWalk();
        walk.To("Cellar");
        walk.To("Vault", "in");
        walk.To("Ledge", "north");
        walk.To("Landing", "east");
        walk.To("Cellar", "south");

        var reach = new string(' ', 35);

        Assert.Equal(
            Lines(
                "+------------------+     +------------------+",
                "| Ledge            |---->| Landing          |",
                "+------------------+     +------------------+",

                // The short line at the left is the Vault's own way up
                // to the Ledge, which nothing is known to come back
                // down, so it keeps its arrowhead.
                new string(' ', 10) + "^" + new string(' ', 24) + "|",
                "+------------------+" + new string(' ', 15) + "|",
                "| Vault            |" + new string(' ', 15) + "|",
                "+------------------+" + new string(' ', 15) + "|",
                reach + "v",
                new string(' ', 25) + "+------------------+",
                new string(' ', 25) + "|*Cellar           |",
                new string(' ', 25) + "+------------------+",
                "",
                "Passages not drawn:",
                "  Cellar              in   Vault               (nowhere to point on a grid)"),
            MapDrawing.Draw(walk.Graph));
    }

    [Fact]
    public void ALineWithARoomStandingInItIsNamedInstead()
    {
        // The same walk, with somewhere in the way. Drawing the line
        // would run it straight over a room that has nothing to do with
        // the passage.
        var walk = new MapWalk();
        walk.To("Cellar");
        walk.To("Vault", "in");
        walk.To("Ledge", "north");
        walk.To("Landing", "east");
        walk.To("Middle", "down");
        walk.To("Landing", "up");
        walk.To("Cellar", "south");

        Assert.Contains(
            "  Landing             s    Cellar              (no clear line)",
            MapDrawing.Draw(walk.Graph),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ADiagonalIsDrawnWhereTheRoomsEndedUpNeighbors()
    {
        var walk = new MapWalk();
        walk.To("Forest Path");
        walk.To("Clearing", "northeast");
        walk.To("Forest Path", "southwest");

        // A cell with no room in it is left blank, so the room to the
        // northeast stands alone on the top row.
        var over = new string(' ', 25);

        Assert.Equal(
            Lines(
                over + "+------------------+",
                over + "| Clearing         |",
                over + "+------------------+",
                new string(' ', 22) + "/",
                "+------------------+",
                "|*Forest Path      |",
                "+------------------+"),
            MapDrawing.Draw(walk.Graph));
    }

    [Fact]
    public void AOneWayDiagonalIsNamedBecauseTheGlyphCannotCarryAnArrow()
    {
        // One character of line, and no room on it to say which way the
        // passage runs. Saying so underneath beats drawing something
        // that reads as a passage walked both ways.
        var walk = new MapWalk();
        walk.To("Forest Path");
        walk.To("Clearing", "northeast");

        var drawing = MapDrawing.Draw(walk.Graph);

        Assert.DoesNotContain('/', drawing);
        Assert.Contains("(one way)", drawing, StringComparison.Ordinal);
    }
}

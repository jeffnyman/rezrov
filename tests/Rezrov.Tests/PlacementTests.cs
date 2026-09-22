using Rezrov.Mapping;

namespace Rezrov.Tests;

/// <summary>
/// [mapping] Where a newly found room is put, and what happens when the
/// cell it wants is already taken.
/// </summary>
public class PlacementTests
{
    [Fact]
    public void ARoomGoesInTheDirectionItWasWalked()
    {
        var walk = new MapWalk();
        walk.To("Clearing");
        walk.To("Forest", "east");
        walk.To("Path", "north");

        Assert.Equal((0, 0), walk.Where("Clearing"));
        Assert.Equal((1, 0), walk.Where("Forest"));
        Assert.Equal((1, -1), walk.Where("Path"));
    }

    [Fact]
    public void ARoomUpAStaircaseGoesNorthAndOneDownGoesSouth()
    {
        var up = new MapWalk();
        up.To("Kitchen");
        up.To("Attic", "up");
        Assert.Equal((0, -1), up.Where("Attic"));

        var down = new MapWalk();
        down.To("Living Room");
        down.To("Cellar", "down");
        Assert.Equal((0, 1), down.Where("Cellar"));
    }

    [Fact]
    public void ACardinalMoveShovesTheMapAsideToKeepItsDirection()
    {
        // Round a loop and out the far side into somewhere new, which
        // wants the cell the first room is sitting in. The direction the
        // player walked is the one thing they will check, so the new room
        // gets the cell and everything at or beyond it moves one south.
        var walk = new MapWalk();
        walk.To("A");
        walk.To("B", "east");
        walk.To("C", "north");
        walk.To("D", "west");
        walk.To("E", "south");

        Assert.Equal((0, 0), walk.Where("E"));
        Assert.Equal((0, -1), walk.Where("D"));

        // The whole half of the map beyond the line moved together, so
        // everything out there kept its place relative to everything else.
        Assert.Equal((0, 1), walk.Where("A"));
        Assert.Equal((1, 1), walk.Where("B"));
        Assert.Equal((1, -1), walk.Where("C"));
    }

    [Fact]
    public void ADiagonalYieldsRatherThanShovingBothWaysAtOnce()
    {
        // Shoving along two axes at once moves far more of the map than
        // one room is worth, so a diagonal settles for the first free
        // cell in the ring around the one it wanted. That can land it
        // somewhere the direction does not really point, and the drawing
        // says so rather than drawing a line that is not there.
        var walk = new MapWalk();
        walk.To("A");
        walk.To("B", "north");
        walk.To("C", "east");
        walk.To("D", "south");
        walk.To("A", "west");
        walk.To("E", "northeast");

        Assert.Equal((1, -1), walk.Where("C"));
        Assert.Equal((0, -2), walk.Where("E"));
    }

    [Fact]
    public void ADirectionWithNowhereToPointTakesTheNearestFreeCell()
    {
        var walk = new MapWalk();
        walk.To("Behind House");
        walk.To("Kitchen", "in");

        Assert.Equal((0, 0), walk.Where("Behind House"));
        Assert.NotEqual((0, 0), walk.Where("Kitchen"));
    }

    [Fact]
    public void TheSameWalkAlwaysDrawsTheSameMap()
    {
        // Nothing here may depend on the order a dictionary happens to
        // hand things back in.
        static IReadOnlyList<(int X, int Y)> Run()
        {
            var walk = new MapWalk();
            walk.To("A");
            walk.To("B", "east");
            walk.To("C", "north");
            walk.To("D", "west");
            walk.To("E", "south");
            walk.To("F", "in");
            walk.To("G", "southwest");
            return [.. walk.Graph.Rooms.Select(room => room.Position!.Value)];
        }

        Assert.Equal(Run(), Run());
    }
}

using Rezrov.Gui;

namespace Rezrov.Tests;

/// <summary>
/// [mapping] Turning a played map into the shapes a window draws, which
/// is where a passage stops being a direction and becomes a line with
/// two ends.
/// </summary>
public class MapShotTests
{
    [Fact]
    public void ThereIsNothingToDrawBeforeAnyTurn()
    {
        var shot = MapShot.Of(new MapWalk().Graph);

        Assert.Empty(shot.Boxes);
        Assert.Empty(shot.Lines);
        Assert.Equal((0d, 0d, 0d, 0d), shot.Extent);
    }

    [Fact]
    public void ARoomIsABoxWhereItsCellSays()
    {
        var walk = new MapWalk();
        walk.To("West of House");
        walk.To("North of House", "north");

        var shot = MapShot.Of(walk.Graph);

        var start = shot.Boxes.Single(b => b.Name == "West of House");
        var north = shot.Boxes.Single(b => b.Name == "North of House");

        // North is up the page, which is the one place the map's own
        // axes and a window's agree without being made to.
        Assert.Equal(start.Center.X, north.Center.X);
        Assert.Equal(start.Center.Y - MapShot.PitchY, north.Center.Y);
    }

    [Fact]
    public void TheRoomThePlayerIsInIsTheOnlyCurrentOne()
    {
        var walk = new MapWalk();
        walk.To("Kitchen");
        walk.To("Cellar", "down");

        var shot = MapShot.Of(walk.Graph);

        Assert.Equal("Cellar", shot.Boxes.Single(b => b.Current).Name);
    }

    [Fact]
    public void ALineRunsBetweenTwoBoxEdgesAndNotThroughThem()
    {
        var walk = new MapWalk();
        walk.To("Clearing");
        walk.To("Forest", "east");

        var shot = MapShot.Of(walk.Graph);
        var line = Assert.Single(shot.Lines);

        var west = shot.Boxes.Single(b => b.Name == "Clearing");
        var east = shot.Boxes.Single(b => b.Name == "Forest");

        // It leaves the right-hand side of one box and meets the
        // left-hand side of the other, so the gap between them is all
        // the line there is.
        Assert.Equal(west.Left + MapShot.BoxWidth, line.X1);
        Assert.Equal(east.Left, line.X2);
        Assert.Equal(west.Center.Y, line.Y1);
        Assert.Equal(east.Center.Y, line.Y2);
    }

    [Fact]
    public void ADiagonalLeavesTheCornerOfItsBox()
    {
        // What the character map could not do. On a grid a northeast
        // passage had to be bent into steps or left out; here it is the
        // straight line it always was.
        var walk = new MapWalk();
        walk.To("Shore");
        walk.To("Headland", "northeast");

        var shot = MapShot.Of(walk.Graph);
        var line = Assert.Single(shot.Lines);

        Assert.True(line.X2 > line.X1, "the line should run east");
        Assert.True(line.Y2 < line.Y1, "the line should run north");
    }

    [Fact]
    public void APassageWalkedBothWaysIsOneLineWithNoArrowOnIt()
    {
        var walk = new MapWalk();
        walk.To("Attic");
        walk.To("Landing", "down");
        walk.To("Attic", "up");

        var shot = MapShot.Of(walk.Graph);
        var line = Assert.Single(shot.Lines);

        Assert.False(line.ArrowAtOne);
        Assert.False(line.ArrowAtTwo);
        Assert.Equal(2, shot.Passages);
    }

    [Fact]
    public void APassageWalkedOneWayIsPointedAtTheRoomItReaches()
    {
        var walk = new MapWalk();
        walk.To("Cliff Top");
        walk.To("Ledge", "down");

        var shot = MapShot.Of(walk.Graph);
        var line = Assert.Single(shot.Lines);

        var top = shot.Boxes.Single(b => b.Name == "Cliff Top");
        var ledge = shot.Boxes.Single(b => b.Name == "Ledge");

        // Whichever end of the line the reached room turned out to be.
        var pointed = line.ArrowAtOne ? Nearest(line.X1, line.Y1, shot) : Nearest(line.X2, line.Y2, shot);

        Assert.True(line.ArrowAtOne ^ line.ArrowAtTwo, "exactly one end should be arrowed");
        Assert.Equal(ledge.Room, pointed);
        Assert.NotEqual(top.Room, pointed);
    }

    [Fact]
    public void AWayUpIsDashedAndSaysSoAtBothEnds()
    {
        // Up and down are put north and south for want of anywhere
        // else, so the line's own direction is a lie unless it is
        // labeled. This is the case the grid map could only draw as a
        // dotted line with a legend under it.
        var walk = new MapWalk();
        walk.To("Cellar");
        walk.To("Kitchen", "up");
        walk.To("Cellar", "down");

        var line = Assert.Single(MapShot.Of(walk.Graph).Lines);

        Assert.True(line.Dashed);
        Assert.Equal(["d", "u"], new[] { line.LabelOne, line.LabelTwo }.Order().ToArray());
    }

    [Fact]
    public void AWayInIsDrawnAlthoughItPointsNowhere()
    {
        // In and out get no cell of their own at all, so the two rooms
        // are put wherever there was space. A character grid had no way
        // to join them; a line between two points always has one.
        var walk = new MapWalk();
        walk.To("Courtyard");
        walk.To("Chapel", "in");

        var shot = MapShot.Of(walk.Graph);
        var line = Assert.Single(shot.Lines);

        Assert.True(line.Dashed);
        Assert.Equal("in", line.LabelOne ?? line.LabelTwo);
    }

    [Fact]
    public void AnOrdinaryPassageIsLeftUnlabeled()
    {
        var walk = new MapWalk();
        walk.To("Hall");
        walk.To("Study", "west");
        walk.To("Hall", "east");

        var line = Assert.Single(MapShot.Of(walk.Graph).Lines);

        Assert.False(line.Dashed);
        Assert.Null(line.LabelOne);
        Assert.Null(line.LabelTwo);
    }

    [Fact]
    public void TwoDirectionsThatAreNotOppositesAreBothWrittenDown()
    {
        // These games are full of this: north out of one room and west
        // back out of the next. Joined into one line and left plain it
        // would read as a passage that is not there, so each end says
        // what was actually walked.
        var walk = new MapWalk();
        walk.To("Mill");
        walk.To("Track", "north");
        walk.To("Mill", "west");

        var line = Assert.Single(MapShot.Of(walk.Graph).Lines);

        Assert.Equal(["n", "w"], new[] { line.LabelOne, line.LabelTwo }.Order().ToArray());
    }

    [Fact]
    public void TheExtentHoldsEveryBoxWithRoomToSpare()
    {
        var walk = new MapWalk();
        walk.To("One");
        walk.To("Two", "north");
        walk.To("Three", "east");
        walk.To("Four", "south");

        var shot = MapShot.Of(walk.Graph);
        var (left, top, width, height) = shot.Extent;

        foreach (var box in shot.Boxes)
        {
            Assert.True(box.Left > left, $"{box.Name} sits off the left edge");
            Assert.True(box.Top > top, $"{box.Name} sits off the top edge");
            Assert.True(box.Left + MapShot.BoxWidth < left + width, $"{box.Name} runs past the right");
            Assert.True(box.Top + MapShot.BoxHeight < top + height, $"{box.Name} runs past the bottom");
        }
    }

    [Fact]
    public void AMapOfOneRoomIsNotBlownUpToFillThePane()
    {
        // Fitted to itself, a single room would be scaled until it
        // filled the pane, which looks like a fault rather than like a
        // map of one room.
        var walk = new MapWalk();
        walk.To("West of House");

        var fit = MapShot.Of(walk.Graph).Fitted(600, 800);

        Assert.True(fit.Scale < 1.5, $"one room was scaled by {fit.Scale}");
    }

    [Fact]
    public void AMapWithNoPaneToDrawItInIsLeftAlone()
    {
        // A pane can be asked to fit itself before it has ever been
        // laid out, and dividing by nothing would put every room at
        // infinity.
        var walk = new MapWalk();
        walk.To("Hall");
        walk.To("Study", "west");

        var fit = MapShot.Of(walk.Graph).Fitted(0, 0);

        Assert.Equal(1, fit.Scale);
    }

    [Fact]
    public void TheWholeMapFitsInsideThePaneItWasFittedTo()
    {
        var walk = new MapWalk();
        walk.To("One");
        walk.To("Two", "north");
        walk.To("Three", "east");
        walk.To("Four", "southeast");
        walk.To("Five", "south");

        var shot = MapShot.Of(walk.Graph);
        var fit = shot.Fitted(420, 560);

        foreach (var box in shot.Boxes)
        {
            var (left, top) = fit.Scaled(box.Left, box.Top);
            var (right, bottom) = fit.Scaled(box.Left + MapShot.BoxWidth, box.Top + MapShot.BoxHeight);

            Assert.InRange(left, 0, 420);
            Assert.InRange(right, 0, 420);
            Assert.InRange(top, 0, 560);
            Assert.InRange(bottom, 0, 560);
        }
    }

    [Fact]
    public void FollowingThePlayerPutsTheirOwnRoomInTheMiddle()
    {
        var walk = new MapWalk();
        walk.To("Start");
        walk.To("Far", "north");
        walk.To("Farther", "north");

        var shot = MapShot.Of(walk.Graph);
        var fit = shot.Centered(1, 400, 300);

        var here = shot.Boxes.Single(b => b.Current);
        var (x, y) = fit.Scaled(here.Center.X, here.Center.Y);

        Assert.Equal(200, x, 6);
        Assert.Equal(150, y, 6);
    }

    [Fact]
    public void ASmallMapIsFollowedByShowingAllOfIt()
    {
        var walk = new MapWalk();
        walk.To("Hall");
        walk.To("Study", "west");

        var shot = MapShot.Of(walk.Graph);

        Assert.Equal(shot.Fitted(700, 900), shot.Following(700, 900, 0.75));
    }

    [Fact]
    public void AMapTooBigToReadIsFollowedRoundThePlayerInstead()
    {
        // Thirty-odd rooms in a pane beside a game: fitting all of it
        // would put the names below the size anyone can read, so the
        // map gives up on showing everything and shows where the
        // player is instead.
        var walk = new MapWalk();
        walk.To("Room 0");

        for (var i = 1; i < 40; i++)
        {
            walk.To($"Room {i}", i % 2 == 0 ? "north" : "east");
        }

        var shot = MapShot.Of(walk.Graph);
        var following = shot.Following(420, 700, 0.75);

        Assert.True(shot.Fitted(420, 700).Scale < 0.75, "the map should not have fitted");
        Assert.Equal(0.75, following.Scale);

        var here = shot.Boxes.Single(b => b.Current);
        var (x, y) = following.Scaled(here.Center.X, here.Center.Y);

        Assert.Equal(210, x, 6);
        Assert.Equal(350, y, 6);
    }

    [Fact]
    public void TheRoomUnderAPointIsTheOneThatWasDrawnThere()
    {
        // The round trip that decides whether the name in the heading
        // is the name of the room the pointer is actually over.
        var walk = new MapWalk();
        walk.To("Clearing");
        walk.To("Forest", "east");
        walk.To("Glade", "north");

        var shot = MapShot.Of(walk.Graph);
        var fit = shot.Fitted(500, 400);

        foreach (var box in shot.Boxes)
        {
            var (x, y) = fit.Scaled(box.Center.X, box.Center.Y);

            Assert.Equal(box.Room, shot.At(fit, x, y));
        }
    }

    [Fact]
    public void ThePointerBetweenTwoRoomsIsOverNeither()
    {
        var walk = new MapWalk();
        walk.To("Clearing");
        walk.To("Forest", "east");

        var shot = MapShot.Of(walk.Graph);
        var fit = shot.Fitted(500, 400);

        // Halfway along the line joining them, which is the gap.
        var line = Assert.Single(shot.Lines);
        var (x, y) = fit.Scaled((line.X1 + line.X2) / 2, (line.Y1 + line.Y2) / 2);

        Assert.Null(shot.At(fit, x, y));
    }

    /// <summary>The room whose box an end of a line is touching.</summary>
    private static int Nearest(double x, double y, MapShot shot) =>
        shot.Boxes
            .OrderBy(b => Math.Abs(b.Center.X - x) + Math.Abs(b.Center.Y - y))
            .First()
            .Room;
}

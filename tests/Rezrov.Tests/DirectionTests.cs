using Rezrov.Mapping;

namespace Rezrov.Tests;

/// <summary>
/// [mapping] The words a player types for a direction, and the geometry
/// each one implies.
/// </summary>
public class DirectionTests
{
    [Theory]
    [InlineData("north", Direction.North)]
    [InlineData("n", Direction.North)]
    [InlineData("SOUTH", Direction.South)]
    [InlineData("e", Direction.East)]
    [InlineData("west", Direction.West)]
    [InlineData("ne", Direction.Northeast)]
    [InlineData("northwest", Direction.Northwest)]
    [InlineData("se", Direction.Southeast)]
    [InlineData("sw", Direction.Southwest)]
    [InlineData("u", Direction.Up)]
    [InlineData("down", Direction.Down)]
    [InlineData("in", Direction.In)]
    [InlineData("enter", Direction.In)]
    [InlineData("out", Direction.Out)]
    [InlineData("exit", Direction.Out)]
    public void ADirectionWordIsRead(string command, Direction expected)
    {
        Assert.True(Directions.TryParse(command, out var direction));
        Assert.Equal(expected, direction);
    }

    [Theory]
    [InlineData("go north", Direction.North)]
    [InlineData("walk n", Direction.North)]
    [InlineData("  run   SE  ", Direction.Southeast)]
    [InlineData("head up", Direction.Up)]
    public void AVerbOfMotionIsSteppedOver(string command, Direction expected)
    {
        Assert.True(Directions.TryParse(command, out var direction));
        Assert.Equal(expected, direction);
    }

    [Theory]
    [InlineData("fore", Direction.North)]
    [InlineData("aft", Direction.South)]
    [InlineData("port", Direction.West)]
    [InlineData("starboard", Direction.East)]
    public void AShipsDirectionsPointWhereItsBowDoes(string command, Direction expected)
    {
        Assert.True(Directions.TryParse(command, out var direction));
        Assert.Equal(expected, direction);
    }

    [Theory]
    [InlineData("take lamp")]
    [InlineData("open the trap door")]
    [InlineData("go")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("p")]
    [InlineData("northerly")]
    public void ACommandThatNamesNoDirectionIsRefused(string? command)
    {
        // Refusing leaves a gap in the map. Guessing would draw a passage
        // nobody walked, which is worse, and is the whole reason the
        // single letter a game might use for "port" is left out here.
        Assert.False(Directions.TryParse(command, out _));
    }

    [Theory]
    [InlineData("n. n. u")]
    [InlineData("north. south")]
    [InlineData("n, n")]
    [InlineData("north then east")]
    [InlineData("go north. go south")]
    public void ALineHoldingSeveralCommandsNamesNoDirection(string command)
    {
        // These parsers let a player walk three rooms on one turn, and
        // only the room they finished in is ever seen. Reading the first
        // word would draw a passage from where they started to somewhere
        // three rooms away.
        Assert.False(Directions.TryParse(command, out _));
    }

    [Fact]
    public void ATrailingStopIsStillOneCommand()
    {
        Assert.True(Directions.TryParse("n.", out var direction));
        Assert.Equal(Direction.North, direction);
    }

    [Fact]
    public void EveryDirectionUndoesItsOpposite()
    {
        foreach (var direction in Directions.All)
        {
            Assert.NotEqual(direction, Directions.Opposite(direction));
            Assert.Equal(direction, Directions.Opposite(Directions.Opposite(direction)));
        }
    }

    [Fact]
    public void AnOppositeOnTheCompassIsTheOffsetNegated()
    {
        foreach (var direction in Directions.All)
        {
            if (Directions.Offset(direction) is not { } offset)
            {
                continue;
            }

            var back = Directions.Offset(Directions.Opposite(direction))!.Value;
            Assert.Equal((-offset.X, -offset.Y), back);
        }
    }

    [Fact]
    public void OnlyTheCompassPointsAnywhereOnAFlatMap()
    {
        Assert.Equal((0, -1), Directions.Offset(Direction.North));
        Assert.Equal((1, 1), Directions.Offset(Direction.Southeast));

        // Y grows southward, as a screen's rows do.
        Assert.Equal((0, 1), Directions.Offset(Direction.South));

        Assert.Null(Directions.Offset(Direction.Up));
        Assert.Null(Directions.Offset(Direction.Down));
        Assert.Null(Directions.Offset(Direction.In));
        Assert.Null(Directions.Offset(Direction.Out));
    }

    [Fact]
    public void AStaircaseIsGivenACellEvenThoughItPointsNowhere()
    {
        // The one place the two disagree: a room up a staircase has to go
        // somewhere readable, so it is put north, while the map still
        // knows better than to call it a passage north.
        Assert.Equal((0, -1), Directions.Cell(Direction.Up));
        Assert.Equal((0, 1), Directions.Cell(Direction.Down));

        Assert.Null(Directions.Cell(Direction.In));
        Assert.Null(Directions.Cell(Direction.Out));

        foreach (var direction in Directions.All)
        {
            if (direction is not (Direction.Up or Direction.Down))
            {
                Assert.Equal(Directions.Offset(direction), Directions.Cell(direction));
            }
        }
    }

    [Fact]
    public void EveryDirectionHasItsOwnShortName()
    {
        var names = Directions.All.Select(Directions.Abbreviation).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("?", names);
    }
}

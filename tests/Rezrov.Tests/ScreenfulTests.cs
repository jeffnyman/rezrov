using Rezrov.Gui;

namespace Rezrov.Tests;

/// <summary>
/// How large a window a game opens in, counted in the characters the
/// screen is made of.
/// </summary>
/// <remarks>
/// The numbers here are the ones the graphical frontend really uses: a
/// character nine pixels wide and nineteen tall, and [blorb 11.2] the
/// screen of 320 by 200 that all four of Infocom's Version 6 games say
/// their pictures were drawn for.
/// </remarks>
public class ScreenfulTests
{
    private const double Cell = 9;
    private const double Line = 19;
    private const double Shape = 320.0 / 200.0;

    [Fact]
    public void AGameThatNamesNoShapeTakesWhateverFits()
    {
        Assert.Equal((120, 40), Screenful.Fit(1080, 760, Cell, Line, null));

        // Whole characters only, since a screen is made of them and a
        // part of one at the edge is a strip of nothing.
        Assert.Equal((120, 40), Screenful.Fit(1088, 775, Cell, Line, null));

        // And never none of them, however little room there is.
        Assert.Equal((1, 1), Screenful.Fit(0, 0, Cell, Line, null));
    }

    [Fact]
    public void AVersionSixGameGetsTheShapeItsArtworkWasDrawnFor()
    {
        // A page of 120 by 40 is 1080 by 760, which is wider than tall
        // in the wrong proportion: the scaling rules would fit the
        // artwork to the width and leave 85 pixels of background along
        // the bottom, which is Shogun's title screen stopping short.
        var (columns, rows) = Screenful.Fit(1080, 760, Cell, Line, Shape);

        Assert.Equal((118, 35), (columns, rows));

        // What is left over is under one character in either direction,
        // which is as close as a grid of characters comes.
        var width = columns * Cell;
        var height = rows * Line;
        var scale = Math.Min(width / 320, height / 200);

        Assert.True(width - (320 * scale) < Cell, $"{width - (320 * scale)} pixels of width are wasted.");
        Assert.True(height - (200 * scale) < Line, $"{height - (200 * scale)} pixels of height are wasted.");
    }

    [Fact]
    public void TheShapeNeverAsksForMoreRoomThanThereIs()
    {
        // Whichever side is too long is brought in, so the answer is
        // never larger than what it was given, whichever way the room
        // is out of proportion.
        foreach (var (width, height) in new[] { (1080.0, 760.0), (2000.0, 600.0), (600.0, 2000.0), (400.0, 300.0) })
        {
            var (columns, rows) = Screenful.Fit(width, height, Cell, Line, Shape);

            Assert.True(columns * Cell <= width, $"{columns} columns do not fit in {width}.");
            Assert.True(rows * Line <= height, $"{rows} rows do not fit in {height}.");
        }
    }

    [Fact]
    public void AWindowAlreadyOfTheRightShapeIsLeftAlone()
    {
        // 152 by 45 characters is 1368 by 855, which is exactly 320 to
        // 200, so there is nothing to bring in.
        Assert.Equal((152, 45), Screenful.Fit(1368, 855, Cell, Line, Shape));
    }

    [Fact]
    public void AScaledDisplayGivesTheWindowEveryColumnItWasSizedFor()
    {
        // The case that was found: 118 columns and a margin of six each
        // side is 1074 wide, which at 175% is 1879.5 of the display's
        // pixels. Cut to 1879 that held 117.97 columns, and the game was
        // told 117.
        var asked = Screenful.Whole(1074, 1.75);
        var given = Given(asked, 1.75);

        Assert.Equal(1880, Math.Floor(asked * 1.75));
        Assert.Equal(118, (int)((given - 12) / Cell));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    [InlineData(2.25)]
    [InlineData(2.5)]
    [InlineData(3.0)]
    public void AWindowComesBackNoSmallerThanAskedAndNoPixelLarger(double scaling)
    {
        // Every size a window might be asked for, in quarter pixels,
        // at each scaling a display offers: what the display gives is
        // never less than was wanted, and never a whole pixel more.
        for (var size = 600.0; size < 1400; size += 0.25)
        {
            var given = Given(Screenful.Whole(size, scaling), scaling);

            Assert.True(given >= size, $"{size} at {scaling} came back {given}.");
            Assert.True((given - size) * scaling < 1, $"{size} at {scaling} came back {given}.");
        }
    }

    [Fact]
    public void ADisplayThatIsNotScaledIsAskedForWhatWasWanted()
    {
        Assert.Equal(1074, Given(Screenful.Whole(1074, 1), 1));
    }

    /// <summary>
    /// What a display gives a window asked for at this size: whole
    /// pixels of its own, with any fraction cut off, as it was measured
    /// to do at 175%.
    /// </summary>
    private static double Given(double asked, double scaling) => Math.Floor(asked * scaling) / scaling;
}

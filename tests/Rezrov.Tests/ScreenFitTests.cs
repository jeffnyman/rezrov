using Rezrov.Gui;

namespace Rezrov.Tests;

/// <summary>
/// [infocom pictures] How a fixed Version 6 screen sits inside a window
/// of another shape, which painting and the pointer both depend on and
/// have to agree about.
/// </summary>
public class ScreenFitTests
{
    private static readonly (int Width, int Height) Unit = (640, 400);

    [Fact]
    public void AWindowOfTheSameShapeIsFilledExactly()
    {
        var fit = ScreenFit.Of((1280, 800), Unit);

        Assert.NotNull(fit);
        Assert.Equal(2, fit.Value.Scale);
        Assert.Equal((0, 0), (fit.Value.Across, fit.Value.Down));
    }

    [Fact]
    public void AWiderWindowLeavesMarginsDownTheSides()
    {
        // 1600 by 800 holds the screen twice over vertically and two
        // and a half times across, so the height is what limits it and
        // the slack goes half to each side.
        var fit = ScreenFit.Of((1600, 800), Unit)!.Value;

        Assert.Equal(2, fit.Scale);
        Assert.Equal(160, fit.Across);
        Assert.Equal(0, fit.Down);
    }

    [Fact]
    public void ATallerWindowLeavesMarginsAboveAndBelow()
    {
        var fit = ScreenFit.Of((1280, 1000), Unit)!.Value;

        Assert.Equal(2, fit.Scale);
        Assert.Equal(0, fit.Across);
        Assert.Equal(100, fit.Down);
    }

    [Fact]
    public void APointComesBackToTheCellItLandedOn()
    {
        var fit = ScreenFit.Of((1600, 800), Unit)!.Value;

        // The screen starts 160 pixels in and each of its units is two
        // pixels, so its own origin is at 160 and its far corner at
        // 160 + 1280.
        Assert.Equal((0, 0), fit.Unscaled(160, 0));
        Assert.Equal((640, 400), fit.Unscaled(160 + 1280, 800));

        // And a click in the middle of the eleventh column of an eight
        // unit cell is in column ten, counting from zero.
        var (x, _) = fit.Unscaled(160 + (10 * 8 * 2) + 4, 0);
        Assert.Equal(10, (int)(x / 8));
    }

    [Fact]
    public void PaintingAndThePointerAgree()
    {
        var fit = ScreenFit.Of((1097, 763), Unit)!.Value;

        // Whatever the window, a point put on the screen by the paint
        // transform comes back to where it started. This is the whole
        // reason the arithmetic lives in one place: a click that lands
        // near enough to look right and is wrong would go unnoticed.
        foreach (var (x, y) in new[] { (0.0, 0.0), (319.0, 199.0), (640.0, 400.0) })
        {
            var (back, down) = fit.Unscaled((x * fit.Scale) + fit.Across, (y * fit.Scale) + fit.Down);

            Assert.Equal(x, back, 6);
            Assert.Equal(y, down, 6);
        }
    }

    [Fact]
    public void AnEmptyWindowOrScreenHasNoFit()
    {
        Assert.Null(ScreenFit.Of((0, 800), Unit));
        Assert.Null(ScreenFit.Of((1280, 0), Unit));
        Assert.Null(ScreenFit.Of((1280, 800), (0, 400)));
        Assert.Null(ScreenFit.Of((1280, 800), (640, 0)));
    }
}

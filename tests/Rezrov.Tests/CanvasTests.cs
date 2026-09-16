using Rezrov.Core.Graphics;

namespace Rezrov.Tests;

/// <summary>
/// [glk #window_graphics] The canvas a graphics window is: filling it,
/// drawing pictures on it at whatever size, letting what is underneath
/// show through where a picture is transparent, and resizing it.
/// </summary>
public class CanvasTests
{
    private const uint Red = 0x00FF0000;
    private const uint Green = 0x0000FF00;
    private const uint Blue = 0x000000FF;
    private const uint Black = 0x00000000;

    [Fact]
    public void ACanvasStartsAsItsBackgroundColorAllOver()
    {
        // [glk #graphics_graphics] White until the game says otherwise.
        var plain = new Canvas(2, 2);
        Assert.Equal((255, 255, 255, 255), plain.At(0, 0));
        Assert.Equal((255, 255, 255, 255), plain.At(1, 1));

        var colored = new Canvas(2, 2, Red);
        Assert.Equal((255, 0, 0, 255), colored.At(1, 1));
        Assert.Equal((2, 2), (colored.Width, colored.Height));
    }

    [Fact]
    public void AFilledRectangleCoversItselfAndNothingElse()
    {
        var canvas = new Canvas(4, 4);
        canvas.Fill(1, 1, 2, 2, Red);

        Assert.Equal((255, 255, 255, 255), canvas.At(0, 0));
        Assert.Equal((255, 0, 0, 255), canvas.At(1, 1));
        Assert.Equal((255, 0, 0, 255), canvas.At(2, 2));
        Assert.Equal((255, 255, 255, 255), canvas.At(3, 3));

        // [glk op:window_fill_rect] A rectangle of no width or height
        // draws nothing at all.
        canvas.Fill(0, 0, 0, 4, Blue);
        canvas.Fill(0, 0, 4, 0, Blue);
        Assert.Equal((255, 255, 255, 255), canvas.At(0, 0));
    }

    [Fact]
    public void ARectangleOverAnEdgeIsCutOffAtIt()
    {
        // [glk op:window_fill_rect] Part of a rectangle may lie outside
        // the canvas, and the rest of it is still painted.
        var canvas = new Canvas(4, 4);
        canvas.Fill(-2, -2, 3, 3, Green);
        canvas.Fill(3, 3, 10, 10, Blue);

        Assert.Equal((0, 255, 0, 255), canvas.At(0, 0));
        Assert.Equal((255, 255, 255, 255), canvas.At(1, 1));
        Assert.Equal((0, 0, 255, 255), canvas.At(3, 3));

        // And one wholly outside touches nothing.
        canvas.Fill(20, 20, 5, 5, Black);
        canvas.Fill(-20, 0, 5, 5, Black);
        Assert.Equal((0, 255, 0, 255), canvas.At(0, 0));
        Assert.Equal((255, 255, 255, 255), canvas.At(1, 1));
    }

    [Fact]
    public void AResizeKeepsWhatFitsAndFillsTheRestWithTheBackground()
    {
        // [glk #window_graphics] The bottom or right is thrown away when
        // a window shrinks, and what appears when it grows is the
        // background color.
        var canvas = new Canvas(4, 4, Red) { Background = Blue };
        canvas.Fill(0, 0, 2, 2, Green);

        canvas.Resize(6, 6);
        Assert.Equal((0, 255, 0, 255), canvas.At(0, 0));
        Assert.Equal((255, 0, 0, 255), canvas.At(3, 3));
        Assert.Equal((0, 0, 255, 255), canvas.At(5, 5));

        canvas.Resize(1, 1);
        Assert.Equal((1, 1), (canvas.Width, canvas.Height));
        Assert.Equal((0, 255, 0, 255), canvas.At(0, 0));
    }

    [Fact]
    public void ClearingPaintsTheWholeCanvasTheBackgroundOfTheMoment()
    {
        // [glk op:window_set_background_color] Changing the color does
        // not change what is already there; the next clear does.
        var canvas = new Canvas(2, 2, Red);
        canvas.Background = Green;
        Assert.Equal((255, 0, 0, 255), canvas.At(0, 0));

        canvas.Clear();
        Assert.Equal((0, 255, 0, 255), canvas.At(0, 0));
        Assert.Equal((0, 255, 0, 255), canvas.At(1, 1));
    }

    [Fact]
    public void TheCanvasCountsWhatHasBeenDoneToIt()
    {
        // A frontend keeps a bitmap of the canvas and would rather not
        // fill it again on every repaint, so the canvas says when there
        // is something new to copy.
        var canvas = new Canvas(4, 4);
        var start = canvas.Changes;

        canvas.Fill(0, 0, 2, 2, Red);
        Assert.True(canvas.Changes > start);

        var filled = canvas.Changes;
        canvas.Draw(Solid(2, 2, 0, 255, 0), 0, 0, 2, 2);
        Assert.True(canvas.Changes > filled);

        var drawn = canvas.Changes;
        canvas.Clear();
        canvas.Resize(8, 8);
        Assert.True(canvas.Changes > drawn);

        // Asking about it changes nothing, and neither does a rectangle
        // that draws nothing.
        var quiet = canvas.Changes;
        _ = canvas.At(0, 0);
        canvas.Fill(0, 0, 0, 0, Blue);
        Assert.Equal(quiet, canvas.Changes);
    }

    [Fact]
    public void APictureIsDrawnWhereItIsPutAndNowhereElse()
    {
        var canvas = new Canvas(4, 4);
        canvas.Draw(Solid(2, 2, 255, 0, 0), 1, 1, 2, 2);

        Assert.Equal((255, 255, 255, 255), canvas.At(0, 0));
        Assert.Equal((255, 0, 0, 255), canvas.At(1, 1));
        Assert.Equal((255, 0, 0, 255), canvas.At(2, 2));
        Assert.Equal((255, 255, 255, 255), canvas.At(3, 3));

        // [glk #graphics_graphics] A picture may hang over any edge,
        // including the left and top, and what lands is still drawn.
        canvas.Draw(Solid(2, 2, 0, 0, 255), -1, -1, 2, 2);
        Assert.Equal((0, 0, 255, 255), canvas.At(0, 0));
    }

    [Fact]
    public void APictureMadeLargerTakesTheNearestPixelAndMadeSmallerTheAverage()
    {
        // Two pixels, black then white. Drawn at twice the width, each
        // stands for two, with no gray invented between them.
        var pair = new Pixels(2, 1, [0, 0, 0, 255, 255, 255, 255, 255]);
        var wide = new Canvas(4, 1);
        wide.Draw(pair, 0, 0, 4, 1);

        Assert.Equal((0, 0, 0, 255), wide.At(0, 0));
        Assert.Equal((0, 0, 0, 255), wide.At(1, 0));
        Assert.Equal((255, 255, 255, 255), wide.At(2, 0));
        Assert.Equal((255, 255, 255, 255), wide.At(3, 0));

        // Drawn at half the width, the one pixel stands for both, so it
        // is what the two average to rather than whichever came first.
        var narrow = new Canvas(1, 1);
        narrow.Draw(pair, 0, 0, 1, 1);
        Assert.Equal((128, 128, 128, 255), narrow.At(0, 0));
    }

    [Fact]
    public void WhatIsSeeThroughLetsWhatIsUnderneathShow()
    {
        // [glk #graphics_testing] Alpha is honored, which is what
        // gestalt_GraphicsTransparency answering one promises.
        var canvas = new Canvas(2, 1, Black);
        canvas.Draw(Solid(2, 1, 255, 255, 255, 128), 0, 0, 2, 1);

        var (red, green, blue, alpha) = canvas.At(0, 0);
        Assert.Equal((byte)255, alpha);
        Assert.InRange(red, 127, 129);
        Assert.Equal(red, green);
        Assert.Equal(red, blue);

        // A wholly transparent picture changes nothing.
        canvas.Draw(Solid(2, 1, 255, 0, 0, 0), 0, 0, 2, 1);
        Assert.Equal((red, green, blue, (byte)255), canvas.At(0, 0));
    }

    [Fact]
    public void AnEmptyDrawingChangesNothing()
    {
        var canvas = new Canvas(2, 2, Red);

        canvas.Draw(Solid(2, 2, 0, 255, 0), 0, 0, 0, 2);
        canvas.Draw(Solid(2, 2, 0, 255, 0), 0, 0, 2, 0);
        canvas.Draw(new Pixels(0, 0, []), 0, 0, 2, 2);

        Assert.Equal((255, 0, 0, 255), canvas.At(0, 0));
        Assert.Equal((255, 0, 0, 255), canvas.At(1, 1));
    }

    private static Pixels Solid(int width, int height, byte red, byte green, byte blue, byte alpha = 255)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            rgba[(i * 4) + 0] = red;
            rgba[(i * 4) + 1] = green;
            rgba[(i * 4) + 2] = blue;
            rgba[(i * 4) + 3] = alpha;
        }

        return new Pixels(width, height, rgba);
    }
}

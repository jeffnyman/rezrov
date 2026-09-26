using Rezrov.Core.Blorb;
using Rezrov.Core.Graphics;
using Rezrov.Gtui;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

/// <summary>
/// The grid frontend's artwork: a picture into pixels, the screen it is
/// measured against, and the order it is drawn in.
/// </summary>
/// <remarks>
/// [infocom pictures] The four Version 6 games Infocom drew art for
/// computed every coordinate for a 640 by 400 screen. This program's
/// character is eight pixels by sixteen, which makes that screen
/// exactly the eighty columns by twenty-five rows those games expect,
/// with one unit to one pixel and nothing rounded on either side. That
/// arithmetic is why the artwork lands where the game put it, so it is
/// pinned here.
/// </remarks>
public class GridPictureTests
{
    [Fact]
    public void TheUnitScreenIsExactlyEightyColumnsByTwentyFive()
    {
        var (width, height) = InfocomPictures.UnitScreen;

        Assert.Equal((80, 25), Paint.Fits(width, height));
        Assert.Equal(0, width % Paint.CellWidth);
        Assert.Equal(0, height % Paint.CellHeight);
    }

    [Fact]
    public void MagnifyingMakesEveryPixelASquareOfPixels()
    {
        var small = new Surface(2, 2);
        small.Set(0, 0, 0x00FF0000);
        small.Set(1, 0, 0x0000FF00);
        small.Set(0, 1, 0x000000FF);
        small.Set(1, 1, 0x00FFFFFF);

        var big = new Surface(8, 8);
        big.Fill(Surface.Black);
        big.Magnify(small, 3, 1, 1);

        // The first pixel becomes a three by three square, set down one
        // and across one.
        Assert.Equal(Surface.Black, big[0, 0]);
        Assert.Equal(0x00FF0000u, big[1, 1]);
        Assert.Equal(0x00FF0000u, big[3, 3]);
        Assert.Equal(0x0000FF00u, big[4, 1]);
        Assert.Equal(0x000000FFu, big[1, 4]);
        Assert.Equal(0x00FFFFFFu, big[6, 6]);

        // And nothing is drawn past the end of it.
        Assert.Equal(Surface.Black, big[7, 7]);
    }

    [Fact]
    public void MagnifyingByNothingDrawsNothing()
    {
        var small = new Surface(2, 2);
        small.Fill(Surface.White);

        var big = new Surface(8, 8);
        big.Fill(Surface.Black);
        big.Magnify(small, 0, 0, 0);

        Assert.Equal(Surface.Black, big[0, 0]);
    }

    [Fact]
    public void APictureIsDrawnAtTheSizeItWasAskedFor()
    {
        // [blorb 2.3] These games ask for their artwork at twice its
        // own size, since 320 by 200 pixel art fills a 640 by 400
        // screen, and the nearest pixel is what keeps it pixel art.
        var page = new Surface(16, 16);
        page.Fill(Surface.Black);

        Paint.Artwork(page, Quarters(), 4, 4, 8, 8);

        // Each of the four pixels becomes a four by four square.
        Assert.Equal(0x00FF0000u, page[4, 4]);
        Assert.Equal(0x00FF0000u, page[7, 7]);
        Assert.Equal(0x0000FF00u, page[8, 4]);
        Assert.Equal(0x000000FFu, page[4, 8]);
        Assert.Equal(0x00FFFFFFu, page[11, 11]);

        Assert.Equal(Surface.Black, page[3, 4]);
        Assert.Equal(Surface.Black, page[12, 4]);
    }

    [Fact]
    public void APictureIsClippedRatherThanRefused()
    {
        // A game is free to draw half off the screen, and a frontend
        // that fell over when it did would be the frontend's fault.
        var page = new Surface(8, 8);
        page.Fill(Surface.Black);

        Paint.Artwork(page, Quarters(), 6, 6, 4, 4);

        // Only the first quarter of the picture fits, and the three
        // that do not are simply not drawn.
        Assert.Equal(0x00FF0000u, page[6, 6]);
        Assert.Equal(0x00FF0000u, page[7, 7]);
    }

    [Fact]
    public void ATransparentPixelLeavesWhatIsUnderIt()
    {
        var page = new Surface(4, 4);
        page.Fill(Surface.White);

        // One opaque red pixel and one wholly transparent one.
        Paint.Artwork(page, new Pixels(2, 1, [0xFF, 0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0x00]), 0, 0, 2, 1);

        Assert.Equal(0x00FF0000u, page[0, 0]);
        Assert.Equal(Surface.White, page[1, 0]);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    [InlineData(-2, 4)]
    public void APictureAskedForAtNoSizeDrawsNothing(int width, int height)
    {
        // A game is free to ask, and dividing by it would be worse.
        var page = new Surface(4, 4);
        page.Fill(Surface.Black);

        Paint.Artwork(page, Quarters(), 0, 0, width, height);

        Assert.Equal(Surface.Black, page[0, 0]);
    }

    [Fact]
    public void APictureCoversABackgroundAndTheTextCoversThePicture()
    {
        // [zm 8.8.6] The order a Version 6 game draws in: it paints a
        // picture over whatever was there and then writes on top of
        // it. Both halves of that are worth pinning, because getting
        // either wrong leaves a game looking almost right.
        var screen = Screen(4, 2);
        screen.Print("M", new TextAttributes(TextStyle.Roman, ScreenColor.White, ScreenColor.Blue, TextAttributes.NormalFont));

        var page = new Surface(4 * Paint.CellWidth, 2 * Paint.CellHeight);
        var blue = Paint.Pixel(ScreenColor.Blue, Surface.Black);

        // A picture over the first two cells and nowhere else.
        Paint.Screen(
            page,
            screen,
            behind: at => at.Fill(0, 0, Paint.CellWidth * 2, Paint.CellHeight, 0x00FF0000));

        var red = 0;
        var left = 0;
        var white = 0;

        for (var y = 0; y < Paint.CellHeight; y++)
        {
            for (var x = 0; x < Paint.CellWidth; x++)
            {
                red += page[x, y] == 0x00FF0000 ? 1 : 0;
                left += page[x, y] == blue ? 1 : 0;
                white += page[x, y] == Surface.White ? 1 : 0;
            }
        }

        Assert.True(red > 0, "the picture did not cover the cell's background");
        Assert.Equal(0, left);
        Assert.True(white > 0, "the letter was not drawn over the picture");

        // And the cell beside it, which the game never wrote to and
        // which the picture reaches, shows the picture through. This
        // is the one that breaks if every cell's background is filled
        // whether or not the game asked for one.
        Assert.Equal(0x00FF0000u, page[Paint.CellWidth + 2, 4]);
    }

    [Fact]
    public void AGameWithNoArtworkIsAskedForNone()
    {
        var art = new GridPictures(null);

        Assert.Equal(0, art.Count);
        Assert.False(art.Has(1));
        Assert.Null(art.Screen);

        // Asked twice, and the second time the answer is the one that
        // was kept rather than a fresh failure.
        Assert.Null(art.Decode(1));
        Assert.Null(art.Decode(1));
    }

    [Fact]
    public void APlacementWithNoRoomIsNotDrawn()
    {
        var page = new Surface(16, 16);
        page.Fill(Surface.Black);

        Paint.Pictures(
            page,
            [new PicturePlacement(1, 0, 0, 1, 1, 0, 0, 0, 8), new PicturePlacement(2, 0, 0, 1, 1, 0, 0, 8, 0)],
            new GridPictures(null));

        Assert.Equal(Surface.Black, page[0, 0]);
    }

    [Fact]
    public void RealArtworkDecodesOnceAndIsKept()
    {
        // The catalog a test can build by hand has picture headers and
        // no pixels, so the only way to try real decoding, and the
        // caching that goes with it, is on a file Infocom shipped.
        var root = Corpus.FindRepositoryRoot();
        Assert.SkipWhen(root is null, "The entharion submodule is not populated.");

        var file = Path.Combine(root, "entharion", "infocom-graphics", "mcga", "zorkzero.mg1");
        Assert.SkipUnless(File.Exists(file), "The entharion submodule is not populated.");

        var art = new GridPictures(BlorbPictures.From(InfocomPictures.Read(File.ReadAllBytes(file))));

        Assert.True(art.Count > 0);
        Assert.Equal(InfocomPictures.UnitScreen, art.Screen);

        var once = art.Decode(5);
        Assert.NotNull(once);
        Assert.True(once.Width > 0 && once.Height > 0);

        // Decoding is far too slow to do again on every repaint, so
        // the same picture comes back rather than a new one.
        Assert.Same(once, art.Decode(5));

        // And a number the file does not have is null, kept as null,
        // rather than decoded again each time it is asked for.
        Assert.Null(art.Decode(9999));
        Assert.Null(art.Decode(9999));
    }

    /// <summary>Two by two: red, green, blue, and white.</summary>
    private static Pixels Quarters() => new(
        2,
        2,
        [
            0xFF, 0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF,
            0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        ]);

    private static BufferedScreen Screen(int columns, int rows) =>
        new(
            columns,
            rows,
            cursorStartsAtBottom: false,
            repaint: () => { },
            waitForKey: () => Zscii.Newline,
            fontWidth: Paint.CellWidth,
            fontHeight: Paint.CellHeight,
            capabilities: ScreenCapabilities.StatusLine | ScreenCapabilities.UpperWindow
                | ScreenCapabilities.Colors | ScreenCapabilities.FixedGrid
                | ScreenCapabilities.Pictures);

    private static TextAttributes Plain =>
        new(TextStyle.Roman, ScreenColor.White, ScreenColor.Black, TextAttributes.NormalFont);
}

using Rezrov.Core.Blorb;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Screen;

namespace Rezrov.Tests;

/// <summary>
/// [zm 8.8.6] Where a Version 6 game's pictures are and what takes
/// them away again: erasing a window, and scrolling one.
/// </summary>
/// <remarks>
/// A picture is drawn where the game put it and stays there until
/// something clears the cells it is on. The model keeps a list of where
/// they went rather than a canvas of pixels, so what would be a partly
/// erased picture on real hardware is here a picture kept or dropped
/// whole.
/// </remarks>
public class PicturePlacementTests
{
    [Fact]
    public void ErasingAWindowTakesOnlyThePicturesItCovers()
    {
        // [zm op:erase_window] Arthur draws the two sides of its frame
        // as pictures one character wide down the left and right edges
        // of the screen, and erases a text box in the middle on the way
        // into a room. The box shares rows with the borders without
        // coming near them, and they must survive it.
        var model = Model();

        Assert.True(model.DrawPicture(1, 6, 1));
        Assert.True(model.DrawPicture(2, 6, 40));
        Assert.True(model.DrawPicture(3, 7, 12));

        Assert.Equal([1, 2, 3], model.Pictures.Select(p => p.Number));

        // A window five rows tall and ten columns wide, in the middle,
        // covering the third picture and neither border.
        Assert.True(model.MoveWindow(3, 6, 11));
        Assert.True(model.WindowSize(3, 5, 10));
        Assert.True(model.EraseWindow(3));

        Assert.Equal([1, 2], model.Pictures.Select(p => p.Number));
    }

    [Fact]
    public void ErasingAWindowLeavesAPictureItOnlyPartlyCovers()
    {
        // Arthur's frame is one picture across the whole top of the
        // screen, and the game erases a box in the middle of it to make
        // the hole the room's illustration goes in. Taking the frame
        // away because part of it was erased leaves the screen bare,
        // and the illustration drawn next covers the hole anyway.
        var model = Model();

        Assert.True(model.DrawPicture(4, 5, 5));

        Assert.True(model.MoveWindow(3, 6, 11));
        Assert.True(model.WindowSize(3, 5, 10));
        Assert.True(model.EraseWindow(3));

        Assert.Equal([4], model.Pictures.Select(p => p.Number));
    }

    [Fact]
    public void ErasingAWindowStillTakesAPictureUnderIt()
    {
        // The other half of the same rule: what the erase does cover is
        // gone, which is what stops a picture hanging over cells the
        // game has just cleared to write in.
        var model = Model();

        Assert.True(model.DrawPicture(3, 7, 12));
        Assert.True(model.MoveWindow(3, 6, 11));
        Assert.True(model.WindowSize(3, 5, 10));
        Assert.True(model.EraseWindow(3));

        Assert.Empty(model.Pictures);
    }

    [Fact]
    public void ScrollingAWindowMovesOnlyThePicturesInIt()
    {
        // [zm 8.8.3] A picture in a scrolling window travels with the
        // text it belongs to, and one beside that window does not move
        // at all, for the same reason the erase leaves it alone.
        var model = Model();

        Assert.True(model.DrawPicture(1, 6, 1));
        Assert.True(model.DrawPicture(3, 7, 12));

        Assert.True(model.MoveWindow(3, 6, 11));
        Assert.True(model.WindowSize(3, 5, 10));
        model.SetWindow(3);
        model.ScrollWindow(3, 2);

        var border = model.Pictures.Single(p => p.Number == 1);
        var inside = model.Pictures.Single(p => p.Number == 3);

        Assert.Equal(5, border.Row);
        Assert.Equal(4, inside.Row);
    }

    /// <summary>
    /// A screen of forty by twenty cells, one unit to a cell, with four
    /// pictures: two borders one cell wide and ten tall, one small
    /// enough to sit inside a text box, and one wide enough to have a
    /// text box erased in the middle of it.
    /// </summary>
    private static WindowedScreenModel Model()
    {
        var bytes = new byte[2048];
        bytes[0] = 6;
        bytes[0x04] = 0x04;
        bytes[0x0E] = 0x04;
        var memory = new ZMemory(bytes);

        var screen = new RecordingScreen(
            40,
            20,
            ScreenCapabilities.StatusLine | ScreenCapabilities.UpperWindow | ScreenCapabilities.Pictures);

        var model = new WindowedScreenModel(screen, new StoryHeader(memory), memory);
        model.UsePictures(BlorbPictures.From(BlorbFile.Read(TestBlorb.Build(
        [
            (1, "PNG ", TestPng.Solid(1, 10, 0, 0, 0xFF)),
            (2, "PNG ", TestPng.Solid(1, 10, 0, 0, 0xFF)),
            (3, "PNG ", TestPng.Solid(4, 3, 0xFF, 0, 0)),
            (4, "PNG ", TestPng.Solid(30, 8, 0, 0xFF, 0)),
        ]))));

        return model;
    }
}

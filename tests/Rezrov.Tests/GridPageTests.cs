using Rezrov.Gtui;

namespace Rezrov.Tests;

/// <summary>
/// The rectangle a game is drawn on when the window's pixels are not
/// the pixels being drawn.
/// </summary>
/// <remarks>
/// Two things want this and get the same answer: a Version 6 game laid
/// out for one fixed screen, and a display the operating system scales,
/// where drawing small and magnifying by a whole number keeps a font
/// drawn a bit at a time from being smeared across pixels it was never
/// drawn for.
/// </remarks>
public class GridPageTests
{
    [Fact]
    public void AnOrdinaryGameOnAnOrdinaryDisplayIsDrawnStraightIntoTheWindow()
    {
        var page = new Page(1);

        Assert.True(page.Direct);
        Assert.True(page.Follows);

        var window = new Surface(64, 32);
        Surface? given = null;

        page.Show(window, drawn => given = drawn);

        // The very same surface, so nothing is copied and nothing is
        // magnified for a display that needs neither.
        Assert.Same(window, given);
    }

    [Fact]
    public void AScaledDisplayIsDrawnSmallAndMagnified()
    {
        var page = new Page(2);

        Assert.False(page.Direct);
        Assert.True(page.Follows);

        var window = new Surface(64, 32);
        Surface? given = null;

        page.Show(window, drawn =>
        {
            given = drawn;
            drawn.Fill(Surface.Black);
            drawn.Set(0, 0, 0x00FF0000);
        });

        // Drawn at half the size, and each of its pixels became two by
        // two in the window.
        Assert.NotNull(given);
        Assert.Equal((32, 16), (given.Width, given.Height));

        Assert.Equal(0x00FF0000u, window[0, 0]);
        Assert.Equal(0x00FF0000u, window[1, 1]);
        Assert.Equal(Surface.Black, window[2, 0]);
        Assert.Equal(Surface.Black, window[0, 2]);
    }

    [Fact]
    public void AFixedScreenIsDrawnAtItsOwnSizeWhateverTheWindow()
    {
        var page = new Page(1, (16, 8));

        Assert.False(page.Direct);
        Assert.False(page.Follows);

        var window = new Surface(64, 32);
        Surface? given = null;

        page.Show(window, drawn =>
        {
            given = drawn;
            drawn.Fill(Surface.White);
        });

        Assert.NotNull(given);
        Assert.Equal((16, 8), (given.Width, given.Height));

        // Four fits across and four down, so the whole window is the
        // screen with nothing left over.
        Assert.Equal(Surface.White, window[0, 0]);
        Assert.Equal(Surface.White, window[63, 31]);
    }

    [Fact]
    public void WhatAFixedScreenDoesNotCoverIsLeftDark()
    {
        // Three fits across and eight down, so the smaller of the two
        // wins and there is a band above and below.
        var page = new Page(1, (16, 8));
        var window = new Surface(48, 64);

        // Left as the last frame had it, so that a surround which is
        // not cleared shows up rather than happening to be black.
        window.Fill(0x00FF0000);

        page.Show(window, drawn => drawn.Fill(Surface.White));

        Assert.Equal(Surface.White, window[0, 20]);
        Assert.Equal(Surface.Black, window[0, 0]);
        Assert.Equal(Surface.Black, window[0, 63]);
    }

    [Fact]
    public void AFixedScreenThatBarelyFitsIsPutInTheMiddleOfTheWindow()
    {
        // A window only a little larger than the screen magnifies it
        // once, which is the case that copies a row at a time rather
        // than a rectangle at a time, and it still has to land where
        // the middle is.
        var page = new Page(1, (16, 8));
        var window = new Surface(20, 10);

        window.Fill(0x00FF0000);

        page.Show(window, drawn => drawn.Fill(Surface.White));

        // Two pixels of surround to the left and one above.
        Assert.Equal(Surface.Black, window[0, 0]);
        Assert.Equal(Surface.Black, window[1, 1]);
        Assert.Equal(Surface.White, window[2, 1]);
        Assert.Equal(Surface.White, window[17, 8]);
        Assert.Equal(Surface.Black, window[18, 8]);
    }

    [Fact]
    public void TheSamePageIsUsedAgainRatherThanMadeEachTime()
    {
        var page = new Page(2);
        var window = new Surface(64, 32);

        Surface? once = null;
        Surface? twice = null;

        page.Show(window, drawn => once = drawn);
        page.Show(window, drawn => twice = drawn);

        Assert.Same(once, twice);
    }

    [Fact]
    public void AWindowThatChangesSizeGetsAPageToMatch()
    {
        var page = new Page(2);
        Surface? given = null;

        page.Show(new Surface(64, 32), drawn => given = drawn);
        Assert.Equal((32, 16), (given!.Width, given.Height));

        page.Show(new Surface(128, 64), drawn => given = drawn);
        Assert.Equal((64, 32), (given!.Width, given.Height));
    }

    [Theory]
    [InlineData(1, 80, 30)]
    [InlineData(2, 40, 15)]
    [InlineData(3, 26, 10)]
    public void TheGridIsWhatFitsOnceTheMagnifyingIsCountedOut(int magnification, int columns, int rows)
    {
        var page = new Page(magnification);
        var window = new Surface(80 * Paint.CellWidth, 30 * Paint.CellHeight);

        Assert.Equal((columns, rows), page.Fits(window));
    }

    [Fact]
    public void AFixedScreenHoldsTheSameGridWhateverTheWindow()
    {
        var page = new Page(2, Rezrov.Core.Graphics.InfocomPictures.UnitScreen);

        Assert.Equal((80, 25), page.Fits(new Surface(200, 100)));
        Assert.Equal((80, 25), page.Fits(new Surface(4000, 3000)));
    }

    [Fact]
    public void AWindowIsOpenedLargerOnAScaledDisplay()
    {
        Assert.Equal(
            (80 * Paint.CellWidth, 30 * Paint.CellHeight),
            new Page(1).Opening(80, 30));

        Assert.Equal(
            (80 * Paint.CellWidth * 2, 30 * Paint.CellHeight * 2),
            new Page(2).Opening(80, 30));
    }

    [Fact]
    public void AFixedScreenIsOpenedAtTwiceItsOwnSize()
    {
        // [infocom pictures] The art on it is 320 by 200 doubled into
        // a 640 by 400 screen, so doubling that again is the size it
        // was meant to be looked at, before any scaling on top.
        Assert.Equal((1280, 800), new Page(1, (640, 400)).Opening(80, 30));
        Assert.Equal((2560, 1600), new Page(2, (640, 400)).Opening(80, 30));
    }

    [Fact]
    public void MagnifyingByLessThanOnceIsMagnifyingOnce()
    {
        Assert.Equal(1, new Page(0).Magnification);
        Assert.Equal(1, new Page(-3).Magnification);
    }
}

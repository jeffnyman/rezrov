using Rezrov.Core.Blorb;
using Rezrov.Glulx.Glk;

namespace Rezrov.Tests;

/// <summary>
/// [glk #graphics] The pictures a game draws: whether it is told it can
/// draw at all, the graphics window it draws into, the size each of the
/// rules works out, and the rectangles and clears that paint the rest.
/// </summary>
public class GlkGraphicsTests
{
    private const uint Whole = 0x10000;
    private const uint Red = 0x00FF0000;
    private const uint Blue = 0x000000FF;

    private const ImageRule Original = ImageRule.WidthOrig | ImageRule.HeightOrig;
    private const ImageRule Fixed = ImageRule.WidthFixed | ImageRule.HeightFixed;

    [Fact]
    public void AGraphicsWindowIsOpenedOnlyWhereOneCanBeShown()
    {
        // [glk #graphics_testing] A display that cannot show a picture
        // gets no graphics window, and says so rather than failing
        // later.
        var (text, _) = Library(graphics: false);
        var plain = text.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;

        Assert.Null(text.OpenWindow(plain, WindowMethod.Above | WindowMethod.Fixed, 8, WindowType.Graphics, 2));
        Assert.Contains("window_open: graphics windows are not supported.", text.Warnings);

        var (glk, _) = Library();
        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;
        var canvas = glk.OpenWindow(story, WindowMethod.Above | WindowMethod.Fixed, 8, WindowType.Graphics, 2);

        Assert.IsType<GraphicsWindow>(canvas);
    }

    [Fact]
    public void TheGestaltAnswersFollowWhatTheDisplayCanDraw()
    {
        var (glk, _) = Library();

        // [glk #graphics_testing] The suite as a whole, then each kind
        // of window on its own, since a library may do one and not the
        // other. Pictures go in graphics windows here and not into the
        // run of a text buffer, and both selectors for a window answer
        // alike, since anything that can draw can draw at a size.
        Assert.Equal(1u, glk.Gestalt((uint)GestaltSelector.Graphics, 0, null));
        Assert.Equal(1u, glk.Gestalt((uint)GestaltSelector.DrawImage, (uint)WindowType.Graphics, null));
        Assert.Equal(0u, glk.Gestalt((uint)GestaltSelector.DrawImage, (uint)WindowType.TextBuffer, null));
        Assert.Equal(1u, glk.Gestalt((uint)GestaltSelector.DrawImageScale, (uint)WindowType.Graphics, null));
        Assert.Equal(1u, glk.Gestalt((uint)GestaltSelector.GraphicsTransparency, 0, null));
        Assert.Equal(1u, glk.Gestalt((uint)GestaltSelector.GraphicsCharInput, 0, null));

        var (text, _) = Library(graphics: false);
        Assert.Equal(0u, glkAnswer(text, GestaltSelector.Graphics));
        Assert.Equal(0u, glkAnswer(text, GestaltSelector.DrawImage));
        Assert.Equal(0u, glkAnswer(text, GestaltSelector.GraphicsTransparency));
        Assert.Equal(0u, glkAnswer(text, GestaltSelector.GraphicsCharInput));

        static uint glkAnswer(GlkLibrary glk, GestaltSelector selector) =>
            glk.Gestalt((uint)selector, (uint)WindowType.Graphics, null);
    }

    [Fact]
    public void AGraphicsWindowIsMeasuredInPixelsAndATextWindowInCharacters()
    {
        // The display is 20 cells by 6, each cell 4 pixels square, and
        // the split asks for 8 pixels of height, which is two cells.
        var (glk, _) = Library();
        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;
        var canvas = (GraphicsWindow)glk.OpenWindow(story, WindowMethod.Above | WindowMethod.Fixed, 8, WindowType.Graphics, 2)!;

        // [glk op:window_get_size] Pixels for the one, characters for
        // the other.
        Assert.Equal((80, 8), canvas.ReportedSize);
        Assert.Equal((20, 4), story.ReportedSize);
        Assert.Equal((80, 8), (canvas.Canvas.Width, canvas.Canvas.Height));
    }

    [Fact]
    public void AFixedSplitRoundsUpToWholeCharacterCells()
    {
        // [glk #window_opening] A size in pixels that is not a whole
        // number of cells takes the cells it needs, and the game is
        // expected to read back what it got.
        var (glk, _) = Library();
        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;
        var canvas = (GraphicsWindow)glk.OpenWindow(story, WindowMethod.Above | WindowMethod.Fixed, 9, WindowType.Graphics, 2)!;

        Assert.Equal((80, 12), canvas.ReportedSize);
    }

    [Fact]
    public void APictureIsMeasuredWhetherOrNotItCanBeDrawn()
    {
        var (glk, _) = Library();

        // [glk op:image_get_info] The size of each picture of the
        // resource file, and nothing for a number that is not one.
        Assert.Equal((4, 2), glk.ImageInfo(1));
        Assert.Equal((10, 20), glk.ImageInfo(2));
        Assert.Null(glk.ImageInfo(9));

        // [glk #graphics_testing] And nothing at all where the library
        // said it has no graphics, since the same gestalt covers this
        // call as covers the drawing ones.
        var (text, _) = Library(graphics: false);
        Assert.Null(text.ImageInfo(1));
    }

    [Fact]
    public void APictureIsDrawnAtItsOwnSizeWhereItIsPut()
    {
        var (glk, display) = Library();
        var canvas = Canvas(glk);

        Assert.True(glk.DrawImage(canvas, 1, 2, 1, Original, 0, 0, Whole));

        // [glk #graphics_graphics] The upper left corner lands where it
        // was put, and the picture is its own four by two.
        Assert.Equal((255, 255, 255, 255), canvas.Canvas.At(1, 1));
        Assert.Equal((255, 0, 0, 255), canvas.Canvas.At(2, 1));
        Assert.Equal((255, 0, 0, 255), canvas.Canvas.At(5, 2));
        Assert.Equal((255, 255, 255, 255), canvas.Canvas.At(6, 2));
        Assert.Equal((255, 255, 255, 255), canvas.Canvas.At(2, 3));

        // The display is told the canvas changed, so it can show it.
        Assert.Contains(canvas, display.Drawings);
    }

    [Fact]
    public void EachRuleWorksOutTheSizeTheSpecificationSays()
    {
        var (glk, _) = Library();
        var canvas = Canvas(glk);

        // [glk op:image_draw_scaled] A width and a height in pixels.
        Assert.Equal((8, 4), Painted(glk, canvas, Fixed, 8, 4));

        // [glk op:image_draw_scaled_ext] A width that is a fraction of
        // the window's, which is 80 pixels across.
        Assert.Equal((40, 2), Painted(glk, canvas, ImageRule.WidthRatio | ImageRule.HeightOrig, 0x8000, 0));

        // The height as a multiple of the picture's own shape, which is
        // twice as wide as it is tall: at one, the shape is kept, and at
        // two it is stretched to twice as tall.
        Assert.Equal((8, 4), Painted(glk, canvas, ImageRule.WidthFixed | ImageRule.AspectRatio, 8, Whole));
        Assert.Equal((8, 8), Painted(glk, canvas, ImageRule.WidthFixed | ImageRule.AspectRatio, 8, 2 * Whole));

        // [glk op:image_draw_scaled] A size of zero draws nothing, and
        // is not a failure to draw.
        canvas.Clear();
        Assert.True(glk.DrawImage(canvas, 1, 0, 0, Fixed, 0, 4, Whole));
        Assert.Equal((0, 0), Extent(canvas));
    }

    [Fact]
    public void WhatCannotBeDrawnIsRefusedRatherThanDrawnWrong()
    {
        var (glk, _) = Library();
        var canvas = Canvas(glk);
        var story = glk.Windows.All.First(w => w.Type == WindowType.TextBuffer);

        // There is no such picture.
        Assert.False(glk.DrawImage(canvas, 9, 0, 0, Original, 0, 0, Whole));

        // [blorb 2.3] A placeholder rectangle has a size and nothing to
        // draw, so it measures but does not draw.
        Assert.False(glk.DrawImage(canvas, 2, 0, 0, Original, 0, 0, Whole));

        // [glk #graphics_testing] And a window this cannot draw in,
        // which gestalt_DrawImage already answered zero for.
        Assert.False(glk.DrawImage(story, 1, 0, 0, Original, 0, 0, Whole));

        Assert.Equal((0, 0), Extent(canvas));
    }

    [Fact]
    public void PaintingAndErasingUseTheWindowsBackgroundColor()
    {
        var (glk, _) = Library();
        var canvas = Canvas(glk);

        glk.FillRect(canvas, Red, 1, 1, 4, 2);
        Assert.Equal((255, 0, 0, 255), canvas.Canvas.At(1, 1));

        // [glk op:window_erase_rect] Back to the background, which is
        // still the white a window starts as.
        glk.EraseRect(canvas, 1, 1, 2, 2);
        Assert.Equal((255, 255, 255, 255), canvas.Canvas.At(1, 1));
        Assert.Equal((255, 0, 0, 255), canvas.Canvas.At(3, 1));

        // [glk op:window_set_background_color] A new color does not
        // change what is painted; the next clear does.
        glk.SetBackgroundColor(canvas, Blue);
        Assert.Equal((255, 0, 0, 255), canvas.Canvas.At(3, 1));

        canvas.Clear();
        Assert.Equal((0, 0, 255, 255), canvas.Canvas.At(3, 1));
        Assert.Equal((0, 0, 255, 255), canvas.Canvas.At(0, 0));
    }

    [Fact]
    public void AResizedWindowKeepsWhatWasPaintedWhereItWas()
    {
        // [glk #window_graphics] Growing a window leaves what was there
        // where it was and fills the new part with the background.
        var (glk, _) = Library();
        var canvas = Canvas(glk);
        var story = glk.Windows.All.First(w => w.Type == WindowType.TextBuffer);

        glk.FillRect(canvas, Red, 0, 0, 80, 8);
        glk.SetBackgroundColor(canvas, Blue);
        glk.SetArrangement(canvas.Parent!, WindowMethod.Above | WindowMethod.Fixed, 16, canvas);

        Assert.Equal((80, 16), canvas.ReportedSize);
        Assert.Equal((255, 0, 0, 255), canvas.Canvas.At(0, 0));
        Assert.Equal((0, 0, 255, 255), canvas.Canvas.At(0, 15));
        Assert.Equal((20, 2), story.ReportedSize);
    }

    [Fact]
    public void AGraphicsWindowTakesKeys()
    {
        // [glk #window_graphics] Character input is allowed in a
        // graphics window, which is what gestalt_GraphicsCharInput
        // promised. Line input is not, and is refused by the same test
        // that refuses it for a blank window.
        var (glk, _) = Library();
        var canvas = Canvas(glk);

        glk.RequestCharEvent(canvas, false);

        Assert.Equal(CharRequest.Latin1, canvas.CharRequest);
        Assert.DoesNotContain("request_char_event: the window does not take character input.", glk.Warnings);
    }

    /// <summary>
    /// A library over a display of 20 cells by 6, each cell four pixels
    /// square, with a resource file of two pictures: a solid red one of
    /// four by two, and a placeholder rectangle of ten by twenty.
    /// </summary>
    private static (GlkLibrary Glk, RecordingGlkDisplay Display) Library(bool graphics = true)
    {
        var display = new RecordingGlkDisplay(20, 6) { Graphics = graphics, Cell = 4 };
        var blorb = TestBlorb.Build(
        [
            (1, "PNG ", TestPng.Solid(4, 2, 255, 0, 0)),
            (2, "Rect", [0, 0, 0, 10, 0, 0, 0, 20]),
        ]);

        return (new GlkLibrary(display) { Resources = BlorbFile.Read(blorb) }, display);
    }

    /// <summary>
    /// A graphics window of 80 by 8 pixels, under a text buffer window.
    /// </summary>
    private static GraphicsWindow Canvas(GlkLibrary glk)
    {
        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;
        return (GraphicsWindow)glk.OpenWindow(story, WindowMethod.Above | WindowMethod.Fixed, 8, WindowType.Graphics, 2)!;
    }

    /// <summary>
    /// Draws the picture by a rule onto a clean canvas and says how much
    /// of the canvas it covered.
    /// </summary>
    private static (int Width, int Height) Painted(GlkLibrary glk, GraphicsWindow canvas, ImageRule rule, uint width, uint height)
    {
        canvas.Clear();
        Assert.True(glk.DrawImage(canvas, 1, 0, 0, rule, width, height, Whole));
        return Extent(canvas);
    }

    /// <summary>
    /// How far the painted part of a canvas reaches from its corner,
    /// the picture being the only thing on it and not white.
    /// </summary>
    private static (int Width, int Height) Extent(GraphicsWindow canvas)
    {
        var width = 0;
        var height = 0;

        for (var x = 0; x < canvas.Canvas.Width; x++)
        {
            if (canvas.Canvas.At(x, 0) != (255, 255, 255, 255))
            {
                width = x + 1;
            }
        }

        for (var y = 0; y < canvas.Canvas.Height; y++)
        {
            if (canvas.Canvas.At(0, y) != (255, 255, 255, 255))
            {
                height = y + 1;
            }
        }

        return (width, height);
    }
}

using Rezrov.Core.Blorb;
using Rezrov.Core.Graphics;

namespace Rezrov.Glulx.Glk;

/// <summary>
/// [glk #graphics] The pictures a game draws: measuring them, sizing
/// them, and painting them and rectangles of color onto the canvas of a
/// graphics window.
/// </summary>
/// <remarks>
/// [glk #graphics_images] A picture is a resource of the game's own
/// resource file, named by number, and what format it is in is the
/// library's business rather than the game's. Reading it is
/// <see cref="PictureReader"/>'s work; what is here is the part Glk
/// defines: which picture, at what size, and where.
///
/// [glk #graphics_graphics] Drawing into a graphics window is done in
/// full here, since the window's canvas is the library's. Drawing into
/// a text buffer window is not: a picture in a run of text has to be
/// placed by whatever lays the text out, so that is a frontend's to do
/// and waits for a frontend that can. gestalt_DrawImage answers for
/// each kind of window separately, which is exactly the case the
/// specification has in mind when it says a library may implement one
/// and not the other.
/// </remarks>
public sealed partial class GlkLibrary
{
    /// <summary>
    /// [glk op:image_draw_scaled_ext] The fixed-point fraction that
    /// stands for the whole of something.
    /// </summary>
    private const uint Whole = 0x10000;

    /// <summary>
    /// The largest size a picture may be asked to be drawn at. A canvas
    /// is a few thousand pixels at most and anything past its edge is
    /// clipped away, so this changes nothing a game can see; it keeps
    /// the arithmetic of a ratio rule from running off the end of what
    /// a number holds.
    /// </summary>
    private const long LargestDrawn = 1 << 20;

    private readonly Dictionary<uint, Pixels?> _images = [];
    private BlorbPictures? _pictures;
    private BlorbFile? _picturesFrom;

    /// <summary>
    /// [glk #graphics_testing] Whether pictures can be drawn anywhere at
    /// all, which is what gestalt_Graphics answers and what every call
    /// of the graphics suite is gated on.
    /// </summary>
    public bool CanDrawImages =>
        _display.CanDrawImages(WindowType.Graphics) || _display.CanDrawImages(WindowType.TextBuffer);

    /// <summary>
    /// [glk #graphics_images] The pictures of the resource file, or null
    /// where there is no resource file or its pictures cannot be read.
    /// </summary>
    private BlorbPictures? Pictures
    {
        get
        {
            if (ReferenceEquals(_picturesFrom, Resources))
            {
                return _pictures;
            }

            _picturesFrom = Resources;
            _pictures = null;
            _images.Clear();

            if (Resources is null)
            {
                return null;
            }

            try
            {
                _pictures = BlorbPictures.From(Resources);
            }
            catch (InvalidDataException)
            {
                // [blorb 2] A picture whose own header is malformed
                // makes the whole catalog unreadable, so the game is
                // told there are no pictures rather than some of them.
                Warn("the resource file's pictures cannot be read.");
            }

            return _pictures;
        }
    }

    /// <summary>
    /// [glk op:image_get_info] The size a picture will be drawn at, or
    /// null if there is no such picture.
    /// </summary>
    /// <remarks>
    /// Pictures are drawn at the size they are stored at, so this is
    /// that size. [blorb 11.2] The scaling rules of a resource file
    /// measure a Z-machine screen against the window the author drew
    /// for; Glk has no such notion, and a Glk game is expected to ask
    /// for this size and lay itself out from it.
    /// </remarks>
    public (int Width, int Height)? ImageInfo(uint image)
    {
        // [glk #graphics_testing] gestalt_Graphics covers this call as
        // well as the drawing ones, so a library that cannot draw knows
        // about no pictures. Games do call it without asking first, and
        // a size answered here would have them lay out a display they
        // then cannot draw on.
        return CanDrawImages && Pictures?.Find((int)image) is { } picture
            ? (picture.Width, picture.Height)
            : null;
    }

    /// <summary>
    /// [glk op:image_draw_scaled_ext] Draws a picture in a window, at a
    /// size the rules work out.
    /// </summary>
    /// <param name="window">The window to draw in.</param>
    /// <param name="image">Which picture.</param>
    /// <param name="first">
    /// [glk #graphics_graphics] The x coordinate in a graphics window,
    /// and [glk #graphics_textbuf] the alignment in a text buffer.
    /// </param>
    /// <param name="second">The y coordinate, unused elsewhere.</param>
    /// <param name="rule">How the width and height are arrived at.</param>
    /// <param name="width">The width argument the rule reads.</param>
    /// <param name="height">The height argument the rule reads.</param>
    /// <param name="maximum">
    /// [glk op:image_draw_scaled_ext] An upper bound on the width as a
    /// fraction of the window's, which the specification says is
    /// ignored in graphics windows.
    /// </param>
    /// <returns>Whether the picture was drawn.</returns>
    public bool DrawImage(GlkWindow window, uint image, int first, int second, ImageRule rule, uint width, uint height, uint maximum)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!_display.CanDrawImages(window.Type))
        {
            // [glk #graphics_testing] Which the gestalt answer already
            // told the game, so this is a game that did not ask.
            Warn($"image_draw: a window of type {window.Type} cannot show pictures.");
            return false;
        }

        if (window is not GraphicsWindow graphics)
        {
            Warn($"image_draw: pictures in a window of type {window.Type} are not placed yet.");
            return false;
        }

        if (Pictures?.Find((int)image) is not { } picture)
        {
            Warn($"image_draw: there is no picture {image}.");
            return false;
        }

        if (Image(image) is not { } pixels)
        {
            // [blorb 2.3] A placeholder rectangle, or a picture in a
            // format with no decoder, which the specification allows a
            // draw to fail over.
            Warn($"image_draw: picture {image} cannot be drawn.");
            return false;
        }

        var (across, down) = Sized(rule, picture, graphics.PixelWidth, width, height);

        // [glk op:image_draw_scaled] A picture of no width or height
        // draws nothing, which is not a failure to draw it.
        if (across > 0 && down > 0)
        {
            graphics.Canvas.Draw(pixels, first, second, across, down);
            _display.Drawn(graphics);
        }

        return true;
    }

    /// <summary>
    /// [glk op:window_fill_rect] Paints a rectangle of a graphics
    /// window one color.
    /// </summary>
    public void FillRect(GlkWindow window, uint color, int left, int top, uint width, uint height)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window is not GraphicsWindow graphics)
        {
            Warn($"window_fill_rect: a window of type {window.Type} cannot be painted.");
            return;
        }

        graphics.Canvas.Fill(left, top, Extent(width), Extent(height), Color(color));
        _display.Drawn(graphics);
    }

    /// <summary>
    /// [glk op:window_erase_rect] Paints a rectangle of a graphics
    /// window its background color.
    /// </summary>
    public void EraseRect(GlkWindow window, int left, int top, uint width, uint height)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window is not GraphicsWindow graphics)
        {
            Warn($"window_erase_rect: a window of type {window.Type} cannot be painted.");
            return;
        }

        graphics.Canvas.Fill(left, top, Extent(width), Extent(height), graphics.Canvas.Background);
        _display.Drawn(graphics);
    }

    /// <summary>
    /// [glk op:window_set_background_color] Sets the color a graphics
    /// window's clears and resizes leave behind. What is already
    /// painted is not changed.
    /// </summary>
    public void SetBackgroundColor(GlkWindow window, uint color)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window is not GraphicsWindow graphics)
        {
            Warn($"window_set_background_color: a window of type {window.Type} has no background color.");
            return;
        }

        graphics.Canvas.Background = Color(color);
    }

    /// <summary>
    /// [glk op:window_flow_break] Breaks the run of text below whatever
    /// margin pictures it is flowing around.
    /// </summary>
    /// <remarks>
    /// [glk #graphics_textbuf] This has no effect in any window but a
    /// text buffer, and no picture can be in a text buffer's margin
    /// yet, so there is never anything to break past. It is here so
    /// that a game which calls it, as the specification advises before
    /// every margin picture, goes on rather than stopping.
    /// </remarks>
    public static void FlowBreak(GlkWindow window) => ArgumentNullException.ThrowIfNull(window);

    /// <summary>
    /// The decoded picture, read once and kept, since a game draws the
    /// same few over and over.
    /// </summary>
    private Pixels? Image(uint image)
    {
        if (_images.TryGetValue(image, out var known))
        {
            return known;
        }

        var pixels = Pictures?.Find((int)image) is { } picture ? PictureReader.Decode(picture) : null;
        _images[image] = pixels;
        return pixels;
    }

    /// <summary>
    /// [glk op:image_draw_scaled_ext] The size to draw a picture at:
    /// the width first, then the height, since a height given as an
    /// aspect ratio is measured against the width that was settled on.
    /// </summary>
    private static (int Width, int Height) Sized(ImageRule rule, PictureInfo picture, int windowWidth, uint width, uint height)
    {
        // The masks are applied here rather than named in the enum,
        // where their values would collide with the last rule of each
        // set.
        var across = (ImageRule)((uint)rule & 0x03) switch
        {
            ImageRule.WidthFixed => width,
            ImageRule.WidthRatio => (long)windowWidth * width / Whole,
            _ => picture.Width,
        };

        across = Math.Clamp(across, 0, LargestDrawn);

        var down = (ImageRule)((uint)rule & 0x0C) switch
        {
            ImageRule.HeightFixed => height,
            ImageRule.AspectRatio when picture.Width > 0 =>
                across * picture.Height / picture.Width * height / Whole,
            ImageRule.AspectRatio => 0,
            _ => picture.Height,
        };

        return ((int)across, (int)Math.Clamp(down, 0, LargestDrawn));
    }

    /// <summary>
    /// [glk #graphics_graphics] A color, whose top eight bits must be
    /// zero, as red, green, and blue.
    /// </summary>
    private static uint Color(uint color) => color & 0x00FFFFFF;

    /// <summary>
    /// A width or height the game gave as an unsigned number, which a
    /// canvas measures as a signed one. Anything past what a canvas
    /// could hold is as good as the largest it could.
    /// </summary>
    private static int Extent(uint size) => size > int.MaxValue ? int.MaxValue : (int)size;
}

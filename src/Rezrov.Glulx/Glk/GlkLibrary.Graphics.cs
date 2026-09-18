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
/// full here, since the window's canvas is the library's. [glk
/// #graphics_textbuf] Drawing into a text buffer window is handed to
/// the display, since a picture in a run of text has to be placed by
/// whatever lays the text out, and its size settled again every time
/// that happens. gestalt_DrawImage answers for each kind of window
/// separately, which is exactly the case the specification has in mind
/// when it says a library may implement one and not the other.
/// </remarks>
public sealed partial class GlkLibrary
{
    /// <summary>
    /// [glk op:image_draw_scaled_ext] The fixed-point fraction that
    /// stands for the whole of something.
    /// </summary>
    private const uint Whole = ImageSizing.Whole;

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

        if (Pictures?.Find((int)image) is null)
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

        var sizing = new ImageSizing(rule, width, height, maximum);

        if (window is GraphicsWindow graphics)
        {
            // [glk op:image_draw_scaled_ext] The bound on the width is
            // a text buffer's business and is ignored here.
            var (across, down) = (sizing with { Maximum = 0 })
                .For(graphics.PixelWidth, pixels.Width, pixels.Height);

            // [glk op:image_draw_scaled] A picture of no width or
            // height draws nothing, which is not a failure to draw it.
            if (across > 0 && down > 0)
            {
                graphics.Canvas.Draw(pixels, first, second, across, down);
                _display.Drawn(graphics);
            }

            return true;
        }

        // [glk #graphics_textbuf] In a text buffer the first argument
        // is the alignment rather than a coordinate, and the second is
        // unused. The size is not settled here: it is measured against
        // the width of the window the text is laid out in, which is the
        // display's to know and to work out again whenever it changes.
        // [glk #link_creating] A picture takes the link value of the
        // stream it was printed to, as the text around it does.
        return _display.DrawImage(window, image, pixels, Alignment(image, first), sizing, window.Stream.Link);
    }

    /// <summary>
    /// [glk #graphics_textbuf] The alignment a picture in a text buffer
    /// was given, of which only five values mean anything.
    /// </summary>
    /// <remarks>
    /// Anything else is placed in the run of the text, which is what
    /// the reference library does. The game has asked for a picture and
    /// named a place for it that does not exist; showing it somewhere
    /// is closer to what was asked for than showing it nowhere.
    /// </remarks>
    private ImageAlign Alignment(uint image, int given)
    {
        if (given is >= (int)ImageAlign.InlineUp and <= (int)ImageAlign.MarginRight)
        {
            return (ImageAlign)given;
        }

        Warn($"image_draw: picture {image} was given alignment {given}, which is not one of the five.");
        return ImageAlign.InlineUp;
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
    /// text buffer, and what effect it has there depends on where the
    /// margin pictures fall once the text has been laid out, which the
    /// display knows and this does not. The specification describes it
    /// as an invisible mark in the stream of text, which works out how
    /// many newlines it needs each time the text is formatted.
    /// </remarks>
    public void FlowBreak(GlkWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Type == WindowType.TextBuffer)
        {
            _display.FlowBreak(window);
        }
    }

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

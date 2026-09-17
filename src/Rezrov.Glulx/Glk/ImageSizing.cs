namespace Rezrov.Glulx.Glk;

/// <summary>
/// [glk op:image_draw_scaled_ext] How large a picture is to be drawn:
/// a width rule, a height rule, the arguments those rules read, and an
/// upper bound on the width.
/// </summary>
/// <remarks>
/// The rules are carried about rather than worked out once because of
/// where the answer has to come from. [glk #graphics_graphics] In a
/// graphics window the size is settled when the game asks, and the
/// pixels are painted onto a canvas that keeps them. [glk
/// #graphics_textbuf] In a text buffer it is settled again every time
/// the text is laid out: a width given as a fraction of the window is a
/// different number in a window of a different size, and the
/// specification says the picture resizes along with the window. So the
/// library settles the size for a graphics window and a frontend
/// settles it for a text buffer, both of them from this.
/// </remarks>
/// <param name="Rule">The width rule and the height rule together.</param>
/// <param name="Width">The width argument the rule reads.</param>
/// <param name="Height">The height argument the rule reads.</param>
/// <param name="Maximum">
/// An upper bound on the width, as a fixed-point fraction of the
/// window's own width, or zero for no bound.
/// </param>
public readonly record struct ImageSizing(ImageRule Rule, uint Width, uint Height, uint Maximum)
{
    /// <summary>
    /// [glk op:image_draw_scaled_ext] The fixed-point fraction that
    /// stands for the whole of something.
    /// </summary>
    public const uint Whole = 0x10000;

    /// <summary>
    /// The largest size a picture may be asked to be drawn at. A window
    /// is a few thousand pixels at most and anything past its edge is
    /// clipped away, so this changes nothing a game can see; it keeps
    /// the arithmetic of a ratio rule from running off the end of what
    /// a number holds.
    /// </summary>
    private const long Largest = 1 << 20;

    /// <summary>
    /// The size to draw a picture of the given size at, in a window of
    /// the given width.
    /// </summary>
    /// <remarks>
    /// [glk op:image_draw_scaled_ext] The width is settled first, since
    /// a height given as an aspect ratio is measured against the width
    /// that was arrived at, and the bound is applied last of all, since
    /// it reduces the two together and so keeps the picture's shape.
    /// </remarks>
    public (int Width, int Height) For(int windowWidth, int pictureWidth, int pictureHeight)
    {
        // The masks are applied here rather than named in the enum,
        // where their values would collide with the last rule of each
        // set.
        var across = (ImageRule)((uint)Rule & 0x03) switch
        {
            ImageRule.WidthFixed => Width,
            ImageRule.WidthRatio => (long)windowWidth * Width / Whole,
            _ => pictureWidth,
        };

        across = Math.Clamp(across, 0, Largest);

        var down = (ImageRule)((uint)Rule & 0x0C) switch
        {
            ImageRule.HeightFixed => Height,
            ImageRule.AspectRatio when pictureWidth > 0 =>
                Math.Clamp(across * pictureHeight / pictureWidth, 0, Largest) * Height / Whole,
            ImageRule.AspectRatio => 0,
            _ => pictureHeight,
        };

        down = Math.Clamp(down, 0, Largest);

        // [glk op:image_draw_scaled_ext] The bound is a fraction of the
        // window's width as well, and a picture wider than it comes
        // down proportionally rather than being cut off.
        if (Maximum != 0)
        {
            var bound = Math.Clamp((long)windowWidth * Maximum / Whole, 0, Largest);
            if (across > bound)
            {
                down = down * bound / across;
                across = bound;
            }
        }

        return ((int)across, (int)down);
    }
}

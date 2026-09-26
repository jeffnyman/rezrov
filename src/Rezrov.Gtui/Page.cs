namespace Rezrov.Gtui;

/// <summary>
/// The rectangle a game is drawn on, and how it reaches the window when
/// the window's pixels are not the pixels being drawn.
/// </summary>
/// <remarks>
/// There are two reasons the two differ, and they meet here because
/// the answer is the same for both.
///
/// [infocom pictures] A Version 6 game laid out for one fixed screen
/// is drawn at that screen's size whatever the window's size, and the
/// whole of it is magnified into the window.
///
/// And a display the operating system scales would otherwise stretch
/// this program's pixels for it, which turns a font drawn a bit at a
/// time and artwork drawn a pixel at a time into a blur. So the
/// drawing is done small and magnified by a whole number here, where
/// nearest neighbour keeps every pixel the shape it was drawn.
///
/// Where neither applies, which is a display at its own resolution
/// playing an ordinary game, there is nothing to magnify and the
/// drawing goes straight into the window as it always did.
/// </remarks>
public sealed class Page
{
    // How many times its own size a fixed screen is opened at, before
    // whatever the display is scaled by.
    private const int Comfortable = 2;

    private readonly (int Width, int Height)? _fixed;

    private Surface? _drawn;

    /// <param name="magnification">
    /// How many of the window's pixels one drawn pixel becomes.
    /// </param>
    /// <param name="fixedSize">
    /// The one screen the game is laid out for, or null for a game
    /// that takes the window's own size.
    /// </param>
    public Page(int magnification, (int Width, int Height)? fixedSize = null)
    {
        Magnification = Math.Max(magnification, 1);
        _fixed = fixedSize;
    }

    /// <summary>
    /// How many of the window's pixels one drawn pixel becomes.
    /// </summary>
    public int Magnification { get; }

    /// <summary>
    /// Whether the drawing goes straight into the window, with nothing
    /// to magnify and no copy to make.
    /// </summary>
    public bool Direct => Magnification == 1 && _fixed is null;

    /// <summary>
    /// Whether the grid changes when the window does, which a game
    /// laid out for one fixed screen has no use for.
    /// </summary>
    public bool Follows => _fixed is null;

    /// <summary>
    /// How large to open a window that is to hold this many characters.
    /// </summary>
    /// <remarks>
    /// [infocom pictures] A fixed screen is opened at twice its own
    /// size before the display's scaling is counted, because the art
    /// on it is 320 by 200 doubled into a 640 by 400 screen and
    /// doubling that again is the size it was meant to be looked at.
    /// A window of ordinary text wants no such thing: its size is the
    /// characters asked for.
    /// </remarks>
    public (int Width, int Height) Opening(int columns, int rows) => (
        (_fixed is { } across ? across.Width * Comfortable : columns * Paint.CellWidth) * Magnification,
        (_fixed is { } down ? down.Height * Comfortable : rows * Paint.CellHeight) * Magnification);

    /// <summary>
    /// The grid of characters a window of this size holds.
    /// </summary>
    public (int Columns, int Rows) Fits(Surface window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return Paint.Fits(
            _fixed?.Width ?? (window.Width / Magnification),
            _fixed?.Height ?? (window.Height / Magnification));
    }

    /// <summary>
    /// Draws the game and shows it in the window.
    /// </summary>
    /// <remarks>
    /// The drawing is handed a surface to fill and knows nothing about
    /// any of this. Whatever locking a drawing needs happens inside
    /// it, so the magnifying afterwards holds nothing up.
    /// </remarks>
    public void Show(Surface window, Action<Surface> draw)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(draw);

        if (Direct)
        {
            draw(window);
            return;
        }

        var wide = _fixed?.Width ?? Math.Max(window.Width / Magnification, 1);
        var high = _fixed?.Height ?? Math.Max(window.Height / Magnification, 1);

        if (_drawn is null || _drawn.Width != wide || _drawn.Height != high)
        {
            _drawn = new Surface(wide, high);
        }

        draw(_drawn);

        // A fixed screen is fitted to the window, since the window may
        // be any size and the screen may not. Everything else is drawn
        // at exactly the size that magnifies back to the window.
        var scale = _fixed is null
            ? Magnification
            : Math.Max(1, Math.Min(window.Width / wide, window.Height / high));

        // What the magnified page does not cover is left dark rather
        // than filled with the game's own color, so that a screen a
        // size too small looks like a screen rather than like a game
        // that has lost its edges.
        window.Fill(Surface.Black);
        window.Magnify(
            _drawn,
            scale,
            (window.Width - (wide * scale)) / 2,
            (window.Height - (high * scale)) / 2);
    }
}

namespace Rezrov.Gui;

/// <summary>
/// [infocom pictures] How a screen of a fixed size sits inside a window
/// of another: scaled up as far as it will go with its shape kept, and
/// centered in whatever is left over.
/// </summary>
/// <remarks>
/// An Infocom Version 6 game computes every coordinate for the screen
/// it was written against, so it is given that screen and the whole of
/// it is scaled into the window.
///
/// Painting and the pointer both need this arithmetic and they have to
/// agree. If they drift, a click lands near enough to the thing it
/// names to look right and still be wrong, which is the sort of fault
/// that survives a long time. So it lives here, once, where it can be
/// checked without a window.
/// </remarks>
/// <param name="Scale">How many window pixels one screen unit covers.</param>
/// <param name="Across">Pixels of margin down the left.</param>
/// <param name="Down">Pixels of margin along the top.</param>
public readonly record struct ScreenFit(double Scale, double Across, double Down)
{
    /// <summary>
    /// The fit of a screen of <paramref name="units"/> in a window of
    /// <paramref name="window"/>, or null where either is empty.
    /// </summary>
    public static ScreenFit? Of((double Width, double Height) window, (int Width, int Height) units)
    {
        if (units.Width <= 0 || units.Height <= 0 || window.Width <= 0 || window.Height <= 0)
        {
            return null;
        }

        var scale = Math.Min(window.Width / units.Width, window.Height / units.Height);

        return new ScreenFit(
            scale,
            (window.Width - (units.Width * scale)) / 2,
            (window.Height - (units.Height * scale)) / 2);
    }

    /// <summary>
    /// A point in the window, taken back into the screen's own
    /// coordinates.
    /// </summary>
    public (double X, double Y) Unscaled(double x, double y) =>
        ((x - Across) / Scale, (y - Down) / Scale);
}

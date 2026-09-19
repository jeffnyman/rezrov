namespace Rezrov.Gtui;

/// <summary>
/// A window with a rectangle of pixels in it, whatever the operating
/// system underneath happens to be.
/// </summary>
/// <remarks>
/// There is very little here, because there is very little a game needs
/// from a window: somewhere to draw, keys coming back, word that the
/// size changed, and a loop that keeps the thing alive until it closes.
/// Everything else a toolkit offers is for programs that are not this
/// one.
///
/// Keys arrive already turned into [zm 3.8] the Z-machine's own codes.
/// Each platform names its keys differently and only the platform knows
/// how, so the translating belongs on that side of this line rather
/// than leaking a Windows virtual key or an X11 keysym into the part of
/// the program that plays the game.
/// </remarks>
internal interface IGridWindow : IDisposable
{
    /// <summary>The pixels the window shows.</summary>
    Surface Surface { get; }

    /// <summary>
    /// [zm 3.8] A key the player pressed, as a Z-machine character, or
    /// nothing at all for a key the Z-machine has no name for.
    /// </summary>
    Action<ushort>? Key { get; set; }

    /// <summary>The window changed size, and the surface with it.</summary>
    Action? Resized { get; set; }

    /// <summary>
    /// Fill the surface: called on the thread that owns the window,
    /// just before the pixels are shown, so that nothing is drawing
    /// into a surface while it is being read.
    /// </summary>
    Action<Surface>? Painting { get; set; }

    /// <summary>
    /// Opens the window with a drawing area of the size asked for, so
    /// a program that wants eighty columns gets eighty columns.
    /// </summary>
    void Open(string title, int width, int height);

    /// <summary>
    /// Asks for the window to be painted again. Safe to call from the
    /// thread running the game.
    /// </summary>
    void Redraw();

    /// <summary>
    /// Answers the window's messages until it closes. This is the
    /// program's main loop, and it belongs to the thread that opened
    /// the window.
    /// </summary>
    void Run();

    /// <summary>
    /// Asks the window to close, from any thread, which is how the game
    /// says it has finished.
    /// </summary>
    void Close();
}

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
    /// How many of the window's pixels one drawn pixel should become,
    /// which is 1 on a display showing its own pixels.
    /// </summary>
    /// <remarks>
    /// A system that scales its displays hands a program that has not
    /// said otherwise a smaller window and stretches the result, which
    /// blurs a font drawn a bit at a time. A platform that says so and
    /// is given real pixels instead answers with how many of them a
    /// drawn pixel is worth, and the drawing is magnified by that
    /// whole number rather than smeared across it.
    ///
    /// Answering 1, which is what a platform that does none of this
    /// does, leaves the drawing exactly as it was.
    /// </remarks>
    int Magnification => 1;

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
    /// The mark the window wears, as the bytes of an icon file, which
    /// is what every one of these systems would rather be handed than
    /// pixels: each has its own decoder and its own idea of what order
    /// the rows go in.
    /// </summary>
    /// <remarks>
    /// A system with nowhere to put a mark, or one that will not read
    /// this one, does nothing. A window without an icon is a window,
    /// and a game is not worth failing to open over its absence.
    /// </remarks>
    void SetIcon(byte[] icon);

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

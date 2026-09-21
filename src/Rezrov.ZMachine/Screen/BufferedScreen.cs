namespace Rezrov.ZMachine.Screen;

/// <summary>
/// A fixed grid of cells as the screen model's frontend: the model
/// wraps and pages for it, and whatever is showing it paints the grid.
/// </summary>
/// <remarks>
/// The interpreter runs on its own thread and calls this from there,
/// while a frontend draws on its own. Every method changes the
/// <see cref="ScreenBuffer"/> under a lock and asks for a repaint; the
/// frontend reads the buffer under the same lock. The one call that
/// waits, the [MORE] prompt, blocks the interpreter's thread until the
/// input side reports a key, which is exactly the pause the standard
/// asks for.
///
/// What differs between one frontend and another is the size of a
/// character in [zm 8.8.1] units, what the screen can do, and the two
/// colors it starts in, so those are given rather than decided here. A
/// frontend that also has to answer to something of its own inherits
/// from this and says so.
/// </remarks>
public class BufferedScreen : IScreen
{
    private readonly Action _repaint;
    private readonly Func<ushort> _waitForKey;
    private readonly TextAttributes _blank;

    /// <param name="width">The grid's width in characters.</param>
    /// <param name="height">Its height in characters.</param>
    /// <param name="cursorStartsAtBottom">
    /// Whether the lower window's cursor begins at the foot of the
    /// screen rather than the top, as a console's does.
    /// </param>
    /// <param name="repaint">Asks the frontend to draw again.</param>
    /// <param name="waitForKey">
    /// [zm 8.4.1] Waits for the key that dismisses a [MORE] prompt.
    /// </param>
    /// <param name="fontWidth">
    /// [zm 8.8.1] How many units wide a character is.
    /// </param>
    /// <param name="fontHeight">How many units tall it is.</param>
    /// <param name="capabilities">What this screen can do.</param>
    /// <param name="foreground">The color text starts in.</param>
    /// <param name="background">The color behind it.</param>
    public BufferedScreen(
        int width,
        int height,
        bool cursorStartsAtBottom,
        Action repaint,
        Func<ushort> waitForKey,
        int fontWidth,
        int fontHeight,
        ScreenCapabilities capabilities,
        ScreenColor foreground = ScreenColor.White,
        ScreenColor background = ScreenColor.Black)
    {
        ArgumentNullException.ThrowIfNull(repaint);
        ArgumentNullException.ThrowIfNull(waitForKey);

        _repaint = repaint;
        _waitForKey = waitForKey;
        FontWidth = fontWidth;
        FontHeight = fontHeight;
        Capabilities = capabilities;
        DefaultForeground = foreground;
        DefaultBackground = background;
        _blank = new TextAttributes(TextStyle.Roman, foreground, background, TextAttributes.NormalFont);
        Buffer = new ScreenBuffer(width, height, _blank, cursorStartsAtBottom);
    }

    /// <summary>The grid, to be read under <see cref="Sync"/>.</summary>
    public ScreenBuffer Buffer { get; }

    /// <summary>The lock the buffer is read and written under.</summary>
    public object Sync { get; } = new();

    /// <summary>
    /// [zm 8.8.6] The pictures the game has drawn and where they went,
    /// to be read under <see cref="Sync"/>. Empty for a game that draws
    /// none, and for every game before Version 6.
    /// </summary>
    public IReadOnlyList<PicturePlacement> Pictures { get; private set; } = [];

    public int Width => Buffer.Width;

    public int Height => Buffer.Height;

    public Cell this[int row, int column] => Buffer[row, column];

    /// <summary>
    /// The cursor for input: in the upper window while the game reads
    /// there, as Bureaucracy's form does, and otherwise in the lower.
    /// </summary>
    public (int Row, int Column)? Cursor =>
        Buffer.CursorVisible ? Buffer.InputCursor : null;

    /// <summary>
    /// The buffer is always up to date, since the interpreter writes
    /// its cells directly.
    /// </summary>
    public virtual void Repaint()
    {
    }

    /// <summary>
    /// [zm 8.8.1] How many units wide a character is. A Version 6
    /// screen is measured in units and the unit is the interpreter's to
    /// choose, so this is the frontend's answer rather than a fixed
    /// one: a screen of pixels says how many pixels a character takes,
    /// and a screen of characters says whatever shape suits the games.
    /// </summary>
    public int FontWidth { get; }

    public int FontHeight { get; }

    public ScreenCapabilities Capabilities { get; }

    public ScreenColor DefaultForeground { get; }

    public ScreenColor DefaultBackground { get; }

    /// <summary>
    /// [zm 3.8.5.4.1] Anything but a control code; what the font has
    /// and a box for the rest is the frontend's affair.
    /// </summary>
    public virtual bool CanPrint(char character) => !char.IsControl(character);

    public void Print(string text, TextAttributes attributes)
    {
        lock (Sync)
        {
            Buffer.Print(text, attributes);
        }

        _repaint();
    }

    public void NewLine()
    {
        lock (Sync)
        {
            Buffer.NewLine();
        }

        _repaint();
    }

    public void EraseLowerWindow(ScreenColor background)
    {
        lock (Sync)
        {
            Buffer.EraseLower(_blank with { Background = background });
        }

        _repaint();
    }

    public void EraseToEndOfLine(ScreenColor background)
    {
        lock (Sync)
        {
            Buffer.EraseToEndOfLine(_blank with { Background = background });
        }

        _repaint();
    }

    /// <summary>
    /// [zm 8.4.1] Shows [MORE] at the cursor, waits for a key, and takes
    /// it away again.
    /// </summary>
    public void MorePrompt()
    {
        int column;
        lock (Sync)
        {
            column = Buffer.CursorColumn;
            Buffer.Print("[MORE]", _blank with { Style = TextStyle.ReverseVideo });
        }

        _repaint();
        _waitForKey();

        lock (Sync)
        {
            for (var i = 0; i < 6; i++)
            {
                Buffer.Backspace();
            }

            while (Buffer.CursorColumn > column)
            {
                Buffer.Backspace();
            }
        }

        _repaint();
    }

    public void UpdateUpperWindow(ScreenModel model)
    {
        lock (Sync)
        {
            Buffer.UpdateUpper(model);
        }

        _repaint();
    }

    /// <summary>
    /// [arc contract 3] Which picture the band is showing, or 0 while
    /// there is no band. What to make of the number is whatever paints
    /// this screen; the grid only knows how many rows it lost.
    /// </summary>
    public int BandPicture { get; private set; }

    public void DrawImageBand(int picture, int mode)
    {
        lock (Sync)
        {
            BandPicture = picture;
            Buffer.SetBand(picture == 0 ? 0 : mode);
        }

        _repaint();
    }

    public void UpdateWindows(WindowedScreenModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        lock (Sync)
        {
            Buffer.UpdateWindows(model);

            // [zm op:draw_picture] Where every picture went, kept for
            // whatever is showing the screen. A frontend of characters
            // has nothing to do with these; one with pixels draws them.
            Pictures = model.Pictures;
        }

        _repaint();
    }

    /// <summary>Shows a typed character at the cursor.</summary>
    public void Echo(char character)
    {
        lock (Sync)
        {
            Buffer.Print(character.ToString(), _blank);
        }

        _repaint();
    }

    /// <summary>Takes back the last typed character.</summary>
    public void EchoBackspace()
    {
        lock (Sync)
        {
            Buffer.Backspace();
        }

        _repaint();
    }

    /// <summary>The return key, as the player sees it.</summary>
    public void EchoNewLine() => NewLine();

    /// <summary>Fits the grid to a new terminal size.</summary>
    public void Resize(int width, int height)
    {
        lock (Sync)
        {
            Buffer.Resize(width, height);
        }

        _repaint();
    }
}

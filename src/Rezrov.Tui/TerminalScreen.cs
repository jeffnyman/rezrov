using Rezrov.ZMachine.Screen;

namespace Rezrov.Tui;

/// <summary>
/// The terminal as the screen model's frontend: a fixed grid the model
/// wraps and pages for, with every change marshaled to the UI thread.
/// </summary>
/// <remarks>
/// The interpreter runs on its own thread and calls this from there,
/// while Terminal.Gui draws on the main thread. Every method changes
/// the <see cref="ScreenBuffer"/> under a lock and asks the view to
/// repaint; the view reads the buffer under the same lock. The one
/// call that waits, the [MORE] prompt, blocks the interpreter's thread
/// until the input side reports a key, which is exactly the pause the
/// standard asks for.
/// </remarks>
public sealed class TerminalScreen : IScreen
{
    private readonly Action _repaint;
    private readonly Func<ushort> _waitForKey;
    private readonly TextAttributes _blank;

    public TerminalScreen(int width, int height, bool cursorStartsAtBottom, Action repaint, Func<ushort> waitForKey)
    {
        ArgumentNullException.ThrowIfNull(repaint);
        ArgumentNullException.ThrowIfNull(waitForKey);

        _repaint = repaint;
        _waitForKey = waitForKey;
        _blank = new TextAttributes(ZMachine.Screen.TextStyle.Roman, DefaultForeground, DefaultBackground, TextAttributes.NormalFont);
        Buffer = new ScreenBuffer(width, height, _blank, cursorStartsAtBottom);
    }

    /// <summary>The grid, to be read under <see cref="Sync"/>.</summary>
    public ScreenBuffer Buffer { get; }

    /// <summary>The lock the buffer is read and written under.</summary>
    public object Sync { get; } = new();

    public int Width => Buffer.Width;

    public int Height => Buffer.Height;

    /// <summary>
    /// [zm 8.8.1] A Version 6 screen is measured in units, and the unit
    /// is the interpreter's to choose. A cell here is 4 units wide and
    /// 1 unit high, which is not a shape any pixel screen has but is
    /// the shape Infocom's games assume when they have no pictures:
    /// they measure text sideways in pixels, through output stream 3,
    /// with constants that want a screen at least 320 wide, and they
    /// count downward in lines. An 80 by 24 terminal is then 320 by
    /// 24, and Zork Zero, Journey, and Shogun lay themselves out as
    /// they were meant to.
    /// </summary>
    public int FontWidth => 4;

    public int FontHeight => 1;

    /// <summary>
    /// Everything a terminal can do: the status line and upper window
    /// are drawn, styles and colors shown, and [zm 7.2] wrapping and
    /// [zm 8.4.1] paging are the model's to do on this fixed grid.
    /// </summary>
    public ScreenCapabilities Capabilities =>
        ScreenCapabilities.StatusLine | ScreenCapabilities.UpperWindow | ScreenCapabilities.Colors
        | ScreenCapabilities.Bold | ScreenCapabilities.Italic | ScreenCapabilities.FixedPitch
        | ScreenCapabilities.FixedGrid;

    public ScreenColor DefaultForeground => ScreenColor.White;

    public ScreenColor DefaultBackground => ScreenColor.Black;

    /// <summary>
    /// [zm 3.8.5.4.1] Anything but a control code; the terminal shows
    /// what its font has and a box for the rest, which is its affair.
    /// </summary>
    public bool CanPrint(char character) => !char.IsControl(character);

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
            Buffer.Print("[MORE]", _blank with { Style = ZMachine.Screen.TextStyle.ReverseVideo });
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

    public void UpdateWindows(WindowedScreenModel model)
    {
        lock (Sync)
        {
            Buffer.UpdateWindows(model);
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

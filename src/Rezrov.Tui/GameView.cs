using System.Drawing;
using System.Text;
using Rezrov.ZMachine.Screen;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;
using TuiColor = Terminal.Gui.Drawing.Color;
using TuiStyle = Terminal.Gui.Drawing.TextStyle;
using ZStyle = Rezrov.ZMachine.Screen.TextStyle;

namespace Rezrov.Tui;

/// <summary>
/// The view that paints the game's screen buffer and hands keys to the
/// interpreter.
/// </summary>
/// <remarks>
/// One view, no widgets: the Z-machine already decided what goes where,
/// so all that is left is to draw the cells with their styles and
/// colors and to put the terminal's cursor where the game's is.
///
/// The terminal's size is not known until the view is first laid out
/// and drawn, and [zm 8.4] the game must be told its screen size before
/// it starts, so the game does not start until then: the first draw
/// with a real viewport calls <see cref="Ready"/>, which builds the
/// screen and the interpreter to that size and hands them back through
/// <see cref="Screen"/> and <see cref="Input"/>. A later change of size
/// is passed on to the screen buffer.
/// </remarks>
public sealed class GameView : View
{
    private readonly KeyMap _keys;
    private readonly Action _quit;

    public GameView(KeyMap keys, Action quit)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(quit);

        _keys = keys;
        _quit = quit;

        CanFocus = true;
        Width = Dim.Fill();
        Height = Dim.Fill();

        KeyDown += OnKeyDown;
        MouseEvent += OnMouse;
    }

    /// <summary>
    /// Called once, with the width and height in characters, when the
    /// view first has a size to give the game.
    /// </summary>
    public Action<int, int>? Ready { get; set; }

    /// <summary>
    /// The screen being painted, once the game has started.
    /// </summary>
    public TerminalScreen? Screen { get; set; }

    /// <summary>Where keys go, once the game has started.</summary>
    public TerminalInput? Input { get; set; }

    // [zm 10.3] A click is input like a key, with its position.
    private void OnMouse(object? sender, Mouse mouse)
    {
        if (Input is not { } input || !(mouse.IsSingleClicked || mouse.IsDoubleClicked) || mouse.Position is not { } position)
        {
            return;
        }

        // [zm op:read_mouse] The primary button is bit 0, the secondary
        // bit 1, and the middle button bit 2, the Windows and X order.
        var buttons = 0;
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) || mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked))
        {
            buttons |= 1;
        }

        if (mouse.Flags.HasFlag(MouseFlags.RightButtonClicked) || mouse.Flags.HasFlag(MouseFlags.RightButtonDoubleClicked))
        {
            buttons |= 2;
        }

        if (mouse.Flags.HasFlag(MouseFlags.MiddleButtonClicked) || mouse.Flags.HasFlag(MouseFlags.MiddleButtonDoubleClicked))
        {
            buttons |= 4;
        }

        input.EnqueueClick(position.X, position.Y, mouse.IsDoubleClicked, buttons == 0 ? 1 : buttons);
        mouse.Handled = true;
    }

    private void OnKeyDown(object? sender, Key key)
    {
        // Ctrl+Q leaves, since no game can ask for a Ctrl combination.
        if (key == Key.Q.WithCtrl)
        {
            _quit();
            key.Handled = true;
            return;
        }

        if (Input is { } input && _keys.ToZscii(key) is { } zscii)
        {
            input.Enqueue(zscii);
            key.Handled = true;
        }
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Viewport.Width;
        var height = Viewport.Height;
        if (width < 1 || height < 1)
        {
            return true;
        }

        if (Screen is null)
        {
            var ready = Ready;
            Ready = null;
            ready?.Invoke(width, height);
            if (Screen is null)
            {
                return true;
            }
        }

        var screen = Screen;
        if (screen.Width != width || screen.Height != height)
        {
            screen.Resize(width, height);
        }

        lock (screen.Sync)
        {
            var buffer = screen.Buffer;
            var run = new StringBuilder();

            for (var row = 0; row < Math.Min(buffer.Height, height); row++)
            {
                Move(0, row);
                TextAttributes? current = null;
                run.Clear();

                for (var column = 0; column < Math.Min(buffer.Width, width); column++)
                {
                    var cell = buffer[row, column];
                    if (current is { } attributes && attributes != cell.Attributes)
                    {
                        SetAttribute(Translate(attributes));
                        AddStr(run.ToString());
                        run.Clear();
                    }

                    // [zm 16] Font 3 is drawn with the Unicode characters
                    // nearest its bitmaps.
                    current = cell.Attributes;
                    run.Append(cell.Attributes.Font == TextAttributes.CharacterGraphicsFont
                        ? CharacterGraphics.ToUnicode(cell.Character)
                        : cell.Character);
                }

                if (current is { } last)
                {
                    SetAttribute(Translate(last));
                    AddStr(run.ToString());
                }
            }

            // [zm op:read_char] Interpreters should display a cursor when
            // waiting for input, and Infocom's showed a block. A steady
            // one, since a blinking block is a distraction in a game
            // that is all reading. The style is set outright, since the
            // toolkit's default depends on the driver.
            var cursor = ViewportToScreen(new Point(Math.Min(buffer.CursorColumn, buffer.Width - 1), buffer.CursorRow));
            Cursor = new Cursor { Position = cursor, Style = buffer.CursorVisible ? CursorStyle.SteadyBlock : CursorStyle.Hidden };
        }

        SetCursorNeedsUpdate();
        return true;
    }

    /// <summary>
    /// The model's attributes as the terminal's: [zm 8.3.1] the color
    /// numbers to the sixteen terminal colors, and [zm 8.7.1] the styles
    /// to the terminal's, with fixed pitch meaning nothing on a screen
    /// that is nothing else.
    /// </summary>
    private static TuiAttribute Translate(TextAttributes attributes)
    {
        var style = TuiStyle.None;
        if (attributes.Style.HasFlag(ZStyle.Bold))
        {
            style |= TuiStyle.Bold;
        }

        if (attributes.Style.HasFlag(ZStyle.Italic))
        {
            style |= TuiStyle.Italic;
        }

        if (attributes.Style.HasFlag(ZStyle.ReverseVideo))
        {
            style |= TuiStyle.Reverse;
        }

        return new TuiAttribute(Translate(attributes.Foreground), Translate(attributes.Background), style);
    }

    private static TuiColor Translate(ScreenColor color) => color switch
    {
        ScreenColor.Black => TuiColor.Black,
        ScreenColor.Red => TuiColor.Red,
        ScreenColor.Green => TuiColor.Green,
        ScreenColor.Yellow => TuiColor.Yellow,
        ScreenColor.Blue => TuiColor.Blue,
        ScreenColor.Magenta => TuiColor.Magenta,
        ScreenColor.Cyan => TuiColor.Cyan,
        ScreenColor.White => TuiColor.White,
        ScreenColor.LightGray => TuiColor.Gray,
        ScreenColor.MediumGray => TuiColor.DarkGray,
        ScreenColor.DarkGray => TuiColor.DarkGray,
        _ => TuiColor.White,
    };
}

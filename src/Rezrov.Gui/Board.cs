using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Rezrov.AaMachine;
using Rezrov.Core.Graphics;
using Rezrov.Glulx.Glk;
using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Text;
using ZCell = Rezrov.ZMachine.Screen.Cell;
using ZStyle = Rezrov.ZMachine.Screen.TextStyle;

namespace Rezrov.Gui;

/// <summary>
/// The one control the window holds: it paints every Glk window and
/// takes every key the player presses.
/// </summary>
/// <remarks>
/// The library lays the window tree out in character cells and this
/// turns cells into pixels, so the layout the game asked for and the
/// picture on the screen are the same arrangement measured two ways.
/// Nothing here decides anything: the text has already been wrapped by
/// <see cref="BufferText"/> and a text grid's cells belong to the
/// library, so painting is only a matter of putting each piece where it
/// was worked out to go.
/// </remarks>
internal sealed class Board : Control
{
    private readonly Dictionary<uint, ImmutableSolidColorBrush> _colors = [];
    private readonly Dictionary<GlkWindow, Painting> _canvases = [];

    // [glk #graphics_textbuf] The pictures a game puts among its text,
    // each turned into a bitmap once. The library decodes each picture
    // once and hands the same pixels over every time it is drawn, so
    // the pixels themselves are the key.
    private readonly Dictionary<Pixels, WriteableBitmap> _inline =
        new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<uint, ImmutableSolidColorBrush> _washes = [];
    private readonly HashSet<GlkWindow> _seen = [];
    private Glyphs _glyphs;
    private GuiGlkDisplay? _display;
    private BufferedScreen? _screen;
    private GuiAaDisplay? _page;

    /// <param name="glyphs">The fonts to draw with.</param>
    /// <param name="smoothing">
    /// How the glyphs themselves are rasterized, which is the one thing
    /// about the look of the text that the font cannot settle.
    /// </param>
    public Board(Glyphs glyphs, TextRenderingMode smoothing)
    {
        ArgumentNullException.ThrowIfNull(glyphs);

        _glyphs = glyphs;
        Focusable = true;

        // Text is smoothed by whatever the platform prefers unless it
        // is told otherwise, and what the software renderer prefers is
        // gray antialiasing, which leaves a serif face at reading size
        // looking thinner and paler than every other window on the
        // machine. Saying subpixel asks for the same rendering the rest
        // of the desktop uses; a display that cannot do it falls back
        // to gray, which is where it started.
        TextOptions.SetTextRenderingMode(this, smoothing);

        // The cells of a screen are filled as rectangles that share
        // their edges. Antialiasing those edges leaves a pale hairline
        // between every pair of them, which at a fractional display
        // scale draws a cross-hatch over the whole window. Text keeps
        // its own smoothing; this is about the geometry only.
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

        // [glk #graphics_textbuf] A picture among the text is drawn at
        // whatever size the image rules worked out, which is rarely the
        // size it was stored at, so it is worth sampling well.
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    /// <summary>
    /// How much blank a window is given by default, in pixels.
    /// </summary>
    /// <remarks>
    /// About half a character at the ordinary text size, which is
    /// enough to stop the text touching the frame and not enough to
    /// look like a border.
    /// </remarks>
    public const double OrdinaryPadding = 6;

    /// <summary>
    /// Blank kept between the game and the edges of the window.
    /// </summary>
    /// <remarks>
    /// Text set hard against the frame of a window is uncomfortable to
    /// read and looks like an oversight, which is why every other
    /// program that shows prose leaves a margin. The padding takes the
    /// game's own background color rather than the window's, so what
    /// it looks like is a page with a margin rather than a screen that
    /// does not quite fill its frame.
    ///
    /// The game is not told about it: what it is given is the size
    /// inside the padding, so a game that lays itself out to the
    /// screen lays itself out to what it can actually use.
    /// </remarks>
    public double Padding { get; init; }

    /// <summary>The part of the window the game is drawn in.</summary>
    public Size Sheet => new(
        Math.Max(Bounds.Width - (Padding * 2), 1),
        Math.Max(Bounds.Height - (Padding * 2), 1));

    /// <summary>
    /// The whole window, in the coordinates the game is drawn in, so
    /// that a background still reaches the edges through the padding.
    /// </summary>
    private Rect Whole => new(-Padding, -Padding, Bounds.Width, Bounds.Height);

    /// <summary>
    /// The display to paint, once the game has been started on it.
    /// </summary>
    public GuiGlkDisplay? Display
    {
        get => _display;
        set
        {
            _display = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// The Z-machine screen to paint, for a game of that machine. A
    /// frontend shows one machine or the other, never both, so whichever
    /// of this and <see cref="Display"/> was set is what is drawn.
    /// </summary>
    public BufferedScreen? Screen
    {
        get => _screen;
        set
        {
            _screen = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// [aam output] The Aa-machine page to paint, for a game of that
    /// machine. A frontend shows one machine of the three, so whichever
    /// of these was set is what is drawn.
    /// </summary>
    public GuiAaDisplay? Page
    {
        get => _page;
        set
        {
            _page = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// [zm 10] Where the Z-machine's keys go, for a game of that
    /// machine.
    /// </summary>
    public BufferedInput? Keys { get; set; }

    /// <summary>
    /// [zm 8.8.6] The pictures a Version 6 game draws.
    /// </summary>
    public GuiPictures? Pictures { get; set; }

    /// <summary>
    /// [infocom pictures] The screen a Version 6 game is being played
    /// on, in units, when that is a fixed one rather than the window's
    /// own size. Null for everything else.
    /// </summary>
    /// <remarks>
    /// Infocom's Version 6 games compute every coordinate for the
    /// screen they were written against, so they are given that screen
    /// and the whole of it is scaled into the window. Handing one a
    /// window-sized screen instead leaves its border art in a corner
    /// and runs its menu labels into each other.
    /// </remarks>
    public (int Width, int Height)? UnitScreen { get; set; }

    /// <summary>
    /// [infocom pictures] The fonts to paint a fixed unit screen with,
    /// which are smaller than the reading fonts because the whole
    /// screen is scaled up afterwards. Setting this replaces the fonts
    /// for good, and only the Version 6 path ever does.
    /// </summary>
    public Glyphs UnitGlyphs
    {
        set => _glyphs = value;
    }

    /// <summary>
    /// [aam story] The faces an Aa-machine story is set in, which it
    /// names for itself rather than choosing among the frontend's.
    /// </summary>
    public GuiAaGlyphs? AaGlyphs { get; set; }

    /// <summary>
    /// A title picture the interpreter is showing before the game
    /// begins, or zero when the game itself has the window.
    /// </summary>
    /// <remarks>
    /// This is set from the thread running the game and read here on
    /// the toolkit's, which is why it is a number and not a bitmap:
    /// reading a whole word is not something the two threads can catch
    /// halfway, and the picture is decoded on this side.
    /// </remarks>
    public int Title { get; set; }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A title screen covers the window, since the game behind it
        // has not started yet.
        if (Title != 0 && Pictures?.Bitmap(Title) is { } title)
        {
            PaintTitle(context, title);
            return;
        }

        // Everything below is drawn inside the padding. The one thing
        // above is the title screen, which is a cover rather than part
        // of the game and fills the window.
        using var margin = context.PushTransform(Matrix.CreateTranslation(Padding, Padding));

        if (Page is { } page)
        {
            // The machine writes on its own thread, so the page is
            // held still while it is laid out and painted.
            lock (page.Sync)
            {
                PaintPage(context, page);
            }

            return;
        }

        if (Screen is { } screen)
        {
            // [zm 8.4] A window is rarely a whole number of characters
            // across and down, so a strip is left over at the right and
            // the bottom. It is the screen's own color rather than the
            // page behind it, or the game appears to be sitting on a
            // sheet of paper a little too small for it.
            lock (screen.Sync)
            {
                context.FillRectangle(Brush(screen.DefaultBackground), Whole);

                if (Fitted() is { } fit)
                {
                    using (context.PushTransform(
                        Matrix.CreateScale(fit.Scale, fit.Scale)
                        * Matrix.CreateTranslation(fit.Across, fit.Down)))
                    {
                        PaintScreen(context, screen);
                    }
                }
                else
                {
                    PaintScreen(context, screen);
                }
            }

            return;
        }

        context.FillRectangle(Brush(GlkLook.Paper), Whole);

        if (Display is not { Root: { } root })
        {
            return;
        }

        lock (Display.Sync)
        {
            _seen.Clear();
            Paint(context, root);
            Forget();
        }
    }

    /// <summary>
    /// Lets go of the bitmap of any graphics window that has been
    /// closed, which would otherwise be held for as long as the game
    /// runs.
    /// </summary>
    private void Forget()
    {
        foreach (var window in _canvases.Keys.Where(w => !_seen.Contains(w)).ToList())
        {
            _canvases[window].Bitmap.Dispose();
            _canvases.Remove(window);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // Pasted text is typed for the player a character at a time, so
        // a command copied from a walkthrough runs on arrival. The
        // modifier is the one the machine uses for copying and pasting
        // everywhere else on it.
        if (e.Key == Key.V && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            _ = Paste();
            e.Handled = true;
            return;
        }

        if (Page is { } page && GuiKeyMap.ToAa(e.Key) is { } character)
        {
            page.Enqueue(character);
            e.Handled = true;
        }
        else if (Keys is { } keys && GuiKeyMap.ToZscii(e.Key) is { } zscii)
        {
            keys.Enqueue(zscii);
            e.Handled = true;
        }
        else if (Display is { } display && GuiKeyMap.ToGlk(e.Key) is { } key)
        {
            display.Key(key);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Text is not { Length: > 0 } text)
        {
            base.OnTextInput(e);
            return;
        }

        if (Page is { } page)
        {
            foreach (var character in AaKeys.Pasted(text))
            {
                page.Enqueue(character);
            }

            e.Handled = true;
        }
        else if (Keys is { } keys)
        {
            // [zm 3.8] Only what ZSCII has a code for can be typed at a
            // Z-machine game; anything else is not a key it knows.
            foreach (var character in text)
            {
                if (Zscii.FromUnicode(character, UnicodeTranslationTable.Default) is { } zscii)
                {
                    keys.Enqueue(zscii);
                }
            }

            e.Handled = true;
        }
        else if (Display is { } display)
        {
            display.Typed(text);
            e.Handled = true;
        }

        base.OnTextInput(e);
    }

    /// <summary>
    /// Types whatever is on the clipboard into the game.
    /// </summary>
    /// <remarks>
    /// The clipboard is only read on the toolkit's thread and only after
    /// waiting, so this is left to finish on its own while the key press
    /// that asked for it returns. The text lands on the same queue as
    /// anything typed, so a line ending in it ends the line exactly as
    /// pressing enter would.
    /// </remarks>
    private async Task Paste()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (Keys is { } keys)
        {
            foreach (var character in text)
            {
                // [zm 3.8] A line ending is the return key however the
                // clipboard spells it, and a carriage return before a
                // newline would otherwise end the line twice.
                if (character == '\r')
                {
                    continue;
                }

                var zscii = character == '\n'
                    ? Zscii.Newline
                    : Zscii.FromUnicode(character, UnicodeTranslationTable.Default);

                if (zscii is { } code)
                {
                    keys.Enqueue(code);
                }
            }

            return;
        }

        Display?.Typed(text);
    }

    /// <summary>
    /// [glk #mouse_events] and [glk #link_events] Touching a window,
    /// which for a window waiting on a link means selecting the link
    /// under the pointer.
    /// </summary>
    /// <remarks>
    /// The specification asks that a player be told to touch a window
    /// rather than to click, double-click, or control-click it, since
    /// every library chooses differently. An ordinary press is the
    /// choice here, this being a frontend with no text selection of
    /// its own to get in the way.
    /// </remarks>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // A press puts the keyboard back in the board, which is where
        // the game is waiting for it.
        Focus();

        if (Page is { } page)
        {
            var at = e.GetPosition(this);

            lock (page.Sync)
            {
                if (Clicked(page, at) is { } link)
            {
                    // [aam output] A link stands for a command rather
                    // than for a letter, so it goes on the same queue
                    // as the keys but as something a key can never be.
                    page.Enqueue(-link);
                    e.Handled = true;
                }
            }
        }
        else if (Screen is { } clicked && Keys is { } keys)
        {
            // [zm 10.3] A Version 6 game reads the mouse, and the
            // terminal has always reported clicks to it. Here the
            // screen may be drawn scaled, so the pointer is taken back
            // out of the window and into the game's own cells before
            // anyone hears about it.
            var at = Unscaled(e.GetPosition(this));
            var column = (int)(at.X / _glyphs.CellWidth);
            var row = (int)(at.Y / _glyphs.CellHeight);

            if (column >= 0 && row >= 0 && column < clicked.Width && row < clicked.Height)
            {
                var properties = e.GetCurrentPoint(this).Properties;

                // [zm op:read_mouse] The primary button is bit 0, the
                // secondary bit 1, and the middle button bit 2, which
                // is the Windows and X order and what the terminal
                // reports.
                var buttons = 0;
                if (properties.IsLeftButtonPressed) { buttons |= 1; }
                if (properties.IsRightButtonPressed) { buttons |= 2; }
                if (properties.IsMiddleButtonPressed) { buttons |= 4; }

                keys.EnqueueClick(column, row, e.ClickCount >= 2, buttons == 0 ? 1 : buttons);
                e.Handled = true;
            }
        }
        else if (Display is { Root: { } root } display)
        {
            var at = e.GetPosition(this);

            lock (display.Sync)
            {
                if (WindowAt(root, at) is { } window && Touched(window, at) is { } input)
                {
                    display.Point(input);
                    e.Handled = true;
                }
            }
        }

        base.OnPointerPressed(e);
    }

    /// <summary>
    /// What touching a window at a point means to the game, or nothing
    /// where the window was not waiting to be touched.
    /// </summary>
    /// <remarks>
    /// [glk #link_events] A link goes before a touch. The
    /// specification says a game should avoid asking for both at once
    /// precisely because a library has no intuitive way to tell them
    /// apart; where one does both, the link is the more particular of
    /// the two and wins where there is a link under the pointer.
    /// </remarks>
    private GlkInput? Touched(GlkWindow window, Point at)
    {
        var place = Place(window);
        var x = at.X - place.X;
        var y = at.Y - place.Y;

        if (window.HyperlinkRequest && Selected(window, x, y, place) is { } link && link != 0)
        {
            return GlkInput.LinkSelected(window, link);
        }

        if (!window.MouseRequest)
        {
            return null;
        }

        // [glk #mouse_events] A text grid answers in characters and a
        // graphics window in pixels, each counted from its own top left
        // corner. The strip a window was stretched over is past its
        // last cell, so a touch there belongs to the last one.
        return window switch
        {
            TextGridWindow grid => GlkInput.MouseClick(
                window,
                (uint)Math.Clamp((int)(x / _glyphs.CellWidth), 0, Math.Max(grid.Width - 1, 0)),
                (uint)Math.Clamp((int)(y / _glyphs.CellHeight), 0, Math.Max(grid.Height - 1, 0))),
            GraphicsWindow canvas => GlkInput.MouseClick(
                window,
                (uint)Math.Clamp((int)x, 0, Math.Max(canvas.PixelWidth - 1, 0)),
                (uint)Math.Clamp((int)y, 0, Math.Max(canvas.PixelHeight - 1, 0))),
            _ => null,
        };
    }

    /// <summary>
    /// [glk #link_events] The link under a point in a window, or zero
    /// where there is none there.
    /// </summary>
    private uint? Selected(GlkWindow window, double x, double y, Rect place) => window switch
    {
        TextGridWindow grid when x >= 0 && y >= 0 => Inside(grid, x, y),
        { Type: WindowType.TextBuffer } => Display!.Text(window).LinkAt(x, y, place.Width, place.Height),
        _ => null,
    };

    private uint? Inside(TextGridWindow grid, double x, double y)
    {
        var column = (int)(x / _glyphs.CellWidth);
        var row = (int)(y / _glyphs.CellHeight);

        return column < grid.Width && row < grid.Height ? grid.LinkAt(column, row) : null;
    }

    /// <summary>
    /// [glk #window_textbuf] The wheel scrolls the text buffer under the
    /// pointer, which is the only way back to what has gone off the top.
    /// </summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (Page is { } page)
        {
            lock (page.Sync)
            {
                // Three lines to a notch of the wheel, as everywhere
                // else that text scrolls.
                page.Main.ScrollBy(
                    e.Delta.Y * _glyphs.CellHeight * 3,
                    page.Column,
                    page.MainHeight);
            }

            InvalidateVisual();
            e.Handled = true;
            base.OnPointerWheelChanged(e);
            return;
        }

        if (Display is { Root: { } root } display)
        {
            lock (display.Sync)
            {
                if (WindowAt(root, e.GetPosition(this)) is { Type: WindowType.TextBuffer } window)
                {
                    // Three lines to a notch of the wheel, which is what
                    // everything else that scrolls text does.
                    var place = Place(window);
                    var text = display.Text(window);
                    text.ScrollBy(e.Delta.Y * _glyphs.LineHeight(GlkStyle.Normal) * 3, place.Width, place.Height);
                    e.Handled = true;
                }
            }

            InvalidateVisual();
        }

        base.OnPointerWheelChanged(e);
    }

    /// <summary>
    /// [infocom pictures] A point in the window, taken back into the
    /// coordinates the screen is drawn in: past the padding, and past
    /// the scaling too where a fixed screen is being fitted.
    /// </summary>
    /// <remarks>
    /// The one way back. Everything drawn goes out through the padding
    /// and, for a fixed screen, through <see cref="Fitted"/>, so a
    /// pointer that did not come back through both would land a little
    /// off the thing it was aimed at, which is the kind of fault that
    /// looks almost right and survives for years.
    /// </remarks>
    private Point Unscaled(Point at)
    {
        // Undone in the order it was done. Painting goes out through
        // the padding and then, for a fixed screen, through the fit,
        // so coming back means the fit first and the padding last.
        var inside = new Point(at.X - Padding, at.Y - Padding);

        if (Fitted() is not { } fit)
        {
            return inside;
        }

        var (x, y) = fit.Unscaled(inside.X, inside.Y);
        return new Point(x, y);
    }

    /// <summary>
    /// [infocom pictures] How a fixed screen sits in this window, or
    /// null when the screen is the window's own size, which is every
    /// game but an Infocom Version 6 one.
    /// </summary>
    /// <remarks>
    /// Measured inside the padding, and holding none of it. The
    /// padding is a transform of its own that everything is drawn
    /// through, this one included, so a fit that carried the padding
    /// as well would apply it twice on the way out and once on the
    /// way back.
    /// </remarks>
    private ScreenFit? Fitted() =>
        UnitScreen is { } units ? ScreenFit.Of((Sheet.Width, Sheet.Height), units) : null;

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // What the game is given is the size inside the padding, not
        // the size of the window, so it lays itself out to what it can
        // actually draw on.
        var inside = new Size(
            Math.Max(e.NewSize.Width - (Padding * 2), 1),
            Math.Max(e.NewSize.Height - (Padding * 2), 1));

        Display?.Resize(inside.Width, inside.Height);
        Page?.Resize(inside.Width, inside.Height);

        // [infocom pictures] A fixed screen keeps its size whatever the
        // window does, because the game laid itself out for that screen
        // and re-gridding it would undo the whole point. The window
        // only changes how far it is scaled.
        if (UnitScreen is null)
        {
            Screen?.Resize(
                Math.Max((int)(inside.Width / _glyphs.CellWidth), 1),
                Math.Max((int)(inside.Height / _glyphs.CellHeight), 1));
        }

        base.OnSizeChanged(e);
    }

    /// <summary>
    /// Where a window sits on the screen. The library lays the tree out
    /// in character cells and this is the same rectangle in pixels.
    /// </summary>
    /// <remarks>
    /// [glk #window_arrangement] The layout is a whole number of cells
    /// across and down, and the window it is drawn in is rarely a whole
    /// number of them, so a strip is left over at the right and at the
    /// bottom. The windows along those two edges are stretched over it,
    /// which fills it with whatever the window beside it is filled
    /// with rather than leaving the page behind showing through.
    ///
    /// Only the rectangle is stretched, not the layout: the game is
    /// still told the size it was given, and a text grid's cells still
    /// fall where the library put them. What changes is where the
    /// window's own background reaches, and which window a click in the
    /// strip belongs to.
    /// </remarks>
    private Rect Place(GlkWindow window)
    {
        var left = window.Left * _glyphs.CellWidth;
        var top = window.Top * _glyphs.CellHeight;
        var right = (window.Left + window.Width) * _glyphs.CellWidth;
        var bottom = (window.Top + window.Height) * _glyphs.CellHeight;

        if (Display is { } display)
        {
            if (window.Left + window.Width >= display.Width)
            {
                right = Math.Max(right, Sheet.Width);
            }

            if (window.Top + window.Height >= display.Height)
            {
                bottom = Math.Max(bottom, Sheet.Height);
            }
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Which window a point on the screen falls in, or null for none.
    /// A pair window is only the space its children share, so the answer
    /// is always one of the windows that shows something.
    /// </summary>
    private GlkWindow? WindowAt(GlkWindow window, Point at)
    {
        if (window is PairWindow pair)
        {
            return WindowAt(pair.First, at) ?? WindowAt(pair.Second, at);
        }

        return Place(window).Contains(at) ? window : null;
    }

    private void Paint(DrawingContext context, GlkWindow window)
    {
        _seen.Add(window);

        if (window is PairWindow pair)
        {
            Paint(context, pair.First);
            Paint(context, pair.Second);
            return;
        }

        var place = Place(window);

        switch (window.Type)
        {
            case WindowType.TextBuffer:
                PaintBuffer(context, window, place);
                break;
            case WindowType.TextGrid when window is TextGridWindow grid:
                PaintGrid(context, grid, place);
                break;
            case WindowType.Graphics when window is GraphicsWindow canvas:
                PaintCanvas(context, canvas, place);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// [aam output] An Aa-machine story: the status area across the
    /// top, a line under it, and the main text below, all in a column
    /// no wider than a comfortable measure and set in the middle of
    /// the window.
    /// </summary>
    /// <remarks>
    /// Nothing is decided here. The page has already been laid out
    /// into a list of boxes and a list of lines, both measured from
    /// the top of the page, so painting is a matter of putting each
    /// one where it was worked out to go.
    ///
    /// The main text is shown from the bottom up, so that a story
    /// shorter than the window sits at the top of it and a longer one
    /// has its newest text against the bottom, which is where a player
    /// is reading.
    /// </remarks>
    private void PaintPage(DrawingContext context, GuiAaDisplay page)
    {
        context.FillRectangle(Washed(page.Background), Whole);

        var width = page.Column;
        var left = page.Left;
        var status = page.StatusHeight;

        if (status > 0)
        {
            using (context.PushClip(new Rect(left, 0, width, status)))
            {
                PaintPart(context, page, page.Status, left, 0, width, status, fromBottom: false);
            }

            // [aam output] The line that sets the status area off from
            // the text, which the reference interpreter draws in the
            // color of the text itself.
            context.FillRectangle(Washed(AaTheme.Ink), new Rect(left, status, width, page.Rule));
        }

        var top = status + page.Rule;

        using var clip = context.PushClip(new Rect(left, top, width, Math.Max(Sheet.Height - top, 0)));

        PaintPart(context, page, page.Main, left, top, width, page.MainHeight, fromBottom: true);
    }

    /// <summary>
    /// One of the two pages, painted into the strip of the window it
    /// was given.
    /// </summary>
    private void PaintPart(
        DrawingContext context,
        GuiAaDisplay page,
        AaText text,
        double left,
        double top,
        double width,
        double height,
        bool fromBottom)
    {
        var laid = text.Lay(width);
        var offset = top + (fromBottom ? text.Offset(width, height) : 0);

        // The boxes a style sheet asked for go behind everything, in
        // the order they were opened, so an inner one paints over the
        // one it is inside.
        foreach (var frame in laid.Frames)
        {
            PaintFrame(context, frame, left, offset);
        }

        foreach (var line in laid.Lines)
        {
            var y = offset + line.Top;

            if (y + line.Height < top || y > top + height)
            {
                continue;
            }

            // A span printed on a color of its own is painted behind
            // its own pieces and nothing else, so a run of it shows as
            // a band across exactly the words it covers.
            foreach (var piece in line.Pieces)
            {
                if (piece.Look.HasPaper)
                {
                    context.FillRectangle(
                        Washed(piece.Look.Paper),
                        new Rect(left + piece.Left, y, piece.Width, line.Height));
                }
            }

            foreach (var inset in line.Pictures)
            {
                if (Inline(inset.Picture) is { } bitmap)
                {
                    context.DrawImage(
                        bitmap,
                        new Rect(bitmap.Size),
                        new Rect(left + inset.Left, y + inset.Top, inset.Width, inset.Height));
                }
            }

            foreach (var piece in line.Pieces)
            {
                PaintPiece(context, page, piece, left, y + line.Baseline);
            }
        }
    }

    /// <summary>
    /// One piece of a line, standing on the line the text stands on
    /// rather than hanging from the top of it, so that a heading and
    /// the prose beside it sit together.
    /// </summary>
    /// <remarks>
    /// [aam story] A class that asks for its letters to stand apart is
    /// drawn a letter at a time, since a face has no such setting and
    /// the layout has already measured the line as though it had. The
    /// one title in the corpus that asks for it is a handful of
    /// letters once a game.
    /// </remarks>
    private void PaintPiece(DrawingContext context, GuiAaDisplay page, AaPiece piece, double left, double baseline)
    {
        if (piece.Words == " ")
        {
            return;
        }

        var glyphs = AaGlyphs!;
        var live = piece.Link != 0 && page.IsLive(piece.Link);
        var top = baseline - glyphs.Ascent(piece.Look);

        if (piece.Look.Spacing == 0)
        {
            context.DrawText(Drawn(piece.Words, piece.Look, live), new Point(left + piece.Left, top));
            return;
        }

        var pen = left + piece.Left;

        foreach (var letter in piece.Words)
        {
            var one = letter.ToString();

            context.DrawText(Drawn(one, piece.Look, live), new Point(pen, top));
            pen += glyphs.Width(one, piece.Look) + piece.Look.Spacing;
        }
    }

    /// <summary>
    /// [aam story] A box a style class asked for: the color behind it
    /// first, then the line around it, with the corners rounded as far
    /// as it asked.
    /// </summary>
    private void PaintFrame(DrawingContext context, AaFrame frame, double left, double top)
    {
        var place = new Rect(left + frame.Left, top + frame.Top, frame.Width, frame.Height);

        if ((frame.Background >> 24) != 0)
        {
            context.DrawRectangle(Washed(frame.Background), null, place, frame.Radius, frame.Radius);
        }

        if (frame.Border > 0 && (frame.BorderColor >> 24) != 0)
        {
            // A line is drawn along the middle of its own thickness, so
            // the rectangle is brought in by half of it to leave the
            // outside of the line where the box said its edge was.
            context.DrawRectangle(
                null,
                new Pen(Washed(frame.BorderColor), frame.Border),
                place.Deflate(frame.Border / 2),
                frame.Radius,
                frame.Radius);
        }
    }

    /// <summary>
    /// A piece of an Aa-machine story's text ready to draw, in the
    /// face and color its style class calls for.
    /// </summary>
    /// <remarks>
    /// [aam output] A piece that is part of a link that can still be
    /// used is drawn in the color links are drawn in and underlined,
    /// whatever its class said, since a link has to look like one. A
    /// link the story has since retired is drawn as ordinary text,
    /// which is what retiring it means.
    /// </remarks>
    private FormattedText Drawn(string words, AaLook look, bool live)
    {
        var formatted = new FormattedText(
            words,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            AaGlyphs!.Face(look),
            look.Size,
            Washed(live ? AaTheme.Link : look.Ink));

        if (live)
        {
            formatted.SetTextDecorations(TextDecorations.Underline);
        }

        return formatted;
    }

    /// <summary>
    /// [aam output] The link under a point in the window, or null where
    /// there is none there.
    /// </summary>
    private static int? Clicked(GuiAaDisplay page, Point at)
    {
        var width = page.Column;
        var left = page.Left;
        var status = page.StatusHeight;
        var top = status + page.Rule;

        var link = at.Y < status
            ? page.Status.LinkAt(at.X - left, at.Y, width, status)
            : page.Main.LinkAt(at.X - left, at.Y - top, width, page.MainHeight);

        return link != 0 && page.IsLive(link) ? link : null;
    }

    /// <summary>
    /// [aam story] A color with how much of it there is honored, since
    /// an Aa-machine style sheet washes a color over a box as often as
    /// it fills one. The other two machines have no such thing, and
    /// their colors go through the plain brush above.
    /// </summary>
    private ImmutableSolidColorBrush Washed(uint color)
    {
        if (!_washes.TryGetValue(color, out var brush))
        {
            brush = new ImmutableSolidColorBrush(Color.FromArgb(
                (byte)(color >> 24),
                (byte)(color >> 16),
                (byte)(color >> 8),
                (byte)color));

            _washes[color] = brush;
        }

        return brush;
    }

    private void PaintBuffer(DrawingContext context, GlkWindow window, Rect place)
    {
        var fonts = Display!.Glyphs(window);
        var text = Display!.Text(window);
        var lines = text.Lines(place.Width);

        // [glk #stream_style_hints] The page itself is whatever the
        // ordinary style is printed on, which a game may have asked to
        // be something other than white.
        var paper = fonts.Look(GlkStyle.Normal).Colors.Paper;
        context.FillRectangle(Brush(paper), place);

        using var clip = context.PushClip(place);

        var top = place.Top + text.Bottom(place.Width, place.Height);

        for (var i = lines.Count - 1; i >= 0 && top > place.Top - lines[i].Height; i--)
        {
            var line = lines[i];
            top -= line.Height;

            // [glk #stream_style_hints] A style printed on a color of
            // its own, or a reversed one, is painted behind its own
            // pieces and nothing else, so a run of it shows as a band
            // across exactly the words it covers. The spaces between
            // words are pieces too, so the band has no gaps in it.
            foreach (var piece in line.Pieces)
            {
                var behind = fonts.Look(piece.Style).Colors.Paper;
                if (behind != paper)
                {
                    context.FillRectangle(
                        Brush(behind),
                        new Rect(place.X + piece.Left, top, piece.Width, line.Height));
                }
            }

            foreach (var inset in line.Images)
            {
                if (Inline(inset.Picture) is { } bitmap)
                {
                    context.DrawImage(
                        bitmap,
                        new Rect(bitmap.Size),
                        new Rect(place.X + inset.Left, top + inset.Top, inset.Width, inset.Height));
                }
            }

            foreach (var piece in line.Pieces)
            {
                if (piece.Text == " ")
                {
                    continue;
                }

                var look = fonts.Look(piece.Style);
                var formatted = Written(piece.Text, look, piece.Link);

                // Pieces are placed on the line's baseline rather than
                // hung from its top, so that a heading and the prose
                // beside it sit on the same line.
                var above = line.Baseline - fonts.Baseline(piece.Style);
                context.DrawText(formatted, new Point(place.X + piece.Left, top + above));
            }
        }
    }

    /// <summary>
    /// A piece of text ready to draw, in the color and face its style
    /// calls for.
    /// </summary>
    /// <remarks>
    /// [glk #link_creating] A piece that belongs to a link is drawn in
    /// the color links are drawn in and underlined, whatever the style
    /// said, since the specification asks that links be shown in some
    /// distinctive way whether or not the game has asked for link
    /// input at all.
    /// </remarks>
    private FormattedText Written(string text, GlkAppearance look, uint link)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _glyphs.Face(look),
            look.Size,
            Brush(link == 0 ? look.Colors.Ink : GlkLook.Linked));

        if (link != 0)
        {
            formatted.SetTextDecorations(TextDecorations.Underline);
        }

        return formatted;
    }

    /// <summary>
    /// [glk #graphics_textbuf] A picture from the run of a text
    /// buffer's text, as a bitmap, kept from one paint to the next.
    /// </summary>
    private WriteableBitmap? Inline(Pixels picture)
    {
        if (_inline.TryGetValue(picture, out var known))
        {
            return known;
        }

        if (GuiPictures.ToBitmap(picture) is not { } bitmap)
        {
            return null;
        }

        _inline[picture] = bitmap;
        return bitmap;
    }

    /// <summary>
    /// [glk #window_graphics] A graphics window is the canvas the
    /// library has been painting on, shown a pixel for a pixel.
    /// </summary>
    /// <remarks>
    /// The canvas is painted on the interpreter's thread and read here
    /// on the toolkit's, so the pixels are taken hold of once and used
    /// as they were at that moment. [glk #window_graphics] A resize
    /// makes a whole new set of them rather than changing the ones in
    /// hand, so what is drawn is always a canvas of one consistent
    /// size, at worst one that has since been painted on again. The
    /// paint that came after it brings its own repaint with it.
    ///
    /// The bitmap is kept between paints and filled again only when the
    /// canvas says it has changed, since a game that fills a rectangle
    /// asks for a repaint each time and copying a megabyte for each
    /// would be felt.
    /// </remarks>
    private void PaintCanvas(DrawingContext context, GraphicsWindow window, Rect place)
    {
        var pixels = window.Canvas.Pixels;
        var changes = window.Canvas.Changes;

        if (pixels.Width <= 0 || pixels.Height <= 0)
        {
            return;
        }

        if (!_canvases.TryGetValue(window, out var painting)
            || painting.Bitmap.PixelSize.Width != pixels.Width
            || painting.Bitmap.PixelSize.Height != pixels.Height)
        {
            painting?.Bitmap.Dispose();

            // [glk #graphics_testing] Red, green, blue, and alpha, one
            // byte each and the color not multiplied by the alpha,
            // which is the shape the decoders and the canvas already
            // keep pixels in, so the copy below is a copy and nothing
            // more.
            painting = new Painting(
                new WriteableBitmap(
                    new PixelSize(pixels.Width, pixels.Height),
                    new Vector(96, 96),
                    PixelFormat.Rgba8888,
                    AlphaFormat.Unpremul),
                changes - 1);

            _canvases[window] = painting;
        }

        if (painting.Changes != changes)
        {
            using (var locked = painting.Bitmap.Lock())
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    Marshal.Copy(
                        pixels.Rgba,
                        y * pixels.Width * 4,
                        locked.Address + (y * locked.RowBytes),
                        pixels.Width * 4);
                }
            }

            _canvases[window] = painting with { Changes = changes };
        }

        // [glk #window_graphics] The canvas is exactly as many pixels
        // as the cells the window was given, so the strip the window
        // was stretched over is beyond it. It is the canvas's own
        // background color, which is what the specification says a
        // resize leaves behind, rather than the canvas stretched to
        // cover it.
        context.FillRectangle(Brush(window.Canvas.Background), place);

        context.DrawImage(
            painting.Bitmap,
            new Rect(0, 0, pixels.Width, pixels.Height),
            new Rect(place.X, place.Y, pixels.Width, pixels.Height));
    }

    // [glk #window_textgrid] A grid's cells belong to the library, so
    // this reads them out and draws each one where its row and column
    // put it.
    private void PaintGrid(DrawingContext context, TextGridWindow grid, Rect place)
    {
        var fonts = Display!.Glyphs(grid);
        var paper = fonts.Look(GlkStyle.Normal).Colors.Paper;

        context.FillRectangle(Brush(paper), place);

        using var clip = context.PushClip(place);

        for (var y = 0; y < grid.Height; y++)
        {
            PaintRow(context, grid, fonts, place, y, paper);
        }
    }

    /// <summary>
    /// One row of a text grid: the backgrounds that are not the page's
    /// own, and then the characters.
    /// </summary>
    /// <remarks>
    /// [glk #window_textgrid] The backgrounds go down in runs of cells
    /// that share one, since a game that reverses a whole status line
    /// would otherwise have eighty rectangles filled where one would
    /// do, and the grid is repainted on every character printed.
    /// A blank cell is painted too: a reversed style shows as a band
    /// whether or not there is a letter standing on it.
    /// </remarks>
    private void PaintRow(DrawingContext context, TextGridWindow grid, IGlyphs fonts, Rect place, int y, uint paper)
    {
        if (grid.Width <= 0)
        {
            return;
        }

        var from = 0;
        var behind = fonts.Look(grid.StyleAt(0, y)).Colors.Paper;

        for (var x = 1; x <= grid.Width; x++)
        {
            var next = x < grid.Width ? fonts.Look(grid.StyleAt(x, y)).Colors.Paper : (uint?)null;
            if (next == behind)
            {
                continue;
            }

            if (behind != paper)
            {
                context.FillRectangle(
                    Brush(behind),
                    new Rect(
                        place.X + (from * _glyphs.CellWidth),
                        place.Y + (y * _glyphs.CellHeight),
                        (x - from) * _glyphs.CellWidth,
                        _glyphs.CellHeight));
            }

            from = x;
            behind = next ?? behind;
        }

        var row = grid.Row(y).TrimEnd();

        for (var x = 0; x < row.Length; x++)
        {
            if (row[x] == ' ')
            {
                continue;
            }

            var look = fonts.Look(grid.StyleAt(x, y));
            var formatted = Written(row[x].ToString(), look, grid.LinkAt(x, y));

            context.DrawText(
                formatted,
                new Point(place.X + (x * _glyphs.CellWidth), place.Y + (y * _glyphs.CellHeight)));
        }
    }

    /// <summary>
    /// [zm 8] The Z-machine's screen: one grid of cells, every one of
    /// them carrying its own style and colors.
    /// </summary>
    private void PaintScreen(DrawingContext context, BufferedScreen screen)
    {
        // The whole screen is the color it starts in, and then only the
        // stretches that differ are painted over it. Most screens are
        // one color throughout, so this is usually a single rectangle
        // where filling every cell would be thousands of them.
        var plain = Brush(screen.DefaultBackground);
        context.FillRectangle(
            plain,
            new Rect(0, 0, screen.Width * _glyphs.CellWidth, screen.Height * _glyphs.CellHeight));

        for (var row = 0; row < screen.Height; row++)
        {
            PaintBackgrounds(context, screen, row, plain);
        }

        // [zm 8.8.6] The pictures go over the backgrounds and under the
        // text, which is the order a Version 6 game draws them in: it
        // paints a picture and then writes over it.
        PaintPictures(context, screen);
        PaintBand(context, screen);

        for (var row = 0; row < screen.Height; row++)
        {
            for (var column = 0; column < screen.Width; column++)
            {
                PaintCell(context, screen[row, column], row, column);
            }
        }

        // The cursor, drawn as a line under the character it is on, so
        // that it does not hide what is already there.
        if (screen.Cursor is { } cursor)
        {
            context.FillRectangle(
                Brush(ScreenColor.White),
                new Rect(
                    cursor.Column * _glyphs.CellWidth,
                    ((cursor.Row + 1) * _glyphs.CellHeight) - 2,
                    _glyphs.CellWidth,
                    2));
        }
    }

    /// <summary>
    /// [arc contract 3] The arc_image band: an Arcturus story's scene
    /// across the top rows, with every piece of the text screen below
    /// it.
    /// </summary>
    /// <remarks>
    /// The band's height comes from the mode the story sent, never from
    /// the picture, so a picture that is not the shape the band expects
    /// is fitted rather than allowed to set the layout. The rest is the
    /// freedom [arc contract 3] gives a modern interpreter: the picture
    /// keeps its own shape, is scaled as far as it will go, and is
    /// centered in what rows it has, with the band's own background
    /// showing where it falls short.
    /// </remarks>
    private void PaintBand(DrawingContext context, BufferedScreen screen)
    {
        var rows = screen.Buffer.BandRows;

        if (rows <= 0)
        {
            return;
        }

        var place = new Rect(0, 0, screen.Width * _glyphs.CellWidth, rows * _glyphs.CellHeight);

        context.FillRectangle(Brush(ScreenColor.Black), place);

        if (screen.BandPicture == 0 || Pictures?.Bitmap(screen.BandPicture) is not { } picture)
        {
            return;
        }

        var scale = Math.Min(place.Width / picture.PixelSize.Width, place.Height / picture.PixelSize.Height);
        var width = picture.PixelSize.Width * scale;
        var height = picture.PixelSize.Height * scale;

        context.DrawImage(
            picture,
            new Rect(
                place.X + ((place.Width - width) / 2),
                place.Y + ((place.Height - height) / 2),
                width,
                height));
    }

    /// <summary>
    /// The title screen, over a black window, as large as it will go
    /// with its own shape kept, in the middle of the window.
    /// </summary>
    /// <remarks>
    /// [blorb 11.2] This is the scaling rule of a Version 6 game, which
    /// fits a picture to the screen it was drawn for and so runs out of
    /// room in one direction first. The artwork is 320 by 200 and a
    /// window is rarely that shape, so a band of black is left over.
    /// Frotz leaves the picture in the corner and the whole band along
    /// one edge; halving the band and putting it on both sides looks
    /// like a picture shown on purpose rather than one that fell short.
    /// </remarks>
    private void PaintTitle(DrawingContext context, WriteableBitmap picture)
    {
        context.FillRectangle(Brush(ScreenColor.Black), new Rect(Bounds.Size));

        var size = picture.PixelSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var scale = Math.Min(Bounds.Width / size.Width, Bounds.Height / size.Height);
        var width = size.Width * scale;
        var height = size.Height * scale;

        context.DrawImage(
            picture,
            new Rect(0, 0, size.Width, size.Height),
            new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height));
    }

    /// <summary>
    /// [zm op:draw_picture] Every picture the game has drawn, at the
    /// place and the size it asked for.
    /// </summary>
    /// <remarks>
    /// [zm 8.8.1] The placement carries both the cells it covers and
    /// where it really is in units. A unit is a pixel on this screen, so
    /// the unit rectangle is used: rounding to whole characters would
    /// shift artwork drawn to the pixel by as much as a character, which
    /// on the Infocom games is plainly visible.
    /// </remarks>
    private void PaintPictures(DrawingContext context, BufferedScreen screen)
    {
        if (Pictures is not { } pictures)
        {
            return;
        }

        foreach (var placement in screen.Pictures)
        {
            if (pictures.Bitmap(placement.Number, placement.Palette) is not { } bitmap
                || placement.UnitWidth <= 0
                || placement.UnitHeight <= 0)
            {
                continue;
            }

            context.DrawImage(
                bitmap,
                new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                new Rect(
                    placement.UnitLeft,
                    placement.UnitTop,
                    placement.UnitWidth,
                    placement.UnitHeight));
        }
    }

    /// <summary>
    /// Paints the stretches of one row whose background is not the
    /// color the screen starts in, each stretch in one piece so that no
    /// seam is left between the cells of it.
    /// </summary>
    private void PaintBackgrounds(DrawingContext context, BufferedScreen screen, int row, IBrush plain)
    {
        var from = 0;

        while (from < screen.Width)
        {
            var brush = Behind(screen[row, from]);
            var to = from + 1;

            while (to < screen.Width && ReferenceEquals(Behind(screen[row, to]), brush))
            {
                to++;
            }

            if (!ReferenceEquals(brush, plain))
            {
                context.FillRectangle(
                    brush,
                    new Rect(
                        from * _glyphs.CellWidth,
                        row * _glyphs.CellHeight,
                        (to - from) * _glyphs.CellWidth,
                        _glyphs.CellHeight));
            }

            from = to;
        }
    }

    /// <summary>
    /// [zm 8.7.1] What is behind a cell. Reverse video swaps the two
    /// colors rather than being a color of its own.
    /// </summary>
    private static IBrush Behind(ZCell cell) =>
        Brush(cell.Attributes.Style.HasFlag(ZStyle.ReverseVideo)
            ? cell.Attributes.Foreground
            : cell.Attributes.Background);

    private void PaintCell(DrawingContext context, ZCell cell, int row, int column)
    {
        var attributes = cell.Attributes;

        var reversed = attributes.Style.HasFlag(ZStyle.ReverseVideo);
        var ink = Brush(reversed ? attributes.Background : attributes.Foreground);

        var place = new Rect(
            column * _glyphs.CellWidth,
            row * _glyphs.CellHeight,
            _glyphs.CellWidth,
            _glyphs.CellHeight);

        // [zm 16] The character graphics font is shown as the nearest
        // Unicode box drawing, block, arrow, and runic characters, as
        // far as the font has them.
        var character = attributes.Font == TextAttributes.CharacterGraphicsFont
            ? CharacterGraphics.ToUnicode(cell.Character)
            : cell.Character;

        if (character is '\0' or ' ')
        {
            return;
        }

        var face = _glyphs.Fixed(
            attributes.Style.HasFlag(ZStyle.Bold),
            attributes.Style.HasFlag(ZStyle.Italic));

        var formatted = new FormattedText(
            character.ToString(),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            face,
            _glyphs.Size(GlkStyle.Preformatted),
            ink);

        context.DrawText(formatted, place.TopLeft);
    }

    /// <summary>
    /// [zm 8.3.1] A Z-machine color as something to paint with.
    /// </summary>
    /// <summary>
    /// [glk #stream_style_hints] A color, whose top eight bits are zero
    /// and whose other three are red, green, and blue, as something to
    /// paint with.
    /// </summary>
    /// <remarks>
    /// The brushes are kept, since a page of text asks for the same few
    /// colors over and over and the whole of it is painted again on
    /// every character the game prints.
    /// </remarks>
    private ImmutableSolidColorBrush Brush(uint color)
    {
        if (!_colors.TryGetValue(color, out var brush))
        {
            brush = new ImmutableSolidColorBrush(
                Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color));
            _colors[color] = brush;
        }

        return brush;
    }

    private static IBrush Brush(ScreenColor color) => color switch
    {
        ScreenColor.Black or ScreenColor.Default => Brushes.Black,
        ScreenColor.Red => Brushes.Firebrick,
        ScreenColor.Green => Brushes.ForestGreen,
        ScreenColor.Yellow => Brushes.Goldenrod,
        ScreenColor.Blue => Brushes.RoyalBlue,
        ScreenColor.Magenta => Brushes.Orchid,
        ScreenColor.Cyan => Brushes.CadetBlue,
        ScreenColor.White => Brushes.Gainsboro,
        _ => Brushes.Gainsboro,
    };

    /// <summary>
    /// A graphics window's bitmap, and which change of the canvas it was
    /// last filled from.
    /// </summary>
    private sealed record Painting(WriteableBitmap Bitmap, int Changes);
}

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

    private readonly HashSet<GlkWindow> _seen = [];
    private readonly Glyphs _glyphs;
    private GuiGlkDisplay? _display;
    private BufferedScreen? _screen;

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
    /// [zm 10] Where the Z-machine's keys go, for a game of that
    /// machine.
    /// </summary>
    public BufferedInput? Keys { get; set; }

    /// <summary>
    /// [zm 8.8.6] The pictures a Version 6 game draws.
    /// </summary>
    public GuiPictures? Pictures { get; set; }

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

        if (Screen is { } screen)
        {
            // [zm 8.4] A window is rarely a whole number of characters
            // across and down, so a strip is left over at the right and
            // the bottom. It is the screen's own color rather than the
            // page behind it, or the game appears to be sitting on a
            // sheet of paper a little too small for it.
            lock (screen.Sync)
            {
                context.FillRectangle(Brush(screen.DefaultBackground), new Rect(Bounds.Size));
                PaintScreen(context, screen);
            }

            return;
        }

        context.FillRectangle(Brush(GlkLook.Paper), new Rect(Bounds.Size));

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

        if (Keys is { } keys && GuiKeyMap.ToZscii(e.Key) is { } zscii)
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

        if (Keys is { } keys)
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

        if (Display is { Root: { } root } display)
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

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        Display?.Resize(e.NewSize.Width, e.NewSize.Height);
        Screen?.Resize(
            Math.Max((int)(e.NewSize.Width / _glyphs.CellWidth), 1),
            Math.Max((int)(e.NewSize.Height / _glyphs.CellHeight), 1));

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
                right = Math.Max(right, Bounds.Width);
            }

            if (window.Top + window.Height >= display.Height)
            {
                bottom = Math.Max(bottom, Bounds.Height);
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

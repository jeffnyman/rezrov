using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
    private static readonly IBrush Paper = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xF7));
    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly IBrush GridPaper = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
    private static readonly IBrush GridInk = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));

    private readonly Dictionary<GlkWindow, Painting> _canvases = [];
    private readonly HashSet<GlkWindow> _seen = [];
    private readonly Glyphs _glyphs;
    private GuiGlkDisplay? _display;
    private BufferedScreen? _screen;

    public Board(Glyphs glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);

        _glyphs = glyphs;
        Focusable = true;

        // The cells of a screen are filled as rectangles that share
        // their edges. Antialiasing those edges leaves a pale hairline
        // between every pair of them, which at a fractional display
        // scale draws a cross-hatch over the whole window. Text keeps
        // its own smoothing; this is about the geometry only.
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
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

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.FillRectangle(Paper, new Rect(Bounds.Size));

        if (Screen is { } screen)
        {
            lock (screen.Sync)
            {
                PaintScreen(context, screen);
            }

            return;
        }

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
    private Rect Place(GlkWindow window) => new(
        window.Left * _glyphs.CellWidth,
        window.Top * _glyphs.CellHeight,
        window.Width * _glyphs.CellWidth,
        window.Height * _glyphs.CellHeight);

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
        var text = Display!.Text(window);
        var lines = text.Lines(place.Width);

        using var clip = context.PushClip(place);

        var top = place.Top + text.Bottom(place.Width, place.Height);

        for (var i = lines.Count - 1; i >= 0 && top > place.Top - lines[i].Height; i--)
        {
            top -= lines[i].Height;

            foreach (var piece in lines[i].Pieces)
            {
                if (piece.Text == " ")
                {
                    continue;
                }

                var formatted = new FormattedText(
                    piece.Text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    _glyphs.Face(piece.Style),
                    _glyphs.Size(piece.Style),
                    Ink);

                context.DrawText(formatted, new Point(place.X + piece.Left, top));
            }
        }
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
        context.FillRectangle(GridPaper, place);

        using var clip = context.PushClip(place);

        for (var y = 0; y < grid.Height; y++)
        {
            var row = grid.Row(y).TrimEnd();
            if (row.Length == 0)
            {
                continue;
            }

            for (var x = 0; x < row.Length; x++)
            {
                if (row[x] == ' ')
                {
                    continue;
                }

                var formatted = new FormattedText(
                    row[x].ToString(),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    _glyphs.Grid(grid.StyleAt(x, y)),
                    _glyphs.Size(GlkStyle.Preformatted),
                    GridInk);

                context.DrawText(
                    formatted,
                    new Point(place.X + (x * _glyphs.CellWidth), place.Y + (y * _glyphs.CellHeight)));
            }
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
            if (pictures.Bitmap(placement.Number) is not { } bitmap
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

        var face = attributes.Style.HasFlag(ZStyle.Bold)
            ? _glyphs.Grid(GlkStyle.Header)
            : attributes.Style.HasFlag(ZStyle.Italic)
                ? _glyphs.Grid(GlkStyle.Emphasized)
                : _glyphs.Grid(GlkStyle.Preformatted);

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

using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Rezrov.Glulx.Glk;

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

    public Board(Glyphs glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);

        _glyphs = glyphs;
        Focusable = true;
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

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.FillRectangle(Paper, new Rect(Bounds.Size));

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

        if (Display is { } display && GuiKeyMap.ToGlk(e.Key) is { } key)
        {
            display.Key(key);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (Display is { } display && e.Text is { Length: > 0 } text)
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
    /// A graphics window's bitmap, and which change of the canvas it was
    /// last filled from.
    /// </summary>
    private sealed record Painting(WriteableBitmap Bitmap, int Changes);
}

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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
            Paint(context, root);
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

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        Display?.Resize(e.NewSize.Width, e.NewSize.Height);
        base.OnSizeChanged(e);
    }

    private void Paint(DrawingContext context, GlkWindow window)
    {
        if (window is PairWindow pair)
        {
            Paint(context, pair.First);
            Paint(context, pair.Second);
            return;
        }

        var place = new Rect(
            window.Left * _glyphs.CellWidth,
            window.Top * _glyphs.CellHeight,
            window.Width * _glyphs.CellWidth,
            window.Height * _glyphs.CellHeight);

        switch (window.Type)
        {
            case WindowType.TextBuffer:
                PaintBuffer(context, window, place);
                break;
            case WindowType.TextGrid when window is TextGridWindow grid:
                PaintGrid(context, grid, place);
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
}

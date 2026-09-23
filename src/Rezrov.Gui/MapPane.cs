using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Rezrov.Gui;

/// <summary>
/// The map of the game, drawn as pixels rather than as characters.
/// </summary>
/// <remarks>
/// The terminal writes its map into a grid, where a passage can only
/// leave a box by one of eight cells and a diagonal has to be bent into
/// steps or dropped. None of that is true here. A line runs from one
/// box's edge to another's at whatever angle it likes, which is what
/// lets a northeast passage be drawn as the northeast passage it is,
/// and lets a way in or out be drawn at all: those rooms are put
/// wherever there was space, and a line between two points does not
/// mind where they are.
///
/// Nothing is worked out in here. <see cref="MapShot"/> settles where
/// every box and line goes before the painting starts, so a repaint is
/// only a matter of putting shapes down, and everything that could be
/// wrong about the layout can be checked without a window.
///
/// The map does not move while a game is being played. Rooms keep the
/// cells they were first placed in, because a map that rearranged
/// itself under the player's eyes every time they walked through a door
/// would be useless for the one thing a map is for. Straightening it
/// out is something the player asks for.
/// </remarks>
internal sealed class MapPane : Control
{
    /// <summary>The blank the map is drawn on.</summary>
    private static readonly ImmutableSolidColorBrush Paper = new(Color.FromRgb(0xF7, 0xF6, 0xF1));

    /// <summary>A room's box, and the line around it.</summary>
    private static readonly ImmutableSolidColorBrush Room = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly ImmutableSolidColorBrush Edge = new(Color.FromRgb(0x6B, 0x66, 0x5E));

    /// <summary>The room the player is standing in.</summary>
    private static readonly ImmutableSolidColorBrush Here = new(Color.FromRgb(0xFD, 0xF0, 0xC8));
    private static readonly ImmutableSolidColorBrush Standing = new(Color.FromRgb(0xB3, 0x7A, 0x1F));

    /// <summary>Passages, their labels, and the room names.</summary>
    private static readonly ImmutableSolidColorBrush Passage = new(Color.FromRgb(0x55, 0x50, 0x49));
    private static readonly ImmutableSolidColorBrush Ink = new(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static readonly ImmutableSolidColorBrush Aside = new(Color.FromRgb(0x8A, 0x6D, 0x3B));

    /// <summary>How far in and out the player may zoom.</summary>
    private const double Least = 0.06;
    private const double Most = 3.0;

    /// <summary>
    /// The smallest scale a room's name can still be read at.
    /// </summary>
    /// <remarks>
    /// What decides whether the map follows the player or shows the
    /// whole game. Below this the names are a few pixels of gray, and
    /// a map whose rooms cannot be told apart is a picture of a map.
    /// </remarks>
    private const double Readable = 0.75;

    /// <summary>
    /// The size a room's name is written at when the map is not scaled,
    /// and the size below which writing it is no longer worth the mud.
    /// </summary>
    private const double Lettering = 12;
    private const double Legible = 6;

    private readonly Typeface _face;

    private MapShot _shot = MapShot.None;
    private ScreenFit _fit = new(1, 0, 0);
    private Point? _dragged;
    private bool _free;
    private int? _under;

    public MapPane(string family)
    {
        _face = new Typeface(new FontFamily(family));
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>
    /// The room the pointer is over, as it changes, so that the pane's
    /// heading can say the whole of a name a box was too small for.
    /// </summary>
    public event Action<string?>? Pointed;

    /// <summary>The map to draw, as it stood when it was taken.</summary>
    public MapShot Shot
    {
        get => _shot;

        set
        {
            _shot = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Goes back to keeping the player's own room in view as the game
    /// moves them around.
    /// </summary>
    public void Follow()
    {
        _free = false;
        InvalidateVisual();
    }

    /// <summary>
    /// Puts the whole game on the screen at once, however small that
    /// makes it, and leaves it there.
    /// </summary>
    /// <remarks>
    /// Asked for, and so honored even where nothing can be read: the
    /// shape of a game is worth seeing whole, and the player can say
    /// when they want it.
    /// </remarks>
    public void Whole()
    {
        _fit = _shot.Fitted(Bounds.Width, Bounds.Height);
        _free = true;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.FillRectangle(Paper, new Rect(Bounds.Size));

        // Until the player takes hold of it, the map follows them. The
        // whole game is shown while the whole game will go in the pane
        // and still be legible; past that it keeps them in the middle
        // at a size that can be read. Once they have panned or zoomed
        // it stays exactly where they put it, because a view that
        // jumped every turn would be worse than no view.
        if (!_free)
        {
            _fit = _shot.Following(Bounds.Width, Bounds.Height, Readable);
        }

        foreach (var line in _shot.Lines)
        {
            Draw(context, line);
        }

        foreach (var box in _shot.Boxes)
        {
            Draw(context, box);
        }
    }

    /// <summary>One passage, and what has to be said about it.</summary>
    private void Draw(DrawingContext context, MapLine line)
    {
        var (x1, y1) = _fit.Scaled(line.X1, line.Y1);
        var (x2, y2) = _fit.Scaled(line.X2, line.Y2);

        var one = new Point(x1, y1);
        var two = new Point(x2, y2);
        var width = Math.Max(1, 1.4 * _fit.Scale);

        // A way up, down, in or out is drawn broken, because the line's
        // own direction is not the direction that was walked: those
        // rooms were put where there was space rather than where the
        // passage points.
        var pen = line.Dashed
            ? new ImmutablePen(Passage, width, new ImmutableDashStyle([3, 3], 0))
            : new ImmutablePen(Passage, width);

        context.DrawLine(pen, one, two);

        if (line.ArrowAtOne)
        {
            Head(context, two, one, width);
        }

        if (line.ArrowAtTwo)
        {
            Head(context, one, two, width);
        }

        Label(context, line.LabelOne, one, two);
        Label(context, line.LabelTwo, two, one);
    }

    /// <summary>
    /// An arrowhead at <paramref name="tip"/>, opening back towards
    /// <paramref name="from"/>.
    /// </summary>
    /// <remarks>
    /// Only on a passage that has been walked one way. Half of these
    /// games turn on a passage that does not come back, and a map that
    /// drew it the same as any other would be lying about the only
    /// thing the player needed it to say.
    /// </remarks>
    private void Head(DrawingContext context, Point from, Point tip, double width)
    {
        var dx = tip.X - from.X;
        var dy = tip.Y - from.Y;
        var reach = Math.Sqrt((dx * dx) + (dy * dy));

        if (reach < 1)
        {
            return;
        }

        var size = Math.Max(4, 8 * _fit.Scale);
        var (ux, uy) = (dx / reach, dy / reach);
        var back = new Point(tip.X - (ux * size), tip.Y - (uy * size));
        var wing = size * 0.45;

        var pen = new ImmutablePen(Passage, width);

        context.DrawLine(pen, tip, new Point(back.X - (uy * wing), back.Y + (ux * wing)));
        context.DrawLine(pen, tip, new Point(back.X + (uy * wing), back.Y - (ux * wing)));
    }

    /// <summary>
    /// What was walked at one end of a line, written just off the end
    /// it belongs to.
    /// </summary>
    private void Label(DrawingContext context, string? text, Point at, Point towards)
    {
        var size = Lettering * _fit.Scale * 0.8;

        if (text is null || size < Legible)
        {
            return;
        }

        var dx = towards.X - at.X;
        var dy = towards.Y - at.Y;
        var reach = Math.Sqrt((dx * dx) + (dy * dy));

        if (reach < 1)
        {
            return;
        }

        // A little way along the line from its end, and pushed off to
        // one side of it, so the writing does not sit on the line.
        var along = Math.Min(reach * 0.35, size * 1.6);
        var (ux, uy) = (dx / reach, dy / reach);

        var written = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _face,
            size,
            Aside);

        context.DrawText(
            written,
            new Point(
                at.X + (ux * along) - (uy * size * 0.5) - (written.Width / 2),
                at.Y + (uy * along) + (ux * size * 0.5) - (written.Height / 2)));
    }

    /// <summary>One room, with as much of its name as fits.</summary>
    private void Draw(DrawingContext context, MapBox box)
    {
        var (left, top) = _fit.Scaled(box.Left, box.Top);
        var width = MapShot.BoxWidth * _fit.Scale;
        var height = MapShot.BoxHeight * _fit.Scale;
        var place = new Rect(left, top, width, height);

        var edge = box.Current
            ? new ImmutablePen(Standing, Math.Max(1.5, 2.4 * _fit.Scale))
            : new ImmutablePen(Edge, Math.Max(1, 1.2 * _fit.Scale));

        context.DrawRectangle(box.Current ? Here : Room, edge, place, 2, 2);

        var size = Lettering * _fit.Scale;

        if (size < Legible)
        {
            return;
        }

        var padding = 4 * _fit.Scale;

        var name = new FormattedText(
            box.Name,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _face,
            size,
            Ink)
        {
            TextAlignment = TextAlignment.Center,
            MaxTextWidth = Math.Max(width - (padding * 2), 1),
            MaxTextHeight = Math.Max(height - padding, 1),
            Trimming = TextTrimming.CharacterEllipsis,
        };

        context.DrawText(
            name,
            new Point(left + padding, top + Math.Max((height - name.Height) / 2, 0)));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        _dragged = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;

        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var at = e.GetPosition(this);

        if (_dragged is { } from)
        {
            _fit = _fit with
            {
                Across = _fit.Across + (at.X - from.X),
                Down = _fit.Down + (at.Y - from.Y),
            };

            _dragged = at;
            _free = true;
            InvalidateVisual();
        }
        else
        {
            Over(_shot.At(_fit, at.X, at.Y));
        }

        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        _dragged = null;
        e.Pointer.Capture(null);

        base.OnPointerReleased(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        Over(null);
        base.OnPointerExited(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var at = e.GetPosition(this);
        var scale = Math.Clamp(_fit.Scale * Math.Pow(1.2, e.Delta.Y), Least, Most);

        // The point under the pointer is the one that stays put, which
        // is what makes zooming feel like moving a sheet of paper
        // rather than jumping somewhere else in the game.
        var (across, down) = _fit.Unscaled(at.X, at.Y);

        _fit = new ScreenFit(scale, at.X - (across * scale), at.Y - (down * scale));
        _free = true;
        e.Handled = true;

        InvalidateVisual();
        base.OnPointerWheelChanged(e);
    }

    /// <summary>
    /// Says which room the pointer is over, but only when it changes,
    /// since the pointer moves a great deal more often than it moves
    /// from one room to another.
    /// </summary>
    private void Over(int? room)
    {
        if (_under == room)
        {
            return;
        }

        _under = room;

        Pointed?.Invoke(
            room is { } found
                ? _shot.Boxes.FirstOrDefault(b => b.Room == found).Name
                : null);
    }
}

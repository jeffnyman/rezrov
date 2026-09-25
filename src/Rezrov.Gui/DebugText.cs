using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Rezrov.Gui;

/// <summary>
/// A panel of fixed-width lines that can be scrolled with the wheel.
/// </summary>
/// <remarks>
/// A scroll viewer is a templated control and this program carries no
/// theme, so it would draw nothing at all. What is wanted here is a
/// number of lines, an offset into them, and a wheel that changes the
/// offset, which is less code than a theme package would be.
///
/// The lines are whatever the debugger said, unchanged. One of them
/// may be marked, which is how the instruction about to run is picked
/// out of a listing, and a marked line is kept in view when the lines
/// are replaced.
/// </remarks>
internal sealed class DebugText : Control
{
    /// <summary>What a line the debugger wrote begins with.</summary>
    public const string Aside = "; ";

    /// <summary>What the line about to be run begins with.</summary>
    public const string Here = "=> ";

    private static readonly ImmutableSolidColorBrush Paper = new(Color.FromRgb(0xFB, 0xFA, 0xF6));
    private static readonly ImmutableSolidColorBrush Ink = new(Color.FromRgb(0x33, 0x31, 0x2C));
    private static readonly ImmutableSolidColorBrush Quiet = new(Color.FromRgb(0x87, 0x83, 0x79));
    private static readonly ImmutableSolidColorBrush Lit = new(Color.FromRgb(0xE4, 0xEC, 0xDA));

    private readonly Typeface _face;
    private readonly double _size;

    private IReadOnlyList<string> _lines = [];
    private double _offset;

    public DebugText(string family, double size)
    {
        _face = new Typeface(new FontFamily(family));
        _size = size;
        ClipToBounds = true;
    }

    /// <summary>
    /// What to show. Setting this keeps the marked line in view, and
    /// otherwise stays where the reader had scrolled to.
    /// </summary>
    public IReadOnlyList<string> Lines
    {
        get => _lines;
        set
        {
            _lines = value;
            Reveal();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Whether new lines scroll the panel to the end, which is what a
    /// running log wants and a listing does not.
    /// </summary>
    public bool Trails { get; init; }

    /// <summary>The height of one line, which the wheel moves by.</summary>
    private double Step => _size * 1.35;

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.FillRectangle(Paper, new Rect(Bounds.Size));

        var first = Math.Max(0, (int)(_offset / Step));
        var top = (first * Step) - _offset;

        for (var i = first; i < _lines.Count && top < Bounds.Height; i++, top += Step)
        {
            var line = _lines[i];

            // The one line the game is about to carry out is worth
            // finding at a glance, so it gets a band of its own.
            if (line.StartsWith(Here, StringComparison.Ordinal))
            {
                context.FillRectangle(Lit, new Rect(0, top, Bounds.Width, Step));
            }

            var written = new FormattedText(
                line,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _face,
                _size,
                line.StartsWith(Aside, StringComparison.Ordinal) ? Quiet : Ink);

            context.DrawText(written, new Point(6, top));
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        Scroll(_offset - (e.Delta.Y * Step * 3));
        e.Handled = true;

        base.OnPointerWheelChanged(e);
    }

    /// <summary>
    /// Moves to where the reader asked, as far as there is anything to
    /// move to.
    /// </summary>
    private void Scroll(double to)
    {
        var most = Math.Max(0, (_lines.Count * Step) - Bounds.Height);

        _offset = Math.Clamp(to, 0, most);
        InvalidateVisual();
    }

    /// <summary>
    /// Puts the interesting line in view: the end for a log, and the
    /// instruction about to run for anything else.
    /// </summary>
    private void Reveal()
    {
        if (Trails)
        {
            Scroll(double.MaxValue);
            return;
        }

        var marked = -1;

        for (var i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].StartsWith(Here, StringComparison.Ordinal))
            {
                marked = i;
                break;
            }
        }

        if (marked < 0)
        {
            Scroll(0);
            return;
        }

        // A third of the way down, so that what has just happened is
        // visible above it and what is coming is visible below.
        Scroll((marked * Step) - (Bounds.Height / 3));
    }
}

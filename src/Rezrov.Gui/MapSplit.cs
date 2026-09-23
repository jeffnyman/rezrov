using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Rezrov.Gui;

/// <summary>
/// The window's whole contents: the game, and the map beside it when
/// the player has asked for one.
/// </summary>
/// <remarks>
/// The map starts put away, so a game that is never mapped is played in
/// exactly the window it was played in before. Opening the map takes
/// columns off the game, which is a resize and nothing more: the board
/// tells the interpreter its new size the same way it does when the
/// window itself is dragged, and an Infocom Version 6 game, which is
/// laid out for a screen of a fixed size, is only scaled rather than
/// re-gridded.
///
/// The divider is a plain border that listens for a drag. A toolkit
/// splitter is a templated control and this program carries no theme,
/// and what a splitter does amounts to one subtraction.
/// </remarks>
internal sealed class MapSplit : Grid
{
    private static readonly ImmutableSolidColorBrush Rule = new(Color.FromRgb(0xC9, 0xC5, 0xBA));

    /// <summary>How thin either side may be dragged, in pixels.</summary>
    private const double Least = 160;

    /// <summary>How much of a fresh window the map takes.</summary>
    private const double Share = 0.4;

    private readonly ColumnDefinition _column;
    private readonly Border _divider;
    private readonly MapSide _side;

    /// <summary>
    /// How wide the map should be when it is showing.
    /// </summary>
    /// <remarks>
    /// A share of the window to begin with, rather than a measured
    /// number of pixels, because the map can be asked for before the
    /// window has ever been laid out and there would be nothing to take
    /// a share of. It stays a share until the player drags the divider,
    /// which is the better behavior anyway: until they have said what
    /// they want, the map keeps its share of a window as the window is
    /// resized.
    /// </remarks>
    private GridLength _want = new(Share / (1 - Share), GridUnitType.Star);

    private Point? _dragged;

    public MapSplit(Control board, MapSide side)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(side);

        _side = side;
        _column = new ColumnDefinition(new GridLength(0, GridUnitType.Pixel));

        ColumnDefinitions =
        [
            new ColumnDefinition(GridLength.Star),
            new ColumnDefinition(GridLength.Auto),
            _column,
        ];

        _divider = new Border
        {
            Background = Rule,
            Width = 4,
            IsVisible = false,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
        };

        _divider.PointerPressed += Grabbed;
        _divider.PointerMoved += Dragged;
        _divider.PointerReleased += Let;

        side.IsVisible = false;
        side.Closed += () => Show(false);

        SetColumn(_divider, 1);
        SetColumn(side, 2);

        Children.Add(board);
        Children.Add(_divider);
        Children.Add(side);
    }

    /// <summary>Whether the map is beside the game.</summary>
    public bool Showing => _side.IsVisible;

    /// <summary>Puts the map away, or brings it back.</summary>
    public void Toggle() => Show(!Showing);

    private void Show(bool showing)
    {
        if (showing)
        {
            // Where the player is standing is the right first sight of
            // the map, whatever they had done to the view before they
            // last closed it.
            _side.Follow();
        }

        _side.IsVisible = showing;
        _divider.IsVisible = showing;
        _column.Width = showing ? _want : new GridLength(0, GridUnitType.Pixel);
    }

    private void Grabbed(object? sender, PointerPressedEventArgs e)
    {
        _dragged = e.GetPosition(this);
        e.Pointer.Capture(_divider);
        e.Handled = true;
    }

    private void Dragged(object? sender, PointerEventArgs e)
    {
        if (_dragged is not { } from)
        {
            return;
        }

        var at = e.GetPosition(this);

        // Dragging the divider left makes the map wider, which is why
        // the movement is taken away rather than added. The width it is
        // taken away from is the one the map has actually been given,
        // which is how a share becomes a number of pixels the moment
        // the player first takes hold of it.
        var want = _column.ActualWidth - (at.X - from.X);
        var most = Math.Max(Bounds.Width - Least, Least);

        _want = new GridLength(Math.Clamp(want, Least, most), GridUnitType.Pixel);
        _column.Width = _want;
        _dragged = at;
        e.Handled = true;
    }

    private void Let(object? sender, PointerReleasedEventArgs e)
    {
        _dragged = null;
        e.Pointer.Capture(null);
    }
}

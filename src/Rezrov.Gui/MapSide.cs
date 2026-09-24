using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

using Rezrov.Mapping;
using Rezrov.Watching;

namespace Rezrov.Gui;

/// <summary>
/// The map and the few words above it: what has been found so far, the
/// name of whichever room the pointer is over, and the two things the
/// player can ask of the map.
/// </summary>
/// <remarks>
/// The game is played on one thread and drawn on another, so the map is
/// never read where it lies. Each turn the watcher says it has changed,
/// and this copies the whole of it into shapes with the game held off,
/// back on the thread that draws. Every repaint after that touches
/// nothing the game can reach.
///
/// Nothing here is a templated control, because this program carries no
/// theme: the window paints a whole Glk screen out of nothing, and
/// taking on a theme package to put three words in a bar would be a
/// poor trade. A border with text in it draws perfectly well on its
/// own.
/// </remarks>
internal sealed class MapSide : Grid
{
    private static readonly ImmutableSolidColorBrush Bar = new(Color.FromRgb(0xEC, 0xEA, 0xE2));
    private static readonly ImmutableSolidColorBrush Paper = new(Color.FromRgb(0xF7, 0xF6, 0xF1));
    private static readonly ImmutableSolidColorBrush Rule = new(Color.FromRgb(0xC9, 0xC5, 0xBA));
    private static readonly ImmutableSolidColorBrush Ink = new(Color.FromRgb(0x33, 0x31, 0x2C));
    private static readonly ImmutableSolidColorBrush Quiet = new(Color.FromRgb(0x77, 0x73, 0x6A));
    private static readonly ImmutableSolidColorBrush Lit = new(Color.FromRgb(0xD9, 0xD5, 0xC8));

    private readonly RoomWatcher? _watcher;
    private readonly MapPane? _pane;
    private readonly TextBlock _says;

    private string? _under;

    /// <param name="watcher">
    /// The map being built, or null for a story nothing knows how to
    /// map, which is the honest state for an Aa-machine story.
    /// </param>
    /// <param name="family">The face the map is lettered in.</param>
    public MapSide(RoomWatcher? watcher, string family)
    {
        _watcher = watcher;

        // Its own, rather than whatever is behind it. A story with no
        // map to draw would otherwise leave the game showing through
        // the space where the map would have been.
        Background = Paper;

        RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)];

        _says = new TextBlock
        {
            Foreground = Quiet,
            FontFamily = new FontFamily(family),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        Children.Add(Heading(family));

        if (watcher is null)
        {
            _says.Text = "No map: this story format is not mapped yet.";
            return;
        }

        _pane = new MapPane(family);
        _pane.Pointed += name =>
        {
            _under = name;
            Told();
        };

        SetRow(_pane, 1);
        Children.Add(_pane);

        watcher.Changed += Turned;
        Refresh();
    }

    /// <summary>Asked for the map to be put away.</summary>
    public event Action? Closed;

    /// <summary>
    /// Stops listening, for when the window closes and the game thread
    /// may still be finishing a turn.
    /// </summary>
    public void Release()
    {
        if (_watcher is not null)
        {
            _watcher.Changed -= Turned;
        }
    }

    /// <summary>
    /// Brings the map up to date and puts it back to following the
    /// player, whatever the view had been left as.
    /// </summary>
    public void Follow()
    {
        Refresh();
        _pane?.Follow();
    }

    /// <summary>
    /// Takes the map as it stands and hands it to the pane.
    /// </summary>
    /// <remarks>
    /// Called on the drawing thread only. The copy is made with the
    /// game held off the graph and nothing of the graph is kept, so
    /// painting can go on for as long as it likes afterwards while the
    /// game gets on with its next turn.
    /// </remarks>
    public void Refresh()
    {
        if (_watcher is null || _pane is null)
        {
            return;
        }

        var shot = MapShot.None;
        _watcher.Read(graph => shot = MapShot.Of(graph));

        _pane.Shot = shot;
        Told();
    }

    /// <summary>
    /// A turn has been played, which happens on the game's own thread.
    /// </summary>
    private void Turned() => Dispatcher.UIThread.Post(Refresh);

    /// <summary>
    /// Straightens the map out, which the player asks for and the map
    /// never does on its own.
    /// </summary>
    /// <remarks>
    /// Rooms drift as a game is played, because each one is placed
    /// where it fell when it was first walked into and is never moved
    /// again. That promise is what makes the map worth watching: a room
    /// stays where the player last saw it. Tidying breaks the promise
    /// deliberately, once, when they say so.
    /// </remarks>
    private void Tidy()
    {
        _watcher?.Read(graph => MapTidy.Tidy(graph));
        Refresh();
    }

    /// <summary>
    /// Throws this map away and starts another.
    /// </summary>
    /// <remarks>
    /// The only thing in the program that discards a map. Restarting
    /// a game does not, restoring a save does not, and closing the
    /// window does not: all of those change where the player is
    /// standing rather than what they have found out. Starting over is
    /// a thing the player says, here.
    /// </remarks>
    private void Clear()
    {
        Forget?.Invoke();
        _watcher?.Forget();
        _under = null;
        Refresh();
        _pane?.Follow();
    }

    /// <summary>Asked for the kept map to be thrown away.</summary>
    public event Action? Forget;

    /// <summary>The bar above the map.</summary>
    private Border Heading(string family)
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 4,
        };

        if (_watcher is not null)
        {
            buttons.Children.Add(new Tap("clear", family, Clear));
            buttons.Children.Add(new Tap("tidy", family, Tidy));
            buttons.Children.Add(new Tap("fit", family, () => _pane?.Whole()));
        }

        buttons.Children.Add(new Tap("close", family, () => Closed?.Invoke()));

        var inside = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
        };

        inside.Children.Add(_says);
        SetColumn(buttons, 1);
        inside.Children.Add(buttons);

        return new Border
        {
            Background = Bar,
            BorderBrush = Rule,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 5),
            Child = inside,
        };
    }

    /// <summary>
    /// What the bar says: the name of the room under the pointer where
    /// there is one, since a box is often too small to hold it, and the
    /// size of the map otherwise.
    /// </summary>
    private void Told()
    {
        if (_under is { } name)
        {
            _says.Text = name;
            _says.Foreground = Ink;
            return;
        }

        var rooms = _pane?.Shot.Boxes.Count ?? 0;
        var passages = _pane?.Shot.Passages ?? 0;

        _says.Foreground = Quiet;
        _says.Text = rooms == 0
            ? "Nowhere yet."
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0} room{1}, {2} passage{3}",
                rooms,
                rooms == 1 ? "" : "s",
                passages,
                passages == 1 ? "" : "s");
    }

    /// <summary>
    /// A word that can be pressed, which is as much of a button as this
    /// needs and asks nothing of a theme.
    /// </summary>
    private sealed class Tap : Border
    {
        public Tap(string text, string family, Action pressed)
        {
            Child = new TextBlock
            {
                Text = text,
                Foreground = Ink,
                FontFamily = new FontFamily(family),
                FontSize = 12,
            };

            Padding = new Thickness(7, 2);
            CornerRadius = new CornerRadius(3);
            BorderBrush = Rule;
            BorderThickness = new Thickness(1);
            Cursor = new Cursor(StandardCursorType.Hand);

            PointerEntered += (_, _) => Background = Lit;
            PointerExited += (_, _) => Background = null;

            PointerPressed += (_, e) =>
            {
                pressed();
                e.Handled = true;
            };
        }
    }
}

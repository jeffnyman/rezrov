using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Rezrov.Core;
using Rezrov.Core.Graphics;

namespace Rezrov.Gui;

/// <summary>
/// The menu bar across the top of the window, and the rule that keeps it
/// from taking the keyboard away from the game.
/// </summary>
/// <remarks>
/// The game owns the keyboard. A menu only borrows it, and a borrowed
/// keyboard has to come back however the menu was left, because a player
/// whose typing silently goes nowhere has no way of knowing why. Two ways
/// of leaving a menu do not give it back on their own. Escape closes a
/// drop-down but leaves the bar holding the keyboard, however many times
/// it is pressed. And a bare Alt, which a player brushes on the way to
/// Alt-Tab, moves the keyboard to the bar without opening anything, so
/// there is no closing to notice. Both were found by logging where every
/// key landed, and both are dealt with in <see cref="Guard"/>.
///
/// Every item does something. One that did nothing when chosen would be
/// worse than no item at all, so the menus grow as the things they offer
/// are made to work.
///
/// macOS is left out. A Mac program keeps its menus in the bar along the
/// top of the screen rather than in its window, which is a different
/// thing to build and a different thing to test.
/// </remarks>
internal sealed class MenuBar
{
    private readonly Menu _menu = new();

    // Whatever last had the keyboard other than the menu, which is what
    // the menu gives it back to. That is the game's panel in an ordinary
    // window and may be the debugger's prompt in a window laid out for
    // debugging, so it is remembered rather than assumed.
    private InputElement? _owner;

    /// <param name="offered">
    /// Whether a command can be typed for the player just now: whether
    /// the game is waiting for one, and knows the word.
    /// </param>
    /// <param name="type">
    /// What typing a command for the player does.
    /// </param>
    /// <param name="map">
    /// What opening and closing the map does, or null where the window
    /// has no map to show.
    /// </param>
    /// <param name="quit">What leaving the game does.</param>
    public MenuBar(Func<string, bool> offered, Action<string> type, Action? map, Action quit)
    {
        ArgumentNullException.ThrowIfNull(offered);
        ArgumentNullException.ThrowIfNull(type);

        // The commands are typed into the game rather than done behind
        // its back, which is how Windows Frotz offers saving and
        // restoring too: the game does the saving, with its own prompt
        // and its own way of asking for a file, and nothing here needs
        // to know how any machine saves. Whether each one is offered is
        // asked as the menu opens rather than kept up to date, since it
        // is only true while the game sits at its prompt.
        var game = new MenuItem { Header = "_Game" };
        var commands = Commands
            .Select(word => (Word: word, Item: Item(Labels[word], () =>
            {
                if (offered(word))
                {
                    type(word);
                }
            })))
            .ToList();

        foreach (var (_, item) in commands)
        {
            game.Items.Add(item);
        }

        game.SubmenuOpened += (_, _) =>
        {
            foreach (var (word, item) in commands)
            {
                item.IsEnabled = offered(word);
            }
        };

        game.Items.Add(new Separator());
        game.Items.Add(Item("_Quit", quit));
        _menu.Items.Add(game);

        if (map is not null)
        {
            // The gesture is only what the item says. The key itself is
            // caught by the window on its way down, as it was before
            // there was a menu to mention it.
            var view = new MenuItem { Header = "_View" };
            view.Items.Add(Item("_Map", map, new KeyGesture(Key.M, KeyModifiers.Control)));
            _menu.Items.Add(view);
        }

        var help = new MenuItem { Header = "_Help" };
        help.Items.Add(Item("_About Rezrov", () => About()));
        _menu.Items.Add(help);
    }

    /// <summary>
    /// The commands the Game menu offers to type for the player, in the
    /// order it offers them.
    /// </summary>
    /// <remarks>
    /// Each is a word a game's own dictionary either holds or does not,
    /// which is how a game that never had undo, as none of Infocom's did
    /// before Version 5, is left without the item rather than given a
    /// word it can only complain about.
    /// </remarks>
    public static IReadOnlyList<string> Commands { get; } = ["save", "restore", "undo", "restart"];

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["save"] = "_Save...",
        ["restore"] = "_Restore...",
        ["undo"] = "_Undo",
        ["restart"] = "Res_tart",
    };

    /// <summary>Whether a window on this machine has a menu bar.</summary>
    public static bool Wanted => !OperatingSystem.IsMacOS();

    /// <summary>The bar itself, for the window to put at its top.</summary>
    public Control Control => _menu;

    /// <summary>
    /// How tall the bar comes to, so that the window can be made that much
    /// taller and the game keeps every row it would have had without one.
    /// </summary>
    /// <remarks>
    /// Only meaningful once the bar is inside a window, since until then
    /// no theme has styled it and it measures nothing.
    /// </remarks>
    public double Height()
    {
        _menu.Measure(Size.Infinity);
        return _menu.DesiredSize.Height;
    }

    /// <summary>
    /// Makes sure the keyboard always finds its way back from the menu.
    /// </summary>
    public void Guard(Window window)
    {
        window.AddHandler(
            InputElement.GotFocusEvent,
            (_, e) =>
            {
                if (e.Source is InputElement element && !Inside(element))
                {
                    _owner = element;
                }
            },
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        // However a drop-down was left, by choosing an item, by Escape,
        // or by a click somewhere else, the keyboard goes back to where
        // it was taken from.
        _menu.Closed += (_, _) => _owner?.Focus();

        // A bare Alt is let through to the game but not to the bar. Only
        // the release of Alt is held back, and only while something other
        // than the menu has the keyboard, so Alt held down while another
        // key is pressed is left to do whatever it did.
        window.AddHandler(
            InputElement.KeyUpEvent,
            (_, e) =>
            {
                if (e.Key is Key.LeftAlt or Key.RightAlt
                    && !Inside(window.FocusManager?.GetFocusedElement()))
                {
                    e.Handled = true;
                }
            },
            RoutingStrategies.Tunnel);
    }

    private static bool Inside(object? element) => element is Menu or MenuItem;

    private static MenuItem Item(string header, Action chosen, KeyGesture? gesture = null)
    {
        var item = new MenuItem { Header = header, InputGesture = gesture };
        item.Click += (_, _) => chosen();
        return item;
    }

    /// <summary>
    /// A small window saying what this program is and which release.
    /// </summary>
    private void About()
    {
        if (TopLevel.GetTopLevel(_menu) is not Window owner)
        {
            return;
        }

        var close = new Button
        {
            Content = "Close",
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = "Rezrov", FontSize = 22, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"Version {ProgramVersion.Current}" });
        panel.Children.Add(new TextBlock
        {
            Text = "An interactive fiction interpreter for the Z-machine, Glulx, and the Å-machine.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 380,
            Margin = new Thickness(0, 8, 0, 0),
        });
        panel.Children.Add(new TextBlock { Text = "https://github.com/jeffnyman/rezrov" });
        panel.Children.Add(new TextBlock { Text = "MIT License. Copyright (c) 2026 Jeff Nyman." });
        panel.Children.Add(close);

        var about = new Window
        {
            Title = "About Rezrov",

            // The program's own mark rather than the story's, since
            // this window is about the one and not the other.
            Icon = StoryIcons.Read(StoryIcons.Rezrov) is { } mark ? new WindowIcon(new MemoryStream(mark)) : null,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        close.Click += (_, _) => about.Close();

        // A window that has just come back from showing another one
        // modally gives the keyboard back to the game and then, as it
        // is let have its own controls again, takes it away. So the
        // keyboard is handed back once that has settled rather than
        // as the other window closes.
        about.Closed += (_, _) => Dispatcher.UIThread.Post(() => _owner?.Focus(), DispatcherPriority.Background);
        _ = about.ShowDialog(owner);
    }
}

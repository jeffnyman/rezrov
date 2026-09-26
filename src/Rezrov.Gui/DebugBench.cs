using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using Rezrov.Debugging;

namespace Rezrov.Gui;

/// <summary>
/// The window laid out for taking a game apart rather than for playing
/// one: the listing and the variables above, the game and the call
/// chain below, and the debugger's own prompt across the bottom.
/// </summary>
/// <remarks>
/// The game gets a panel rather than the window because here it is the
/// attachment and the debugger is the work. It is the same board that
/// draws a game played normally, so the game is told the size of its
/// panel and lays itself out for that, exactly as it does for a window
/// that has been made smaller.
///
/// Everything shown comes from a <see cref="DebugView"/>, which is
/// gathered on the thread the game runs on. Nothing here ever reads
/// the machine.
/// </remarks>
internal sealed class DebugBench : Grid
{
    /// <summary>How much of the log is worth keeping.</summary>
    private const int Kept = 500;

    private static readonly ImmutableSolidColorBrush Bar = new(Color.FromRgb(0xEC, 0xEA, 0xE2));
    private static readonly ImmutableSolidColorBrush Rule = new(Color.FromRgb(0xC9, 0xC5, 0xBA));
    private static readonly ImmutableSolidColorBrush Ink = new(Color.FromRgb(0x33, 0x31, 0x2C));

    private readonly DebugText _listing;
    private readonly DebugText _routine;
    private readonly DebugText _globals;
    private readonly DebugText _chain;
    private readonly DebugText _watching;
    private readonly DebugText _log;
    private readonly List<string> _said = [];

    /// <param name="board">The game, drawn as it always is.</param>
    /// <param name="sans">The face the headings are written in.</param>
    /// <param name="typewriter">The face a listing is written in.</param>
    /// <param name="size">How large the listings are.</param>
    public DebugBench(Control board, string sans, string typewriter, double size)
    {
        ArgumentNullException.ThrowIfNull(board);

        _listing = new DebugText(typewriter, size);
        _routine = new DebugText(typewriter, size);
        _globals = new DebugText(typewriter, size);
        _chain = new DebugText(typewriter, size);
        _watching = new DebugText(typewriter, size);
        _log = new DebugText(typewriter, size) { Trails = true };

        Prompt = new DebugPrompt(typewriter, size);

        RowDefinitions =
        [
            new RowDefinition(new GridLength(3, GridUnitType.Star)),
            new RowDefinition(new GridLength(2, GridUnitType.Star)),
            new RowDefinition(new GridLength(1, GridUnitType.Star)),
            new RowDefinition(GridLength.Auto),
        ];

        Add(Across(Framed("listing", sans, _listing), Beside(sans)), 0);
        Add(Across(Framed("the game", sans, board), Under(sans)), 1);
        Add(Framed("the debugger", sans, _log), 2);
        Add(Prompt, 3);
    }

    /// <summary>Where the player types commands.</summary>
    public DebugPrompt Prompt { get; }

    /// <summary>
    /// Shows what a command said and the state it left the game in.
    /// </summary>
    /// <remarks>
    /// Called on the drawing thread with a view that was gathered on
    /// the game's, which is the whole of how the two threads share
    /// what the debugger knows.
    ///
    /// The typed line is written into the log before its answer, so
    /// that the log reads as the conversation it is. A terminal gets
    /// that for nothing, since what was typed is already on the
    /// screen, and a window has to put it there.
    /// </remarks>
    public void Show(string typed, string said, DebugView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (typed.Length > 0)
        {
            _said.Add(DebugPrompt.Lead + typed);
        }

        if (said.Length > 0)
        {
            _said.AddRange(Lines(said));
        }

        if (_said.Count > Kept)
        {
            _said.RemoveRange(0, _said.Count - Kept);
        }

        _log.Lines = [.. _said];
        _listing.Lines = Lines(view.Listing);
        _chain.Lines = Lines(view.Chain);
        _globals.Lines = Lines(view.Globals);
        _watching.Lines = Lines(view.Watching);
        _routine.Lines = [.. Lines(view.Locals), string.Empty, .. Lines(view.Stack)];
    }

    private static string[] Lines(string text) =>
        text.Length == 0 ? [] : text.Split(Environment.NewLine);

    /// <summary>
    /// The right hand column beside the game: the routines it is
    /// inside, and whatever is being kept an eye on.
    /// </summary>
    private Grid Under(string sans)
    {
        var column = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(new GridLength(1, GridUnitType.Star)),
                new RowDefinition(new GridLength(1, GridUnitType.Star)),
            ],
        };

        var chain = Framed("call chain", sans, _chain);
        var watching = Framed("watching", sans, _watching);

        SetRow(watching, 1);
        column.Children.Add(chain);
        column.Children.Add(watching);

        return column;
    }

    /// <summary>The right hand column above the game.</summary>
    private Grid Beside(string sans)
    {
        var column = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(new GridLength(1, GridUnitType.Star)),
                new RowDefinition(new GridLength(2, GridUnitType.Star)),
            ],
        };

        var routine = Framed("this routine", sans, _routine);
        var globals = Framed("globals", sans, _globals);

        SetRow(globals, 1);
        column.Children.Add(routine);
        column.Children.Add(globals);

        return column;
    }

    private void Add(Control child, int row)
    {
        SetRow(child, row);
        Children.Add(child);
    }

    /// <summary>Two panels side by side, the left one the wider.</summary>
    private static Grid Across(Control left, Control right)
    {
        var across = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
            ],
        };

        SetColumn(right, 1);
        across.Children.Add(left);
        across.Children.Add(right);

        return across;
    }

    /// <summary>A panel with a word above it saying what it is.</summary>
    private static Grid Framed(string title, string sans, Control body)
    {
        var framed = new Grid
        {
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)],
        };

        framed.Children.Add(new Border
        {
            Background = Bar,
            BorderBrush = Rule,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(8, 4),
            Child = new TextBlock
            {
                Text = title,
                Foreground = Ink,
                FontFamily = new FontFamily(sans),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });

        var inside = new Border
        {
            BorderBrush = Rule,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = body,
        };

        SetRow(inside, 1);
        framed.Children.Add(inside);

        return framed;
    }
}

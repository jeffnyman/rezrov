using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Rezrov.Gui;

/// <summary>
/// The line the player types debugger commands on.
/// </summary>
/// <remarks>
/// A text box is a templated control and this program carries no
/// theme, so it would draw nothing. What is needed here is one line of
/// text, a caret, backspace, enter, and the last few commands on the
/// up arrow, which is the whole of it.
///
/// While a command is being carried out the prompt says so and takes
/// nothing, because the game may be the thing running and the player
/// may be about to type a command to the game rather than to the
/// debugger. Whichever of the two is waiting is the one with the
/// keyboard.
/// </remarks>
internal sealed class DebugPrompt : Control
{
    private static readonly ImmutableSolidColorBrush Paper = new(Color.FromRgb(0xF2, 0xF0, 0xE8));
    private static readonly ImmutableSolidColorBrush Ink = new(Color.FromRgb(0x33, 0x31, 0x2C));
    private static readonly ImmutableSolidColorBrush Quiet = new(Color.FromRgb(0x87, 0x83, 0x79));
    private static readonly ImmutableSolidColorBrush Rule = new(Color.FromRgb(0xC9, 0xC5, 0xBA));

    /// <summary>What a typed line is shown after.</summary>
    public const string Lead = "(rezrov) ";

    private readonly Typeface _face;
    private readonly double _size;
    private readonly List<string> _said = [];

    private string _typed = string.Empty;
    private int _back;
    private bool _busy;

    public DebugPrompt(string family, double size)
    {
        _face = new Typeface(new FontFamily(family));
        _size = size;

        Focusable = true;
        Height = size * 2.2;
        Cursor = new Cursor(StandardCursorType.Ibeam);

        PointerPressed += (_, _) => Focus();

        // The caret is only drawn while this has the keyboard, so the
        // player can see which of the two is listening.
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
    }

    /// <summary>A command has been typed.</summary>
    public event Action<string>? Entered;

    /// <summary>
    /// Whether a command is being carried out, during which the player
    /// is typing to the game rather than to the debugger.
    /// </summary>
    public bool Busy
    {
        get => _busy;
        set
        {
            _busy = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.FillRectangle(Paper, new Rect(Bounds.Size));
        context.DrawLine(new Pen(Rule), new Point(0, 0), new Point(Bounds.Width, 0));

        var text = _busy ? "the game is running, and the keyboard is its own" : Lead + _typed;

        var written = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _face,
            _size,
            _busy ? Quiet : Ink);

        var top = (Bounds.Height - written.Height) / 2;
        context.DrawText(written, new Point(8, top));

        if (!_busy && IsFocused)
        {
            var caret = 8 + written.Width + 1;
            context.DrawLine(
                new Pen(Ink),
                new Point(caret, top + 1),
                new Point(caret, top + written.Height - 1));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_busy)
        {
            base.OnKeyDown(e);
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                Enter();
                e.Handled = true;
                break;

            case Key.Back when _typed.Length > 0:
                _typed = _typed[..^1];
                e.Handled = true;
                break;

            case Key.Escape:
                _typed = string.Empty;
                e.Handled = true;
                break;

            // The last few commands, because a debugger is mostly the
            // same handful of words over and over.
            case Key.Up when _back < _said.Count:
                _typed = _said[^++_back];
                e.Handled = true;
                break;

            case Key.Down when _back > 1:
                _typed = _said[^--_back];
                e.Handled = true;
                break;

            case Key.Down when _back == 1:
                _back = 0;
                _typed = string.Empty;
                e.Handled = true;
                break;

            default:
                break;
        }

        if (e.Handled)
        {
            InvalidateVisual();
        }

        base.OnKeyDown(e);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (!_busy && e.Text is { Length: > 0 } text)
        {
            // A newline arriving as text rather than as a key press is
            // still the player having finished the line.
            foreach (var character in text)
            {
                if (character is '\r' or '\n')
                {
                    Enter();
                }
                else if (!char.IsControl(character))
                {
                    _typed += character;
                }
            }

            e.Handled = true;
            InvalidateVisual();
        }

        base.OnTextInput(e);
    }

    private void Enter()
    {
        var line = _typed.Trim();

        _typed = string.Empty;
        _back = 0;

        if (line.Length == 0)
        {
            return;
        }

        // The same command twice running is one line of history, not
        // two, or holding the up arrow walks through a wall of steps.
        if (_said.Count == 0 || !string.Equals(_said[^1], line, StringComparison.Ordinal))
        {
            _said.Add(line);
        }

        Entered?.Invoke(line);
    }
}

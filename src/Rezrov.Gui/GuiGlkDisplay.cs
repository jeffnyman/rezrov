using System.Collections.Concurrent;
using System.Text;
using Rezrov.Core.Graphics;
using Rezrov.Glulx.Glk;

namespace Rezrov.Gui;

/// <summary>
/// The Glk display in a window: text buffer windows kept here as text
/// that wraps to the width it is given, text grids and graphics read
/// from the library, and the player's keys taken from a queue the
/// control fills.
/// </summary>
/// <remarks>
/// The interpreter runs on its own thread and calls in here to print
/// and to wait; the control paints on the toolkit's thread and reads
/// what is here under <see cref="Sync"/>. That is the same arrangement
/// the terminal frontend uses, and for the same reason: a Glk display
/// is called from inside the game's own execution, so it cannot be the
/// thread that also draws.
///
/// [glk #window_textgrid] A text grid's contents belong to the library,
/// which keeps every cell, so nothing is kept here for one. Only text
/// buffer windows need storage of their own, since [glk #window_textbuf]
/// the library hands their text over a character at a time and expects
/// the display to remember it.
///
/// [glk #line_events] Showing the line as it is typed is the display's
/// work, so the line being edited lives here and is drawn after the
/// text, in the style Glk keeps for input.
/// </remarks>
public sealed class GuiGlkDisplay : IGlkDisplay
{
    private readonly BlockingCollection<Press> _presses = [];
    private readonly Dictionary<GlkWindow, BufferText> _buffers = [];
    private readonly Dictionary<GlkWindow, IGlyphs> _fonts = [];
    private readonly StringBuilder _typing = new();
    private readonly IGlyphs _glyphs;
    private readonly Action _repaint;
    private double _pixelWidth;
    private double _pixelHeight;
    private bool _resized;
    private GlkWindow? _interrupted;

    /// <param name="glyphs">The font the text is measured in.</param>
    /// <param name="repaint">Asks the control to draw again.</param>
    /// <param name="width">The window's width in pixels.</param>
    /// <param name="height">The window's height in pixels.</param>
    public GuiGlkDisplay(IGlyphs glyphs, Action repaint, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(repaint);

        _glyphs = glyphs;
        _repaint = repaint;
        _pixelWidth = width;
        _pixelHeight = height;
    }

    /// <summary>What the control reads the windows under.</summary>
    public object Sync { get; } = new();

    /// <summary>
    /// [glk #window_arrangement] The top of the window tree, or null
    /// before the game opens its first window.
    /// </summary>
    public GlkWindow? Root { get; private set; }

    /// <summary>
    /// [glk #line_events] The window a line is being typed into, or
    /// null when nothing is being typed.
    /// </summary>
    public GlkWindow? Typing { get; private set; }

    /// <summary>
    /// The library measures the display in character cells, so the size
    /// is the window's pixels divided by the fixed font's cell.
    /// </summary>
    public int Width => Math.Max((int)(_pixelWidth / _glyphs.CellWidth), 1);

    public int Height => Math.Max((int)(_pixelHeight / _glyphs.CellHeight), 1);

    /// <summary>
    /// [glk #window_graphics] How many pixels a cell is, which is what
    /// turns the library's layout into places on the screen.
    /// </summary>
    public int CellWidth => Math.Max((int)Math.Round(_glyphs.CellWidth), 1);

    public int CellHeight => Math.Max((int)Math.Round(_glyphs.CellHeight), 1);

    /// <summary>
    /// The text of a buffer window, made the first time it is printed
    /// to. Read under <see cref="Sync"/>, as everything here is.
    /// </summary>
    public BufferText Text(GlkWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!_buffers.TryGetValue(window, out var text))
        {
            text = new BufferText(Glyphs(window));
            _buffers[window] = text;
        }

        return text;
    }

    /// <summary>
    /// [glk #stream_style_hints] The fonts as one window sees them,
    /// bound to the style hints that window was opened with, which are
    /// the only ones that will ever reach it.
    /// </summary>
    public IGlyphs Glyphs(GlkWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!_fonts.TryGetValue(window, out var fonts))
        {
            fonts = _glyphs.Bound(window.Type, window.Styles);
            _fonts[window] = fonts;
        }

        return fonts;
    }

    /// <summary>
    /// [glk #stream_style_check] What a style comes out looking like in
    /// a window, which is the same answer the control paints it with,
    /// so a game that asks is told what it will actually see.
    /// </summary>
    public GlkAppearance? Appearance(GlkWindow window, GlkStyle style)
    {
        ArgumentNullException.ThrowIfNull(window);

        // A blank or graphics window shows no text, so there is nothing
        // to tell the game about.
        if (window.Type is not (WindowType.TextBuffer or WindowType.TextGrid))
        {
            return null;
        }

        lock (Sync)
        {
            return Glyphs(window).Look(style);
        }
    }

    public void Print(GlkWindow window, uint character, GlkStyle style, uint link)
    {
        lock (Sync)
        {
            Text(window).Put(character, style, link);
        }

        _repaint();
    }

    public void Clear(GlkWindow window)
    {
        lock (Sync)
        {
            if (_buffers.TryGetValue(window, out var text))
            {
                text.Clear();
            }
        }

        _repaint();
    }

    /// <summary>
    /// [glk #graphics_testing] Pictures can be drawn in a graphics
    /// window, whose canvas this paints, and [glk #graphics_textbuf] in
    /// the run of a text buffer's text, which this lays out.
    /// </summary>
    public bool CanDrawImages(WindowType type) => type is WindowType.Graphics or WindowType.TextBuffer;

    /// <summary>
    /// [glk #graphics_textbuf] A picture in the run of a text buffer's
    /// text, which goes into the text and is laid out with it.
    /// </summary>
    /// <remarks>
    /// The size is settled when the text is laid out rather than here,
    /// since a picture measured against the window's width is a
    /// different size in a window of a different size, and the
    /// specification says it resizes when the window does.
    /// </remarks>
    public bool DrawImage(GlkWindow window, uint image, Pixels picture, ImageAlign align, ImageSizing sizing, uint link)
    {
        ArgumentNullException.ThrowIfNull(window);

        bool placed;
        lock (Sync)
        {
            placed = Text(window).Draw(picture, align, sizing, link);
        }

        _repaint();
        return placed;
    }

    /// <summary>
    /// [glk op:window_flow_break] A mark in the text that takes it down
    /// below whatever margin pictures it is standing aside for.
    /// </summary>
    public void FlowBreak(GlkWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        lock (Sync)
        {
            Text(window).FlowBreak();
        }

        _repaint();
    }

    /// <summary>
    /// [glk #mouse_events] A window can be touched where there is a
    /// pointer to touch it with, and the specification allows it only
    /// in these two kinds.
    /// </summary>
    public bool CanReportMouse(WindowType type) => type is WindowType.TextGrid or WindowType.Graphics;

    /// <summary>
    /// [glk #link_testing] A link can be selected wherever text is
    /// shown, which is where a link can be printed in the first place.
    /// </summary>
    public bool CanReportHyperlinks(WindowType type) => type is WindowType.TextBuffer or WindowType.TextGrid;

    /// <summary>
    /// [glk #window_graphics] The game painted on a canvas, so the
    /// control has something new to show.
    /// </summary>
    public void Drawn(GlkWindow window) => _repaint();

    public void Arranged(GlkWindow? root)
    {
        lock (Sync)
        {
            Root = root;
        }

        _repaint();
    }

    public GlkInput WaitForInput(IReadOnlyList<GlkWindow> lineRequests, IReadOnlyList<GlkWindow> charRequests, TimeSpan? timeout)
    {
        ArgumentNullException.ThrowIfNull(lineRequests);
        ArgumentNullException.ThrowIfNull(charRequests);

        lock (Sync)
        {
            if (_resized)
            {
                // [glk #arrange_events] A change of size is reported
                // before any input, so the game lays itself out for the
                // new one before it asks for anything.
                _resized = false;
                return GlkInput.Arrange;
            }
        }

        if (lineRequests.Count > 0)
        {
            return ReadLine(lineRequests[0], timeout);
        }

        if (charRequests.Count > 0)
        {
            return ReadKey(charRequests[0], timeout);
        }

        // Nothing to type into. [glk #mouse_events] and
        // [glk #link_events] A window may still be waiting to be
        // touched or to have a link selected, which is as much
        // something to wait for as a key is. Failing that, only the
        // timer can end the wait, and with no timer either there is
        // nothing to wait for at all.
        _repaint();

        if (timeout is null && !Pointing())
        {
            return GlkInput.Ended;
        }

        var idle = new Deadline(timeout);

        while (true)
        {
            if (!Take(idle, out var press))
            {
                return GlkInput.Timer;
            }

            if (press.Kind == PressKind.Point && press.Input is { } pointed)
            {
                return pointed;
            }

            if (press.Kind == PressKind.Wake)
            {
                return GlkInput.Woken;
            }
        }
    }

    /// <summary>
    /// [glk #mouse_events] and [glk #link_events] Whether any window is
    /// waiting to be touched or to have a link selected.
    /// </summary>
    private bool Pointing()
    {
        lock (Sync)
        {
            return Leaves(Root).Any(window => window.MouseRequest || window.HyperlinkRequest);
        }
    }

    private static IEnumerable<GlkWindow> Leaves(GlkWindow? window) => window switch
    {
        null => [],
        PairWindow pair => Leaves(pair.First).Concat(Leaves(pair.Second)),
        _ => [window],
    };

    public void Wake() => _presses.Add(new Press(PressKind.Wake, 0, ""));

    /// <summary>
    /// [glk #mouse_events] and [glk #link_events] The player touched a
    /// window or selected a link in one, worked out by whatever knows
    /// where the pointer landed.
    /// </summary>
    public void Point(GlkInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _presses.Add(new Press(PressKind.Point, 0, "", input));
    }

    /// <summary>The window was made a different size.</summary>
    public void Resize(double width, double height)
    {
        lock (Sync)
        {
            if (Math.Abs(width - _pixelWidth) < 0.5 && Math.Abs(height - _pixelHeight) < 0.5)
            {
                return;
            }

            _pixelWidth = width;
            _pixelHeight = height;
            _resized = true;
        }

        // A resize can arrive while the game is waiting for a key, and
        // the wait has to end so the game hears about it.
        _presses.Add(new Press(PressKind.Wake, 0, ""));
    }

    /// <summary>A key the player pressed, as a Glk key code.</summary>
    public void Key(uint key) => _presses.Add(new Press(PressKind.Key, key, ""));

    /// <summary>Characters the player typed or pasted.</summary>
    public void Typed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _presses.Add(new Press(PressKind.Text, 0, text));
    }

    // [glk #line_events] A line, edited here as it is typed, and given
    // to the game when the player presses enter.
    private GlkInput ReadLine(GlkWindow window, TimeSpan? timeout)
    {
        lock (Sync)
        {
            Typing = window;

            // [glk #line_events] A wait that was cut short by a resize
            // or by a link leaves the game to ask for the line again,
            // and the player finds what they had typed still there. The
            // game's own initial text wins wherever it gave any.
            var initial = window.LineRequest?.Initial ?? "";
            if (initial.Length == 0 && ReferenceEquals(_interrupted, window))
            {
                initial = _typing.ToString();
            }

            _interrupted = null;
            _typing.Clear();
            _typing.Append(initial);
            Show(window);
        }

        _repaint();
        var waiting = new Deadline(timeout);

        while (true)
        {
            if (!Take(waiting, out var press))
            {
                Finish(window);
                return GlkInput.Timer;
            }

            if (press.Kind == PressKind.Point && press.Input is { } pointed)
            {
                Interrupt(window);
                return pointed;
            }

            if (press.Kind == PressKind.Wake)
            {
                lock (Sync)
                {
                    if (_resized)
                    {
                        _resized = false;
                        Interrupt(window);
                        return GlkInput.Arrange;
                    }
                }

                Interrupt(window);
                return GlkInput.Woken;
            }

            if (press.Kind == PressKind.Text)
            {
                lock (Sync)
                {
                    foreach (var character in press.Text)
                    {
                        // A pasted line ending is the end of the line,
                        // and anything after it waits for the next one.
                        if (character is '\n' or '\r')
                        {
                            return Complete(window);
                        }

                        if (!char.IsControl(character))
                        {
                            _typing.Append(character);
                        }
                    }

                    Show(window);
                }

                _repaint();
                continue;
            }

            switch (press.Key)
            {
                case GlkKeyCode.Return:
                    return Complete(window);

                case GlkKeyCode.Delete:
                    lock (Sync)
                    {
                        if (_typing.Length > 0)
                        {
                            _typing.Length--;
                        }

                        Show(window);
                    }

                    _repaint();
                    break;

                default:
                    // Every other special key is left alone in this
                    // frontend, which has no line terminators yet.
                    break;
            }
        }
    }

    private GlkInput ReadKey(GlkWindow window, TimeSpan? timeout)
    {
        _repaint();
        var waiting = new Deadline(timeout);

        while (true)
        {
            if (!Take(waiting, out var press))
            {
                return GlkInput.Timer;
            }

            switch (press.Kind)
            {
                case PressKind.Wake:
                    lock (Sync)
                    {
                        if (_resized)
                        {
                            _resized = false;
                            return GlkInput.Arrange;
                        }
                    }

                    return GlkInput.Woken;

                case PressKind.Point when press.Input is { } pointed:
                    return pointed;

                case PressKind.Text when press.Text.Length > 0:
                    return GlkInput.KeyPress(window, press.Text[0]);

                case PressKind.Key:
                    return GlkInput.KeyPress(window, press.Key);

                default:
                    break;
            }
        }
    }

    private GlkInput Complete(GlkWindow window)
    {
        string line;
        lock (Sync)
        {
            line = _typing.ToString();
            _interrupted = null;
            Finish(window);

            // [glk #line_events] The line and the newline after it are
            // part of what the window shows, so they go into the text
            // now that they are no longer being edited.
            if (window.EchoLineInput && window.Type == WindowType.TextBuffer)
            {
                var text = Text(window);
                foreach (var character in line)
                {
                    text.Put(character, GlkStyle.Input, 0);
                }

                text.Put('\n', GlkStyle.Input, 0);
            }
        }

        _repaint();
        return GlkInput.Line(window, line);
    }

    /// <summary>
    /// Ends the line request without throwing away what the player had
    /// typed, for a wait that something other than the enter key cut
    /// short.
    /// </summary>
    private void Interrupt(GlkWindow window)
    {
        _interrupted = window;
        Finish(window);
    }

    private void Finish(GlkWindow window)
    {
        Typing = null;
        if (window.Type == WindowType.TextBuffer)
        {
            Text(window).Pending = "";
        }
    }

    private void Show(GlkWindow window)
    {
        if (window.Type == WindowType.TextBuffer)
        {
            Text(window).Pending = _typing.ToString();
        }
    }

    private bool Take(Deadline deadline, out Press press)
    {
        if (deadline.Forever)
        {
            press = _presses.Take();
            return true;
        }

        var left = deadline.Left;
        if (left <= TimeSpan.Zero)
        {
            press = default;
            return false;
        }

        return _presses.TryTake(out press!, left);
    }

    private enum PressKind
    {
        Key,
        Text,
        Wake,
        Point,
    }

    private readonly record struct Press(PressKind Kind, uint Key, string Text, GlkInput? Input = null);

    /// <summary>
    /// [glk #timer_events] How long is left of a wait, or forever when
    /// there is no timer.
    /// </summary>
    private readonly struct Deadline(TimeSpan? timeout)
    {
        private readonly long _until = timeout is { } span
            ? Environment.TickCount64 + (long)span.TotalMilliseconds
            : 0;

        public bool Forever { get; } = timeout is null;

        public TimeSpan Left => TimeSpan.FromMilliseconds(Math.Max(_until - Environment.TickCount64, 0));
    }
}

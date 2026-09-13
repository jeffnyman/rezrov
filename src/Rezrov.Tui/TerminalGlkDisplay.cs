using System.Collections.Concurrent;
using System.Text;
using Rezrov.Glulx.Glk;
using Rezrov.ZMachine.Screen;
using GlkWindowType = Rezrov.Glulx.Glk.WindowType;

namespace Rezrov.Tui;

/// <summary>
/// The Glk display on a terminal: the windows painted on a
/// <see cref="GlkScreen"/>, and the player's keys taken from a queue
/// the view fills.
/// </summary>
/// <remarks>
/// The interpreter runs on its own thread and calls in here to print
/// and to wait; the view paints on the toolkit's thread and reads the
/// cells under <see cref="Sync"/>. Line input is edited here, since
/// [glk #line_events] showing the line as it is typed is the display's
/// job: in a text buffer window the characters go into the buffer as
/// they are typed, and in a text grid, whose cells belong to the
/// library, they are laid over the grid until the line is done. A
/// text buffer that fills with more than a screen of text between two
/// inputs pauses with a prompt, as the Z-machine screen does, so
/// nothing scrolls past unread.
/// </remarks>
public sealed class TerminalGlkDisplay : IGlkDisplay, ITerminalPicture
{
    private readonly BlockingCollection<uint> _keys = [];
    private readonly Dictionary<GlkWindow, int> _marks = [];
    private readonly Dictionary<GlkWindow, StringBuilder> _partialLines = [];
    private readonly Action _repaint;
    private readonly TextReader? _commands;
    private readonly TextAttributes _normal;
    private bool _resized;

    /// <param name="width">The terminal's width in cells.</param>
    /// <param name="height">The terminal's height in cells.</param>
    /// <param name="repaint">Asks the view to draw again.</param>
    /// <param name="commands">
    /// Lines to give the game before the player types, or null.
    /// </param>
    public TerminalGlkDisplay(int width, int height, Action repaint, TextReader? commands = null)
    {
        ArgumentNullException.ThrowIfNull(repaint);

        _repaint = repaint;
        _commands = commands;
        _normal = new TextAttributes(ZMachine.Screen.TextStyle.Roman, ScreenColor.White, ScreenColor.Black, TextAttributes.NormalFont);
        Screen = new GlkScreen(width, height, _normal);
    }

    /// <summary>The picture of the windows.</summary>
    public GlkScreen Screen { get; }

    public object Sync { get; } = new();

    public int Width => Screen.Width;

    public int Height => Screen.Height;

    public Cell this[int row, int column] => Screen[row, column];

    public (int Row, int Column)? Cursor { get; private set; }

    /// <summary>How many times a window paused for a key.</summary>
    public int MorePrompts { get; private set; }

    /// <summary>A key from the player, as a Glk key code.</summary>
    public void Enqueue(uint key) => _keys.Add(key);

    /// <summary>Waits for any key at all.</summary>
    public uint WaitForAnyKey() => _keys.Take();

    public void Repaint() => Screen.Repaint();

    public void Resize(int width, int height)
    {
        lock (Sync)
        {
            Screen.Resize(width, height);
            _resized = true;
        }

        _repaint();
    }

    public void Print(GlkWindow window, uint character, GlkStyle style)
    {
        ArgumentNullException.ThrowIfNull(window);

        lock (Sync)
        {
            Screen.Print(window, character, style);
        }

        // A text buffer that has shown a window's worth of new lines
        // since the player last had a turn waits before showing more.
        if (window.Type == GlkWindowType.TextBuffer && window.Height > 1
            && Screen.LineCount(window) - Mark(window) >= window.Height - 1)
        {
            MorePrompt(window);
        }

        _repaint();
    }

    public void Clear(GlkWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        lock (Sync)
        {
            Screen.Clear(window);
            _marks[window] = Screen.LineCount(window);
        }

        _repaint();
    }

    public void Arranged(GlkWindow? root)
    {
        lock (Sync)
        {
            Screen.Arranged(root);
            foreach (var window in _marks.Keys.Where(w => Screen.LineCount(w) == 0).ToList())
            {
                _marks.Remove(window);
            }
        }

        _repaint();
    }

    public GlkInput WaitForInput(IReadOnlyList<GlkWindow> lineRequests, IReadOnlyList<GlkWindow> charRequests, TimeSpan? timeout)
    {
        ArgumentNullException.ThrowIfNull(lineRequests);
        ArgumentNullException.ThrowIfNull(charRequests);

        lock (Sync)
        {
            // The player has caught up with everything printed.
            foreach (var window in _marks.Keys.ToList())
            {
                _marks[window] = Screen.LineCount(window);
            }

            if (_resized)
            {
                // [glk #arrange_events] A change of size is reported before
                // any input, so the game sees the new layout first.
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
            ShowCursor(charRequests[0]);
            return TryTake(timeout, out var key) ? GlkInput.KeyPress(charRequests[0], key) : GlkInput.Timer;
        }

        // [glk #timer_events] Nothing to type into: keys pressed now mean
        // nothing, and only the timer can end the wait.
        Cursor = null;
        _repaint();
        if (timeout is { } wait)
        {
            var deadline = DateTime.UtcNow + wait;
            while (_keys.TryTake(out _, deadline - DateTime.UtcNow))
            {
            }

            return GlkInput.Timer;
        }

        return GlkInput.Ended;
    }

    /// <summary>
    /// Says something to the player in the terminal's bottom row and
    /// waits for a key: how the game ended, once it has.
    /// </summary>
    public void Notice(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        lock (Sync)
        {
            Screen.Overlay = (Height - 1, 0, Cells(text, _normal with { Style = ZMachine.Screen.TextStyle.ReverseVideo }));
            Cursor = null;
        }

        _repaint();
        WaitForAnyKey();
    }

    private GlkInput ReadLine(GlkWindow window, TimeSpan? timeout)
    {
        var request = window.LineRequest!;
        var isBuffer = window.Type == GlkWindowType.TextBuffer;

        // A line the timer interrupted is picked up where it was left.
        if (!_partialLines.TryGetValue(window, out var text))
        {
            text = new StringBuilder(request.Initial);
            _partialLines[window] = text;
            if (isBuffer)
            {
                Echo(window, request.Initial);
            }
        }

        // A commands file types the line all at once.
        if (_commands?.ReadLine() is { } scripted)
        {
            text.Append(scripted);
            if (isBuffer)
            {
                Echo(window, scripted);
            }

            return Finish(window, text, 0);
        }

        while (true)
        {
            ShowCursor(window, text.ToString());
            if (!TryTake(timeout, out var key))
            {
                return GlkInput.Timer;
            }

            if (key == GlkKeyCode.Return)
            {
                return Finish(window, text, 0);
            }

            // [glk op:set_terminators_line_event] A special key the game
            // asked for ends the line and is reported with it.
            if (window.LineTerminators.Contains(key))
            {
                return Finish(window, text, key);
            }

            if (key == GlkKeyCode.Delete)
            {
                if (text.Length > 0)
                {
                    text.Length--;
                    if (isBuffer)
                    {
                        lock (Sync)
                        {
                            Screen.Backspace(window);
                        }
                    }
                }

                continue;
            }

            if (!GlkKeyCode.IsSpecial(key) && key <= 0x10FFFF && text.Length < request.MaxLength)
            {
                var typed = char.ConvertFromUtf32((int)key);
                text.Append(typed);
                if (isBuffer)
                {
                    Echo(window, typed);
                }
            }
        }
    }

    private GlkInput Finish(GlkWindow window, StringBuilder text, uint terminator)
    {
        _partialLines.Remove(window);
        lock (Sync)
        {
            Screen.Overlay = null;

            // [glk #line_events] The finished line is followed by a new
            // line in a text buffer, unless the game turned echoing off;
            // a text grid gets the line from the library itself.
            if (window.Type == GlkWindowType.TextBuffer && window.EchoLineInput)
            {
                Screen.Print(window, '\n', GlkStyle.Input);
            }

            _marks[window] = Screen.LineCount(window);
            Cursor = Screen.CursorFor(window);
        }

        _repaint();
        return GlkInput.Line(window, text.ToString(), terminator);
    }

    private void Echo(GlkWindow window, string text)
    {
        lock (Sync)
        {
            foreach (var rune in text.EnumerateRunes())
            {
                Screen.Print(window, (uint)rune.Value, GlkStyle.Input);
            }
        }
    }

    /// <summary>
    /// Puts the cursor at the window's input point, with the line typed
    /// so far laid over a text grid, and asks for a draw.
    /// </summary>
    private void ShowCursor(GlkWindow window, string typed = "")
    {
        lock (Sync)
        {
            var cursor = Screen.CursorFor(window);
            if (window.Type == GlkWindowType.TextGrid && cursor is { } at)
            {
                Screen.Overlay = typed.Length == 0 ? null : (at.Row, at.Column, Cells(typed, GlkScreen.Attributes(GlkStyle.Input, _normal)));
                cursor = (at.Row, Math.Min(at.Column + typed.Length, Width - 1));
            }

            Cursor = cursor;
        }

        _repaint();
    }

    private void MorePrompt(GlkWindow window)
    {
        lock (Sync)
        {
            Screen.Overlay = (window.Top + window.Height - 1, window.Left, Cells("[MORE]", _normal with { Style = ZMachine.Screen.TextStyle.ReverseVideo }));
            Cursor = null;
        }

        _repaint();
        MorePrompts++;
        WaitForAnyKey();

        lock (Sync)
        {
            Screen.Overlay = null;
            _marks[window] = Screen.LineCount(window);
        }
    }

    private int Mark(GlkWindow window)
    {
        lock (Sync)
        {
            // A window seen for the first time has been read up to now.
            if (!_marks.TryGetValue(window, out var mark))
            {
                mark = Screen.LineCount(window);
                _marks[window] = mark;
            }

            return mark;
        }
    }

    private bool TryTake(TimeSpan? timeout, out uint key)
    {
        if (timeout is { } wait)
        {
            return _keys.TryTake(out key, wait);
        }

        key = _keys.Take();
        return true;
    }

    private static Cell[] Cells(string text, TextAttributes attributes) =>
        [.. text.Select(c => new Cell(c, attributes))];
}

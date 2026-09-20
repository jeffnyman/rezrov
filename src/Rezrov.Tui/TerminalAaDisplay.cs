using System.Collections.Concurrent;
using System.Text;
using Rezrov.AaMachine;
using Rezrov.AaMachine.Execution;
using Rezrov.ZMachine.Screen;

namespace Rezrov.Tui;

/// <summary>
/// An Aa-machine story shown on the terminal.
/// </summary>
/// <remarks>
/// [aam output] This is the whole of the machine's output model
/// brought down to a grid of characters: divs become margins and
/// alignment, spans and divs alike carry a style class, and the top
/// status area becomes rows across the top of the screen.
///
/// What a terminal cannot do it says so about, and the game is told
/// through VM_INFO before it tries: there are no pictures, so a
/// resource is shown as the words the story carries in its place, and
/// there are no links, so link text is ordinary text.
///
/// The machine runs on its own thread and blocks here when it wants
/// the player, which is why the presses arrive through a queue.
/// </remarks>
public sealed class TerminalAaDisplay : IAaOutput, ITerminalPicture
{
    private readonly AaStory _story;
    private readonly Action _changed;
    private readonly TextReader? _commands;
    private readonly BlockingCollection<int> _keys = [];
    private readonly List<int> _divs = [];
    private readonly List<int> _spans = [];
    private readonly StringBuilder _floated = new();

    private int _newlines = 1;
    private bool _inStatus;
    private bool _floating;
    private bool _uppercase;

    public TerminalAaDisplay(AaStory story, int width, int height, Action changed, TextReader? commands = null)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(changed);

        _story = story;
        _changed = changed;
        _commands = commands;

        Screen = new AaScreen(width, height, Normal);
        Screen.StatusAttributes = Normal with { Style = TextStyle.ReverseVideo };
    }

    /// <summary>The picture the view paints.</summary>
    public AaScreen Screen { get; }

    public object Sync { get; } = new();

    public int Width => Screen.Width;

    public int Height => Screen.Height;

    public Cell this[int row, int column] => Screen[row, column];

    public (int Row, int Column)? Cursor => Screen.Cursor;

    // A terminal can tell bold from italic and can color text, and it
    // can put a line where the style sheet asks. It cannot draw, so
    // there are no pictures and nothing to click.
    public bool HasLinks => false;

    public bool HasStyles => true;

    public bool HasColor => true;

    public bool HasAlignment => true;

    public bool IsScripting => false;

    /// <summary>The look of plain text.</summary>
    private static TextAttributes Normal =>
        new(TextStyle.Roman, ScreenColor.Default, ScreenColor.Default, TextAttributes.NormalFont);

    private TextPane Pane => _inStatus ? Screen.Status : Screen.Main;

    public void Repaint()
    {
        lock (Sync)
        {
            Screen.Paint();
        }
    }

    public void Resize(int width, int height)
    {
        lock (Sync)
        {
            Screen.Resize(width, height);
        }

        _changed();
    }

    /// <summary>A key the player pressed.</summary>
    public void Enqueue(int key) => _keys.Add(key);

    /// <summary>
    /// The next line the player types, or the next line of the command
    /// file while there is one. The characters are echoed as they are
    /// typed, since the game's own cursor is where they appear.
    /// </summary>
    public string ReadLine()
    {
        if (_commands?.ReadLine() is { } scripted)
        {
            Write(scripted);
            Newline();
            return scripted;
        }

        var typed = new StringBuilder();

        while (true)
        {
            var key = _keys.Take();

            if (key is '\r' or '\n')
            {
                Newline();
                return typed.ToString();
            }

            if (key == '\b')
            {
                if (typed.Length > 0)
                {
                    typed.Length--;

                    lock (Sync)
                    {
                        Pane.Backspace();
                    }

                    _changed();
                }

                continue;
            }

            if (key is >= ' ' and < 0x110000)
            {
                typed.Append((char)key);
                Write(((char)key).ToString());
            }
        }
    }

    /// <summary>
    /// The next single key the player presses. A command file gives
    /// one key per line, the first character of it, so that a script
    /// stays in step whether the game wants a line or a key; an empty
    /// line is the return key, which is what most games wait for.
    /// </summary>
    public int ReadKey()
    {
        if (_commands?.ReadLine() is { } scripted)
        {
            return scripted.Length > 0 ? scripted[0] : AaKeyMap.Return;
        }

        return _keys.Take();
    }

    public void Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return;
        }

        if (_floating)
        {
            _floated.Append(_uppercase ? Shout(text) : text);
            return;
        }

        lock (Sync)
        {
            var attributes = Attributes();

            foreach (var character in _uppercase ? Shout(text) : text)
            {
                Pane.Put(character, attributes);
            }
        }

        _newlines = 0;
        _changed();
    }

    // [aam opcode] A space that is not a place to break a line,
    // which the pane only breaks at an ordinary one.
    public void NoBreakSpace() => Write("\u00a0");

    public void Space() => Write(" ");

    public void Spaces(int count) => Write(new string(' ', Math.Clamp(count, 0, Width)));

    public void Newline() => VerticalSpace(0);

    public void EndParagraph() => VerticalSpace(1);

    public void EnterDiv(int styleClass)
    {
        _divs.Add(styleClass);

        if (_story.Styles.FloatsRight(styleClass))
        {
            // [aam output] A floated div is set aside rather than laid
            // in the text, since nothing here can flow one around the
            // other.
            _floating = true;
            _floated.Clear();
            return;
        }

        VerticalSpace(_story.Styles.Ems(styleClass, "margin-top", 0));
        LayOut();
    }

    public void LeaveDiv(int styleClass)
    {
        if (_divs.Count > 0)
        {
            _divs.RemoveAt(_divs.Count - 1);
        }

        if (_floating)
        {
            _floating = false;

            lock (Sync)
            {
                Screen.Floated = _floated.ToString().Trim();
            }

            _changed();
            return;
        }

        VerticalSpace(_story.Styles.Ems(styleClass, "margin-bottom", 0));
        LayOut();
    }

    public void EnterSpan(int styleClass) => _spans.Add(styleClass);

    public void LeaveSpan()
    {
        if (_spans.Count > 0)
        {
            _spans.RemoveAt(_spans.Count - 1);
        }
    }

    public void SetBody(int styleClass)
    {
    }

    public void EnterStatus(int area, int styleClass)
    {
        // [aam opcode] Entering a status area empties it. Only the top
        // one is shown; anything written to another goes into the
        // main text, which is where an inline area would appear.
        if (area != 0)
        {
            return;
        }

        lock (Sync)
        {
            Screen.Status.Clear();
            Screen.Floated = string.Empty;
            Screen.StatusRows = Math.Max(_story.Styles.Ems(styleClass, "height", 1), 1);
        }

        _inStatus = true;
        _newlines = 1;
        _divs.Add(styleClass);
        LayOut();
    }

    public void LeaveStatus()
    {
        if (!_inStatus)
        {
            return;
        }

        if (_divs.Count > 0)
        {
            _divs.RemoveAt(_divs.Count - 1);
        }

        _inStatus = false;
        _floating = false;
        _newlines = 1;

        _changed();
    }

    public void EnterSelfLink()
    {
    }

    public void LeaveSelfLink()
    {
    }

    public void EnterLink(string input)
    {
    }

    public void LeaveLink()
    {
    }

    public void EnterLinkResource(int resource)
    {
    }

    public void LeaveLinkResource()
    {
    }

    public void EmbedResource(int resource)
    {
        // Nothing here can draw a picture, so the words the story
        // carries in its place are the next best thing.
        Write("[");
        Write(AltText(resource));
        Write("]");
    }

    public bool CanEmbedResource(int resource) => false;

    public void ProgressBar(int amount, int total)
    {
        var full = Math.Max(Width - 3, 1);
        var filled = total == 0 ? 0 : (int)Math.Floor((full * ((double)amount / total)) + 0.5);

        EndParagraph();
        Write("[" + new string('=', Math.Clamp(filled, 0, full)) + new string(' ', Math.Clamp(full - filled, 0, full)) + "]");
        EndParagraph();
    }

    public void SetStyle(int bits)
    {
    }

    public void ResetStyle(int bits)
    {
    }

    public void Unstyle()
    {
    }

    public void Clear()
    {
        lock (Sync)
        {
            Screen.Main.Clear();
        }

        _newlines = 1;
        _changed();
    }

    public void ClearStatus()
    {
        lock (Sync)
        {
            Screen.Status.Clear();
            Screen.Floated = string.Empty;
            Screen.StatusRows = 0;
        }

        _changed();
    }

    public void ClearAll()
    {
        Clear();
        ClearStatus();
    }

    public void ClearLinks()
    {
    }

    public void ClearDiv()
    {
    }

    public void ClearOld()
    {
    }

    public void LeaveAll()
    {
        Newline();

        _inStatus = false;
        _floating = false;
        _divs.Clear();
        _spans.Clear();
    }

    public void Restart()
    {
        lock (Sync)
        {
            Screen.Main.Clear();
            Screen.Status.Clear();
            Screen.Floated = string.Empty;
            Screen.StatusRows = 0;
        }

        _newlines = 1;
        _inStatus = false;
        _floating = false;
        _divs.Clear();
        _spans.Clear();
        _changed();
    }

    // The picture's lock is called Sync too, so the machine's call to
    // make sure everything is visible is named only on the interface.
    void IAaOutput.Sync() => _changed();

    public int Measure(int which) => which switch
    {
        0 => Width,
        1 => _inStatus ? Screen.StatusRows : Math.Max(Height - Screen.StatusRows, 0),
        _ => 0,
    };

    public bool ScriptOn() => false;

    public void ScriptOff()
    {
    }

    /// <summary>
    /// A note from the interpreter rather than from the game, which
    /// goes in the text where the player will see it.
    /// </summary>
    public void Notice(string text)
    {
        EndParagraph();
        Write(text);
        Newline();
    }

    private string AltText(int resource) =>
        resource >= 0 && resource < _story.Resources.Count
            ? _story.Text.At(_story.Resources[resource].AltText)
            : string.Empty;

    private static string Shout(string text) => text.ToUpperInvariant();

    // The look of the text being written now: heavier or leaning if
    // any class around it asks, and reversed throughout the status
    // area so that it reads as a bar.
    private TextAttributes Attributes()
    {
        var attributes = _inStatus ? Screen.StatusAttributes : Normal;
        var style = attributes.Style;

        foreach (var styleClass in _divs.Concat(_spans))
        {
            if (_story.Styles.IsBold(styleClass))
            {
                style |= TextStyle.Bold;
            }

            if (_story.Styles.IsItalic(styleClass))
            {
                style |= TextStyle.Italic;
            }
        }

        return attributes with { Style = style };
    }

    // The margins and the alignment of the paragraph being written,
    // which come from the innermost div that asks for any.
    private void LayOut()
    {
        var left = 0;
        var right = 0;
        var alignment = PaneAlignment.Start;
        var shout = false;

        foreach (var styleClass in _divs)
        {
            left += _story.Styles.Ems(styleClass, "margin-left", 0);
            right += _story.Styles.Ems(styleClass, "margin-right", 0);
            shout |= _story.Styles.IsUppercase(styleClass);

            alignment = _story.Styles.Alignment(styleClass) switch
            {
                AaAlignment.Center => PaneAlignment.Center,
                AaAlignment.End => PaneAlignment.End,
                _ => alignment,
            };

            // [aam story] A width in characters is a width, and what
            // is left over is margin on the right.
            if (_story.Styles.Characters(styleClass, "width") is { } characters && characters < Width)
            {
                right = Math.Max(right, Width - left - characters);
            }
        }

        _uppercase = shout;

        lock (Sync)
        {
            Pane.Layout(left, right, alignment);
        }
    }

    private void VerticalSpace(int blank)
    {
        lock (Sync)
        {
            while (_newlines < blank + 1)
            {
                Pane.Put('\n', Attributes());
                _newlines++;
            }
        }

        LayOut();
        _changed();
    }
}

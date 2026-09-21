using System.Collections.Concurrent;
using System.Text;
using Rezrov.AaMachine;
using Rezrov.AaMachine.Execution;
using Rezrov.Core.Graphics;

namespace Rezrov.Gui;

/// <summary>
/// [aam output] An Aa-machine story shown in a window.
/// </summary>
/// <remarks>
/// This is the first frontend that can do what the machine's output
/// model actually asks for. A style sheet names faces and sizes and
/// colors and draws boxes around things, and a window has all of
/// those; a story carries pictures, and a window can draw them; a
/// story marks words as links, and a window can be clicked. So the
/// answers it gives through VM_INFO are all yes, and the games take
/// their better paths accordingly.
///
/// The text is kept in two pages, the main one and the status area
/// across the top, each of which lays itself out at whatever width the
/// window gives it. Nothing here paints: what comes out is a list of
/// boxes and a list of lines, and <see cref="Board"/> puts them on the
/// screen.
///
/// The machine runs on its own thread and blocks here when it wants
/// the player, which is why the presses arrive through a queue.
/// </remarks>
public sealed class GuiAaDisplay : IAaOutput
{
    /// <summary>
    /// [aam output] How wide the column of text is allowed to become,
    /// in characters, however wide the window is. A line of prose that
    /// runs the width of a large screen is hard to read back to, and
    /// the reference interpreter caps its own column at the same
    /// measure.
    /// </summary>
    private const int Columns = 60;

    private readonly AaStory _story;
    private readonly IAaGlyphs _glyphs;
    private readonly AaSheet _sheet;
    private readonly Action _changed;
    private readonly Func<TextWriter?> _transcripts;
    private readonly BlockingCollection<int> _keys = [];
    private readonly Dictionary<int, Pixels?> _pictures = [];
    private readonly List<string> _links = [];
    private readonly StringBuilder _self = new();

    private TextOutput? _transcript;
    private TextWriter? _written;
    private double _width;
    private double _height;
    private int _area = -1;
    private int _dead;
    private bool _selfLink;

    /// <param name="story">The story being played.</param>
    /// <param name="glyphs">The faces to measure and draw with.</param>
    /// <param name="plain">How text is set where no class says otherwise.</param>
    /// <param name="changed">What to call when the screen should be painted again.</param>
    /// <param name="transcripts">
    /// How to open a transcript when the story asks for one, or a
    /// function returning null where the player declined.
    /// </param>
    public GuiAaDisplay(
        AaStory story,
        IAaGlyphs glyphs,
        AaLook plain,
        Action changed,
        Func<TextWriter?> transcripts)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(changed);
        ArgumentNullException.ThrowIfNull(transcripts);

        _story = story;
        _glyphs = glyphs;
        _changed = changed;
        _transcripts = transcripts;
        _sheet = new AaSheet(story.Styles, glyphs, plain);

        Main = new AaText(glyphs, _sheet);
        Status = new AaText(glyphs, _sheet);
        Background = AaTheme.Paper;
    }

    /// <summary>The main run of the story's text.</summary>
    public AaText Main { get; }

    /// <summary>[aam opcode] The status area across the top.</summary>
    public AaText Status { get; }

    /// <summary>
    /// [aam opcode] What the whole page is painted on, which a story
    /// may set along with the color of its text.
    /// </summary>
    public uint Background { get; private set; }

    /// <summary>
    /// How wide the column of text is: as much of the window as the
    /// measure allows.
    /// </summary>
    public double Column => Math.Max(
        Math.Min(_width, Columns * _glyphs.CharacterWidth(_sheet.Plain)),
        1);

    /// <summary>How far from the left of the window the column sits.</summary>
    public double Left => Math.Max((_width - Column) / 2, 0);

    /// <summary>
    /// How tall the status area is, which is nought while the story
    /// has not made one.
    /// </summary>
    public double StatusHeight => Status.Height(Column);

    /// <summary>How tall the main text has room to be.</summary>
    public double MainHeight => Math.Max(_height - StatusHeight - Rule, 0);

    /// <summary>
    /// [aam output] The line drawn under the status area, which the
    /// reference interpreter draws to set it off from the text below.
    /// </summary>
    public double Rule => StatusHeight > 0 ? Math.Max(Math.Round(_sheet.Plain.Size / 10), 1) : 0;

    // A window can show a picture, can tell one face from another, can
    // color text, can put a line where the style sheet asks, and can
    // be clicked. There is nothing in the output model it has to
    // decline.
    public bool HasLinks => true;

    public bool HasStyles => true;

    public bool HasColor => true;

    public bool HasAlignment => true;

    public bool IsScripting => _transcript is not null;

    private AaText Pane => _area == 0 ? Status : Main;

    /// <summary>The window changed size, so the text lays out again.</summary>
    public void Resize(double width, double height)
    {
        _width = width;
        _height = height;
        _changed();
    }

    /// <summary>A key the player pressed.</summary>
    public void Enqueue(int key) => _keys.Add(key);

    /// <summary>
    /// [aam output] Whether a link is still one: a story may turn the
    /// links it has already shown back into ordinary text, and the
    /// words stay where they are when it does.
    /// </summary>
    public bool IsLive(int link) => link > _dead && link <= _links.Count;

    /// <summary>What a link types when it is used.</summary>
    public string Typed(int link) => IsLive(link) ? _links[link - 1] : string.Empty;

    /// <summary>
    /// The next line the player types, echoed as it is typed, since
    /// the game's own place in the text is where it appears.
    /// </summary>
    public string ReadLine()
    {
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
                    Main.Backspace();
                    _changed();
                }

                continue;
            }

            // [aam output] A link the player clicked arrives as the
            // whole of the line rather than as a key, since what it
            // stands for is a command and not a letter.
            if (key <= -1 && Typed(-key) is { Length: > 0 } clicked)
            {
                Write(clicked);
                Newline();
                return clicked;
            }

            if (key is >= ' ' and < 0x110000)
            {
                typed.Append((char)key);
                Write(((char)key).ToString());
            }
        }
    }

    /// <summary>The next single key the player presses.</summary>
    public int ReadKey()
    {
        while (true)
        {
            var key = _keys.Take();

            // A click on a link is not a key, and a story waiting for
            // one is not waiting for that.
            if (key >= 0)
            {
                return key;
            }
        }
    }

    public void Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return;
        }

        if (_selfLink)
        {
            _self.Append(text.ToLowerInvariant());
        }

        Pane.Put(text);
        _transcript?.Write(text);
        _changed();
    }

    // [aam opcode] A space that is not a place to break a line.
    public void NoBreakSpace()
    {
        Pane.Put(" ");
        _transcript?.NoBreakSpace();
        _changed();
    }

    public void Space() => Write(" ");

    public void Spaces(int count)
    {
        Pane.Put(new string(' ', Math.Clamp(count, 0, 1000)));
        _transcript?.Spaces(count);
        _changed();
    }

    public void Newline()
    {
        Pane.Newline();
        _transcript?.Newline();
        _changed();
    }

    public void EndParagraph()
    {
        Pane.EndParagraph();
        _transcript?.EndParagraph();
        _changed();
    }

    public void EnterDiv(int styleClass)
    {
        Pane.EnterDiv(styleClass);
        _transcript?.EnterDiv(styleClass);
        _changed();
    }

    public void LeaveDiv(int styleClass)
    {
        Pane.LeaveDiv();
        _transcript?.LeaveDiv(styleClass);
        _changed();
    }

    public void EnterSpan(int styleClass)
    {
        Pane.EnterSpan(styleClass);
        _transcript?.EnterSpan(styleClass);
    }

    public void LeaveSpan()
    {
        Pane.LeaveSpan();
        _transcript?.LeaveSpan();
    }

    public void SetBody(int styleClass)
    {
        var plain = _sheet.Inside(_sheet.Plain, styleClass, span: false);

        Background = _story.Styles.BackgroundColor(styleClass) is { IsSet: true } behind
            ? behind.Value
            : AaTheme.Paper;

        Main.SetBody(plain);
        Status.SetBody(plain);
        _transcript?.SetBody(styleClass);
        _changed();
    }

    public void EnterStatus(int area, int styleClass)
    {
        // [aam opcode] A status area is not entered from inside
        // another, and entering one empties it.
        if (_area >= 0)
        {
            return;
        }

        _area = area;

        if (area == 0)
        {
            Status.Clear();
        }

        Pane.EnterDiv(styleClass);
        _transcript?.EnterStatus(area, styleClass);
        _changed();
    }

    public void LeaveStatus()
    {
        if (_area < 0)
        {
            return;
        }

        Pane.LeaveDiv();
        _area = -1;
        _transcript?.LeaveStatus();
        _changed();
    }

    /// <summary>
    /// [aam output] A link that types back the words it is made of,
    /// which is how a game offers a noun it has just mentioned.
    /// </summary>
    public void EnterSelfLink()
    {
        _links.Add(string.Empty);
        _self.Clear();
        _selfLink = true;
        Pane.EnterLink(_links.Count);
        _transcript?.EnterSelfLink();
    }

    public void LeaveSelfLink()
    {
        if (_selfLink)
        {
            _links[^1] = _self.ToString().Trim();
            _selfLink = false;
        }

        Pane.LeaveLink();
        _transcript?.LeaveSelfLink();
    }

    public void EnterLink(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        _links.Add(input);
        Pane.EnterLink(_links.Count);
        _transcript?.EnterLink(input);
    }

    public void LeaveLink()
    {
        Pane.LeaveLink();
        _transcript?.LeaveLink();
    }

    /// <summary>
    /// [aam output] A link to a resource somewhere else, which is a
    /// place on the web rather than a command. Nothing here can go and
    /// fetch one, so the words are shown as ordinary text and the
    /// player is not invited to click something that would do nothing.
    /// </summary>
    public void EnterLinkResource(int resource) => _transcript?.EnterLinkResource(resource);

    public void LeaveLinkResource() => _transcript?.LeaveLinkResource();

    public void EmbedResource(int resource)
    {
        Pane.Draw(Picture(resource), AltText(resource));
        _transcript?.EmbedResource(resource);
        _changed();
    }

    public bool CanEmbedResource(int resource) => Picture(resource) is not null;

    public void ProgressBar(int amount, int total)
    {
        Pane.ProgressBar(amount, total);
        _transcript?.ProgressBar(amount, total);
        _changed();
    }

    // [aam opcode] The style bits the machine kept from before there
    // were style classes. A story that has a style sheet has no use
    // for them, and every story the compiler makes has one.
    public void SetStyle(int bits) => _transcript?.SetStyle(bits);

    public void ResetStyle(int bits) => _transcript?.ResetStyle(bits);

    public void Unstyle() => _transcript?.Unstyle();

    public void Clear()
    {
        Main.Clear();
        _transcript?.Clear();
        _changed();
    }

    public void ClearStatus()
    {
        Status.Clear();
        Status.LeaveAll();
        _transcript?.ClearStatus();
        _changed();
    }

    public void ClearAll()
    {
        Clear();
        ClearStatus();
    }

    public void ClearLinks()
    {
        _dead = _links.Count;
        _transcript?.ClearLinks();
        _changed();
    }

    public void ClearDiv()
    {
        Pane.ClearDiv();
        _transcript?.ClearDiv();
        _changed();
    }

    // [aam opcode] Clearing what the player has already had a chance
    // to read is for a frontend that cannot scroll back. This one can,
    // so nothing is thrown away.
    public void ClearOld() => _transcript?.ClearOld();

    public void LeaveAll()
    {
        Newline();
        Main.LeaveAll();
        Status.LeaveAll();
        _area = -1;
        _selfLink = false;
        _transcript?.LeaveAll();
    }

    public void Restart()
    {
        Main.Clear();
        Main.LeaveAll();
        Status.Clear();
        Status.LeaveAll();
        _links.Clear();
        _dead = 0;
        _area = -1;
        _selfLink = false;
        Background = AaTheme.Paper;
        _transcript?.Restart();
        _changed();
    }

    public void Sync()
    {
        Main.ScrollToEnd();
        _transcript?.Sync();
        _changed();
    }

    /// <summary>
    /// [aam opcode] How wide or how tall the current page is, in
    /// characters, which is what the reference interpreter answers by
    /// dividing the room it has by the size of a figure nought.
    /// </summary>
    public int Measure(int which) => which switch
    {
        0 => (int)(Column / Math.Max(_glyphs.CharacterWidth(_sheet.Plain), 1)),
        1 => (int)((_area == 0 ? StatusHeight : MainHeight)
            / Math.Max(_sheet.Plain.Size * AaText.LineSpacing, 1)),
        _ => 0,
    };

    public bool ScriptOn()
    {
        if (_transcript is not null)
        {
            return true;
        }

        if (_transcripts() is not { } writer)
        {
            return false;
        }

        _written = writer;
        _transcript = new TextOutput(writer, _story.Styles, AltText);
        return true;
    }

    public void ScriptOff()
    {
        _transcript?.Finish();
        _written?.Flush();
        _written?.Dispose();
        _transcript = null;
        _written = null;
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

    /// <summary>
    /// The pixels of a resource the story carries, or null where it
    /// carries none, carries one this cannot read, or points at a
    /// place on the web that nothing here can go and fetch.
    /// </summary>
    /// <remarks>
    /// A story may carry a file of any sort at all, and the corpus
    /// carries sheet music as PDFs beside its pictures. What cannot be
    /// drawn is answered for honestly, and the machine then prints the
    /// words the story carries in its place instead of asking for it.
    /// </remarks>
    private Pixels? Picture(int resource)
    {
        if (_pictures.TryGetValue(resource, out var known))
        {
            return known;
        }

        var pixels = Read(resource);

        _pictures[resource] = pixels;
        return pixels;
    }

    private Pixels? Read(int resource)
    {
        if (resource < 0 || resource >= _story.Resources.Count)
        {
            return null;
        }

        var bytes = _story.Contents(_story.Resources[resource]);

        if (bytes.IsEmpty)
        {
            return null;
        }

        try
        {
            return PngReader.Read(bytes);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private string AltText(int resource) =>
        resource >= 0 && resource < _story.Resources.Count
            ? _story.Text.At(_story.Resources[resource].AltText)
            : string.Empty;
}

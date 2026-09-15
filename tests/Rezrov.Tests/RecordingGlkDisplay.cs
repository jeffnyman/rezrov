using System.Text;
using Rezrov.Glulx.Glk;

namespace Rezrov.Tests;

/// <summary>
/// A Glk display for tests: a fixed size, a record of everything
/// printed to each text buffer window, cleared, and rearranged, and a
/// queue of input to hand over when the game waits.
/// </summary>
internal sealed class RecordingGlkDisplay : IGlkDisplay
{
    private readonly Dictionary<uint, StringBuilder> _texts = [];
    private readonly StringBuilder _all = new();

    public RecordingGlkDisplay(int width = 40, int height = 10)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>
    /// Everything printed to any text buffer window, in order.
    /// </summary>
    public string Output => _all.ToString();

    /// <summary>The styles seen with each character, in order.</summary>
    public List<GlkStyle> Styles { get; } = [];

    /// <summary>
    /// The link value seen with each character, in order.
    /// </summary>
    public List<uint> Links { get; } = [];

    /// <summary>
    /// Whether this display claims to have a pointer, which the mouse
    /// and hyperlink gestalt answers follow.
    /// </summary>
    public bool Pointer { get; init; }

    public List<GlkWindow> Cleared { get; } = [];

    public int ArrangedCount { get; private set; }

    public GlkWindow? LastRoot { get; private set; }

    /// <summary>
    /// The lines and keys to give the game, in order. The window of an
    /// entry is ignored: a line goes to the first window asking for a
    /// line and a key to the first asking for a key. When the queue is
    /// empty, input has ended.
    /// </summary>
    public Queue<GlkInput> Inputs { get; } = [];

    /// <summary>
    /// The initial text of each line request seen, for checking that a
    /// game's pre-entered text reaches the display.
    /// </summary>
    public List<string> InitialTexts { get; } = [];

    /// <summary>The timeouts passed to each wait.</summary>
    public List<TimeSpan?> Timeouts { get; } = [];

    /// <summary>How many times the library asked for a wake.</summary>
    public int Wakes { get; private set; }

    /// <summary>What was printed to one window.</summary>
    public string Text(GlkWindow window) => _texts.TryGetValue(window.Id, out var text) ? text.ToString() : "";

    public void Print(GlkWindow window, uint character, GlkStyle style, uint link)
    {
        Append(window, GlkText.ToString(character));
        Styles.Add(style);
        Links.Add(link);
    }

    public bool CanReportMouse(WindowType type) => Pointer && type == WindowType.TextGrid;

    public bool CanReportHyperlinks(WindowType type) => Pointer && type is WindowType.TextBuffer or WindowType.TextGrid;

    public void Clear(GlkWindow window) => Cleared.Add(window);

    public void Arranged(GlkWindow? root)
    {
        ArrangedCount++;
        LastRoot = root;
    }

    public GlkInput WaitForInput(IReadOnlyList<GlkWindow> lineRequests, IReadOnlyList<GlkWindow> charRequests, TimeSpan? timeout)
    {
        Timeouts.Add(timeout);
        foreach (var window in lineRequests)
        {
            InitialTexts.Add(window.LineRequest!.Initial);
        }

        if (Inputs.Count == 0)
        {
            return GlkInput.Ended;
        }

        var input = Inputs.Dequeue();
        switch (input.Kind)
        {
            case GlkInputKind.Line when lineRequests.Count > 0:
            {
                // [glk #line_events] The display shows the typed line in
                // a text buffer window, unless echoing is off.
                var window = lineRequests[0];
                if (window.Type == WindowType.TextBuffer && window.EchoLineInput)
                {
                    Append(window, input.Text + "\n");
                }

                return GlkInput.Line(window, input.Text ?? "", input.Terminator);
            }

            case GlkInputKind.Key when charRequests.Count > 0:
                return GlkInput.KeyPress(charRequests[0], input.Key);

            case GlkInputKind.Mouse when input.Window is null:
                // [glk #mouse_events] A click with no window named goes
                // to whichever window is waiting for one, as a display
                // works out for itself where the pointer landed.
                return Listening(window => window.MouseRequest) is { } clicked
                    ? GlkInput.MouseClick(clicked, input.Column, input.Row)
                    : input;

            case GlkInputKind.Hyperlink when input.Window is null:
                return Listening(window => window.HyperlinkRequest) is { } selected
                    ? GlkInput.LinkSelected(selected, input.Link)
                    : input;

            default:
                return input;
        }
    }

    public void Wake() => Wakes++;

    /// <summary>
    /// The first window of the tree last laid out that matches, or null.
    /// </summary>
    private GlkWindow? Listening(Func<GlkWindow, bool> wanted) => Leaves(LastRoot).FirstOrDefault(wanted);

    private static IEnumerable<GlkWindow> Leaves(GlkWindow? window) => window switch
    {
        null => [],
        PairWindow pair => Leaves(pair.First).Concat(Leaves(pair.Second)),
        _ => [window],
    };

    private void Append(GlkWindow window, string text)
    {
        if (!_texts.TryGetValue(window.Id, out var builder))
        {
            builder = new StringBuilder();
            _texts[window.Id] = builder;
        }

        builder.Append(text);
        _all.Append(text);
    }
}

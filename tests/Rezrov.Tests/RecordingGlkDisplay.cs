using System.Text;
using Rezrov.Glulx.Glk;

namespace Rezrov.Tests;

/// <summary>
/// A Glk display for tests: a fixed size, and a record of everything
/// printed to each text buffer window, cleared, and rearranged.
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

    public List<GlkWindow> Cleared { get; } = [];

    public int ArrangedCount { get; private set; }

    public GlkWindow? LastRoot { get; private set; }

    /// <summary>What was printed to one window.</summary>
    public string Text(GlkWindow window) => _texts.TryGetValue(window.Id, out var text) ? text.ToString() : "";

    public void Print(GlkWindow window, uint character, GlkStyle style)
    {
        var text = GlkText.ToString(character);
        if (!_texts.TryGetValue(window.Id, out var builder))
        {
            builder = new StringBuilder();
            _texts[window.Id] = builder;
        }

        builder.Append(text);
        _all.Append(text);
        Styles.Add(style);
    }

    public void Clear(GlkWindow window) => Cleared.Add(window);

    public void Arranged(GlkWindow? root)
    {
        ArrangedCount++;
        LastRoot = root;
    }
}

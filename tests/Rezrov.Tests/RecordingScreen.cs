using System.Text;
using Rezrov.ZMachine.Screen;

namespace Rezrov.Tests;

/// <summary>
/// A screen for tests: a fixed grid of any size that records what the
/// model sends to the lower window, run by run, and counts the rest.
/// </summary>
internal sealed class RecordingScreen : IScreen
{
    private readonly StringBuilder _text = new();

    public RecordingScreen(int width = 80, int height = 24, ScreenCapabilities? capabilities = null)
    {
        Width = width;
        Height = height;
        Capabilities = capabilities ?? (ScreenCapabilities.StatusLine | ScreenCapabilities.UpperWindow
            | ScreenCapabilities.Colors | ScreenCapabilities.Bold | ScreenCapabilities.Italic
            | ScreenCapabilities.FixedPitch | ScreenCapabilities.FixedGrid);
    }

    public int Width { get; }

    public int Height { get; }

    public ScreenCapabilities Capabilities { get; }

    public ScreenColor DefaultForeground { get; init; } = ScreenColor.White;

    public ScreenColor DefaultBackground { get; init; } = ScreenColor.Blue;

    /// <summary>Characters this screen refuses to show.</summary>
    public HashSet<char> Unprintable { get; } = [];

    /// <summary>Every run sent to the lower window, in order.</summary>
    public List<(string Text, TextAttributes Attributes)> Runs { get; } = [];

    /// <summary>
    /// The lower window's text with newlines, as a whole.
    /// </summary>
    public string Text => _text.ToString();

    /// <summary>The lower window's text split into lines.</summary>
    public string[] Lines => Text.Split('\n');

    public int MorePrompts { get; private set; }

    public int LowerErasures { get; private set; }

    public int LineErasures { get; private set; }

    public int UpperWindowUpdates { get; private set; }

    /// <summary>
    /// The model as it was at the last upper window update.
    /// </summary>
    public ScreenModel? LastModel { get; private set; }

    public bool CanPrint(char character) => !Unprintable.Contains(character);

    public void Print(string text, TextAttributes attributes)
    {
        Runs.Add((text, attributes));
        _text.Append(text);
    }

    public void NewLine() => _text.Append('\n');

    public void EraseLowerWindow(ScreenColor background)
    {
        LowerErasures++;
        _text.Clear();
    }

    public void EraseToEndOfLine(ScreenColor background) => LineErasures++;

    public void MorePrompt() => MorePrompts++;

    public void UpdateUpperWindow(ScreenModel model)
    {
        UpperWindowUpdates++;
        LastModel = model;
    }
}

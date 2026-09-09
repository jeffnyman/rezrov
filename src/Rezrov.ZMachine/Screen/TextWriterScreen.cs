namespace Rezrov.ZMachine.Screen;

/// <summary>
/// A screen that is a stream of text: the lower window goes to a
/// <see cref="TextWriter"/> and the upper window is not shown at all.
/// </summary>
/// <remarks>
/// Enough for a transcript, a test, or a console without cursor
/// control. It declares no upper window and no status line, so a game
/// that reads the header knows it is talking to a teletype, and it does
/// not ask the model to wrap or page, since whatever shows the stream
/// does that. The width and height are still reported, because
/// [zm 8.4] the header must hold some dimensions and games lay text out
/// by them.
/// </remarks>
public sealed class TextWriterScreen : IScreen
{
    private readonly TextWriter _writer;

    public TextWriterScreen(TextWriter writer, int width = 80, int height = 24)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        _writer = writer;
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public ScreenCapabilities Capabilities => ScreenCapabilities.None;

    public ScreenColor DefaultForeground => ScreenColor.White;

    public ScreenColor DefaultBackground => ScreenColor.Black;

    /// <summary>
    /// Text can hold any character that is not a control code, which
    /// [zm 3.8.5.4.5] must not be used anyway.
    /// </summary>
    public bool CanPrint(char character) => !char.IsControl(character);

    public void Print(string text, TextAttributes attributes) => _writer.Write(text);

    public void NewLine() => _writer.Write((char)0x0A);

    public void EraseLowerWindow(ScreenColor background)
    {
    }

    public void EraseToEndOfLine(ScreenColor background)
    {
    }

    public void MorePrompt()
    {
    }

    public void UpdateUpperWindow(ScreenModel model)
    {
    }
}

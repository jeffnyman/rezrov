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
/// by them. [zm 16] The character graphics font is offered, as the
/// nearest Unicode characters, since a text stream can carry those.
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

    /// <summary>
    /// [zm 8.8.1] For a Version 6 game, a cell is 4 units wide and 1
    /// high, for the reasons the terminal frontend gives: Infocom's
    /// games measure sideways in pixels and downward in lines when they
    /// have no pictures.
    /// </summary>
    public int FontWidth => 4;

    public int FontHeight => 1;

    public ScreenCapabilities Capabilities => ScreenCapabilities.CharacterGraphicsFont;

    public ScreenColor DefaultForeground => ScreenColor.White;

    public ScreenColor DefaultBackground => ScreenColor.Black;

    /// <summary>
    /// Text can hold any character that is not a control code, which
    /// [zm 3.8.5.4.5] must not be used anyway.
    /// </summary>
    public bool CanPrint(char character) => !char.IsControl(character);

    public void Print(string text, TextAttributes attributes)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (attributes.Font != TextAttributes.CharacterGraphicsFont)
        {
            _writer.Write(text);
            return;
        }

        foreach (var character in text)
        {
            _writer.Write(CharacterGraphics.ToUnicode(character));
        }
    }

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

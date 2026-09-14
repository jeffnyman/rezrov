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
/// by them; the height is 255 unless told otherwise, which [zm 8.4.1]
/// means infinite. The standard advises against that number, for the
/// sake of games that send the cursor to the bottom of the screen, but
/// [zm 8.4.1 deviates] a stream has no bottom to send it to, and a game
/// that sizes its work by the height then rolls its dice as it does on
/// the other interpreters whose plain frontends say 255, so a script
/// made on one of those plays here unchanged. [zm 16] The character
/// graphics font is offered, as the
/// nearest Unicode characters, since a text stream can carry those.
/// </remarks>
public sealed class TextWriterScreen : IScreen
{
    private readonly TextWriter _writer;
    private readonly bool _showUpperWindow;

    // The upper window as last shown and as last seen, one string per
    // row, for telling a repaint from a keystroke's echo.
    private string[] _shown = [];
    private string[] _seen = [];
    private bool _atLineStart = true;

    /// <param name="showUpperWindow">
    /// Whether to write the upper window's rows into the stream whenever
    /// the game pauses for input and has changed them, for a game that
    /// draws its text there, such as Custard. A change of a single cell
    /// on the row the last one was on is the echo of a key being typed
    /// and is not written, nor is a window with nothing on it.
    /// </param>
    public TextWriterScreen(TextWriter writer, int width = 80, int height = 255, bool showUpperWindow = false)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        _writer = writer;
        _showUpperWindow = showUpperWindow;
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

        if (text.Length > 0)
        {
            _atLineStart = false;
        }

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

    public void NewLine()
    {
        _writer.Write((char)0x0A);
        _atLineStart = true;
    }

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
        ArgumentNullException.ThrowIfNull(model);

        if (!_showUpperWindow)
        {
            return;
        }

        var rows = new string[model.UpperWindow.Lines];
        for (var row = 0; row < rows.Length; row++)
        {
            rows[row] = model.UpperWindow.RowText(row + 1);
        }

        var sinceSeen = Difference(_seen, rows);
        var sinceShown = Difference(_shown, rows);
        _seen = rows;

        if (sinceShown.Cells == 0)
        {
            return;
        }

        // A key typed into the window changes one cell, and the keys of
        // a command stay on one row, so that is not a repaint yet.
        if (sinceSeen.Cells <= 1 && sinceShown.Rows <= 1)
        {
            return;
        }

        _shown = rows;
        var last = rows.Length;
        while (last > 0 && string.IsNullOrWhiteSpace(rows[last - 1]))
        {
            last--;
        }

        if (last == 0)
        {
            return;
        }

        if (!_atLineStart)
        {
            NewLine();
        }

        for (var row = 0; row < last; row++)
        {
            _writer.Write(rows[row].TrimEnd());
            _writer.Write((char)0x0A);
        }

        _writer.Write((char)0x0A);
        _atLineStart = true;
    }

    // How many cells differ between two sets of rows, and how many rows
    // hold a difference. Rows one has and the other does not count as
    // wholly different.
    private static (int Cells, int Rows) Difference(string[] before, string[] after)
    {
        var cells = 0;
        var rows = 0;
        for (var row = 0; row < Math.Max(before.Length, after.Length); row++)
        {
            var changed = 0;
            if (row >= before.Length || row >= after.Length)
            {
                changed = Math.Max(before.Length > row ? before[row].Length : 0, after.Length > row ? after[row].Length : 0);
            }
            else
            {
                for (var column = 0; column < before[row].Length; column++)
                {
                    if (before[row][column] != after[row][column])
                    {
                        changed++;
                    }
                }
            }

            if (changed > 0)
            {
                cells += changed;
                rows++;
            }
        }

        return (cells, rows);
    }
}

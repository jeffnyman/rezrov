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
/// graphics font is offered, as the nearest Unicode characters, since
/// a text stream can carry those; and so is the fixed-pitch font,
/// which a stream of text is in anyway. A game that has switched to
/// the graphics font for a border needs the fixed-pitch font said yes
/// to before it can switch back, since [zm op:set_font] a refused font
/// leaves the current one in force: Journey prints its command menu
/// that way, and would print it in runes otherwise.
/// </remarks>
public sealed class TextWriterScreen : IScreen
{
    private readonly TextWriter _writer;
    private readonly bool _showUpperWindow;
    private readonly bool _hasPictures;

    // The pictures on the screen now, and as last written out, so that
    // a play records the ones that changed and not the ones that are
    // simply still there.
    private IReadOnlyList<PicturePlacement> _pictures = [];
    private string _drawn = string.Empty;

    // The upper window as last shown and as last seen, one string per
    // row, for telling a repaint from a keystroke's echo.
    private string[] _shown = [];
    private string[] _seen = [];
    private readonly System.Text.StringBuilder _line = new();

    /// <param name="hasPictures">
    /// [zm 8.8.6] Whether the game is told the screen can show
    /// pictures. A stream of text cannot show one, so what is recorded
    /// is which pictures are on the screen and where, which is the
    /// thing a Version 6 game gets wrong when it gets anything wrong.
    /// Off, the game is told there are none and Infocom's Version 6
    /// games take their text-only paths.
    /// </param>
    /// <param name="showUpperWindow">
    /// Whether to write the upper window's rows into the stream whenever
    /// the game pauses for input and has changed them, for a game that
    /// draws its text there, such as Custard. A change of a single cell
    /// on the row the last one was on is the echo of a key being typed
    /// and is not written, nor is a window with nothing on it.
    /// </param>
    public TextWriterScreen(
        TextWriter writer,
        int width = 80,
        int height = 255,
        bool showUpperWindow = false,
        bool hasPictures = false)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        _writer = writer;
        _showUpperWindow = showUpperWindow;
        _hasPictures = hasPictures;
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
    /// <remarks>
    /// A screen that says it has pictures measures in real pixels
    /// instead, since that is what the game lays its artwork out in. A
    /// cell of 8 by 16 over a screen of 80 by 25 comes to 640 by 400,
    /// which is [blorb 11.2] exactly twice the 320 by 200 all four of
    /// Infocom's Version 6 games say their pictures were drawn for, so
    /// every size in a recording is exactly double the artwork's own
    /// and can be read at a glance.
    /// </remarks>
    public int FontWidth => _hasPictures ? 8 : 4;

    public int FontHeight => _hasPictures ? 16 : 1;

    public ScreenCapabilities Capabilities =>
        ScreenCapabilities.CharacterGraphicsFont | ScreenCapabilities.FixedPitch
        | (_hasPictures ? ScreenCapabilities.Pictures : 0);

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
            Write(text);
            return;
        }

        foreach (var character in text)
        {
            Write(CharacterGraphics.ToUnicode(character).ToString());
        }
    }

    public void NewLine()
    {
        _writer.Write((char)0x0A);
        _line.Clear();
    }

    // Writes text and remembers it as part of the line in progress, so
    // that an upper window written out in the middle of a line, which
    // is where a prompt leaves the stream, can be followed by that line
    // begun again and the command echoed after it stays on it.
    private void Write(string text)
    {
        _writer.Write(text);
        _line.Append(text);
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

    /// <summary>
    /// [zm 8.8.6] Remembers which pictures the game has on the screen.
    /// </summary>
    /// <remarks>
    /// A Version 6 game redraws many times within a turn, so nothing is
    /// written here; <see cref="ShowPictures"/> writes the picture line
    /// when whatever is driving the game says it has paused, which is
    /// the same rule the upper window follows.
    /// </remarks>
    public void UpdateWindows(WindowedScreenModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        _pictures = model.Pictures;
    }

    /// <summary>
    /// Writes which pictures are on the screen, if that has changed
    /// since it was last written. Nothing is written for a game that
    /// draws none, so a play without pictures reads as it always did.
    /// </summary>
    public void ShowPictures()
    {
        if (!_hasPictures)
        {
            return;
        }

        var now = _pictures.Count == 0
            ? "[no pictures]"
            : "[pictures " + string.Join(
                "; ",
                _pictures
                    .OrderBy(picture => picture.Number)
                    .ThenBy(picture => picture.Row)
                    .ThenBy(picture => picture.Column)
                    .Select(picture =>
                        $"{picture.Number} at {picture.Row},{picture.Column} {picture.Rows}x{picture.Columns}"))
            + "]";

        // Nothing at all is written until the first picture appears, so
        // that a game which never draws one records nothing.
        if (now == _drawn || (_drawn.Length == 0 && _pictures.Count == 0))
        {
            return;
        }

        _drawn = now;
        _writer.WriteLine();
        _writer.WriteLine(now);
    }

    public void UpdateUpperWindow(ScreenModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Only a pause, for input or to quit, is a moment to show the
        // window; a game redraws it several times in a turn (Beyond
        // Zork paints the title, then the map, then the text) and a
        // stream wants the finished picture once.
        if (!_showUpperWindow || !model.IsPausing)
        {
            return;
        }

        // [zm 16] Cells in the character graphics font are written as
        // the same Unicode characters the lower window gets, so a map
        // drawn in it (Beyond Zork's) reads as a map.
        var rows = new string[model.UpperWindow.Lines];
        var builder = new System.Text.StringBuilder(model.UpperWindow.Width);
        for (var row = 0; row < rows.Length; row++)
        {
            builder.Clear();
            for (var column = 1; column <= model.UpperWindow.Width; column++)
            {
                var cell = model.UpperWindow[row + 1, column];
                builder.Append(cell.Attributes.Font == TextAttributes.CharacterGraphicsFont ? CharacterGraphics.ToUnicode(cell.Character) : cell.Character);
            }

            rows[row] = builder.ToString();
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

        // A line in progress, a prompt as a rule, is ended, the window
        // written, and the line begun again, so that what the player
        // types next still follows the prompt.
        var interrupted = _line.ToString();
        if (interrupted.Length > 0)
        {
            _writer.Write((char)0x0A);
        }

        for (var row = 0; row < last; row++)
        {
            _writer.Write(rows[row].TrimEnd());
            _writer.Write((char)0x0A);
        }

        _writer.Write((char)0x0A);
        _writer.Write(interrupted);
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

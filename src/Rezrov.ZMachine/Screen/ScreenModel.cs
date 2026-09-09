using System.Globalization;
using System.Text;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Text;

namespace Rezrov.ZMachine.Screen;

/// <summary>
/// The screen model of section 8 for every version but 6: two windows,
/// their cursors, the text style, colors, and font in force, buffered
/// printing, and the [MORE] pause.
/// </summary>
/// <remarks>
/// The model sits between the interpreter and a frontend's
/// <see cref="IScreen"/>. The interpreter calls it for every screen
/// opcode and sends it every character printed to output stream 1. It
/// keeps the upper window's cells itself, since [zm 8.7.2.1] that window
/// overlays the screen and never scrolls, and passes the lower window
/// through as styled runs of text, since that one only ever scrolls.
///
/// [zm 8.5] Versions 1 and 2 have just the lower window, and [zm 8.6]
/// Version 3 adds an upper window below the status line. [zm 8.7] From
/// Version 4 the two windows are as here, with styles. Version 6 is a
/// different model, [zm 8.8], and is not built yet.
/// </remarks>
public sealed class ScreenModel : IOutput
{
    /// <summary>[zm 8.7.2] The lower window's number.</summary>
    public const int Lower = 0;

    /// <summary>[zm 8.7.2] The upper window's number.</summary>
    public const int Upper = 1;

    private readonly IScreen _screen;
    private readonly StoryHeader _header;
    private readonly ZMachineVersion _version;
    private readonly UnicodeTranslationTable _extraCharacters;
    private readonly List<(char Character, TextAttributes Attributes)> _word = [];
    private int _lowerColumn;
    private int _linesSincePause;
    private bool _pagingSuppressed;
    private bool _upperChanged;

    public ScreenModel(IScreen screen, StoryHeader header, ZMemory memory)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(header);

        _screen = screen;
        _header = header;
        _version = header.Version;
        _extraCharacters = UnicodeTranslationTable.ForStory(header, memory);
        UpperWindow = new UpperWindow(screen.Width, Math.Max(screen.Height, 1), DefaultAttributes);

        Reset();
    }

    /// <summary>The frontend the model draws on.</summary>
    public IScreen Screen => _screen;

    /// <summary>[zm 8.4] Width in characters.</summary>
    public int Width => _screen.Width;

    /// <summary>[zm 8.4] Height in lines.</summary>
    public int Height => _screen.Height;

    /// <summary>The upper window's cells and cursor.</summary>
    public UpperWindow UpperWindow { get; }

    /// <summary>
    /// [zm 8.2] The status line as last shown, one cell per column, or
    /// null if it has not been shown yet or the version has none.
    /// </summary>
    public IReadOnlyList<Cell>? StatusLine { get; private set; }

    /// <summary>
    /// [zm 8.7.2] The window text goes to: <see cref="Lower"/> or
    /// <see cref="Upper"/>.
    /// </summary>
    public int CurrentWindow { get; private set; }

    /// <summary>[zm 8.7.1] The styles the game has asked for.</summary>
    public TextStyle Style { get; private set; }

    /// <summary>[zm 8.3] The current foreground, an actual color.</summary>
    public ScreenColor Foreground { get; private set; }

    /// <summary>[zm 8.3] The current background, an actual color.</summary>
    public ScreenColor Background { get; private set; }

    /// <summary>[zm 8.1.2] The current font number.</summary>
    public int Font { get; private set; }

    /// <summary>
    /// [zm 7.2] Whether the lower window buffers text into words so
    /// that none is split across lines.
    /// </summary>
    public bool IsBuffering { get; private set; }

    /// <summary>
    /// How many characters are on the lower window's current line,
    /// counting any word still in the buffer.
    /// </summary>
    public int LowerColumn => _lowerColumn + _word.Count;

    /// <summary>
    /// [zm 8.6.1.1] In Versions 1 to 3 the top screen line holds the
    /// status line and the upper window starts below it.
    /// </summary>
    public int StatusLineRows => _version <= ZMachineVersion.V3 ? 1 : 0;

    /// <summary>
    /// The most lines the upper window can have: the screen less the
    /// status line.
    /// </summary>
    public int MaxUpperLines => Math.Max(Height - StatusLineRows, 0);

    /// <summary>
    /// The attributes text is printed in right now: the game's style,
    /// forced to fixed pitch where [zm 8.1] the header's bit or font 4
    /// says so and where [zm 8.7.2.4] the upper window is the target.
    /// </summary>
    public TextAttributes Attributes
    {
        get
        {
            var style = Style;
            if (CurrentWindow == Upper || Font == TextAttributes.FixedPitchFont
                || _header.Flags2.HasFlag(Flags2.ForceFixedPitch))
            {
                style |= TextStyle.FixedPitch;
            }

            return new TextAttributes(style, Foreground, Background, Font);
        }
    }

    private TextAttributes DefaultAttributes =>
        new(TextStyle.Roman, _screen.DefaultForeground, _screen.DefaultBackground, TextAttributes.NormalFont);

    // [zm 8.7.3.2] Erased space is in the current colors but never
    // reverse video, which Cell.Blank takes care of.
    private TextAttributes BlankAttributes => new(TextStyle.Roman, Foreground, Background, Font);

    private bool IsGrid => _screen.Capabilities.HasFlag(ScreenCapabilities.FixedGrid);

    /// <summary>
    /// Puts the screen as it is at the start of a game and after a
    /// restart.
    /// </summary>
    /// <remarks>
    /// [zm 8.7.3.3] The same operation as erasing window -1: the whole
    /// screen cleared, the upper window collapsed, the lower window
    /// selected. [zm 8.5.2] and [zm 8.6.3] say the same for the early
    /// versions. Style, colors, and font go back to their defaults, and
    /// [zm 7.2.1] buffering is on.
    /// </remarks>
    public void Reset()
    {
        _word.Clear();
        Style = TextStyle.Roman;
        Foreground = _screen.DefaultForeground;
        Background = _screen.DefaultBackground;
        Font = TextAttributes.NormalFont;
        IsBuffering = true;
        StatusLine = null;
        _pagingSuppressed = false;
        EraseWindow(-1);
    }

    /// <summary>
    /// [zm op:print_char] and every other printing opcode arrive here,
    /// one ZSCII code at a time, as output stream 1.
    /// </summary>
    public void Print(ushort zscii)
    {
        if (zscii == Zscii.Newline)
        {
            NewLine();
        }
        else if (Zscii.ToUnicode(zscii, _version, _extraCharacters) is { } character)
        {
            Print(character);
        }
    }

    /// <summary>
    /// Prints one character to the current window.
    /// </summary>
    /// <remarks>
    /// [zm 7.2] In the lower window with buffering on, the character
    /// joins the word being built, and the word is placed when a space
    /// or a newline ends it. [zm 8.7.2.5] The upper window never
    /// buffers, and [zm 8.6.1.1.1] printing there overlays the cell at
    /// the cursor.
    /// </remarks>
    public void Print(char character)
    {
        if (CurrentWindow == Upper)
        {
            PrintUpper(character);
        }
        else if (!IsBuffering)
        {
            EmitCharacters([(character, Attributes)]);
        }
        else if (character == ' ')
        {
            FlushWord();
            EmitSpace();
        }
        else
        {
            _word.Add((character, Attributes));
        }
    }

    /// <summary>
    /// [zm op:print_unicode] Prints a Unicode character, or a question
    /// mark where [zm 3.8.5.4.3] the screen has no form for it.
    /// </summary>
    public void PrintUnicode(char character) =>
        Print(_screen.CanPrint(character) && !char.IsControl(character) ? character : '?');

    /// <summary>
    /// [zm op:check_unicode] Whether the screen can show a character.
    /// </summary>
    public bool CanPrint(char character) => _screen.CanPrint(character) && !char.IsControl(character);

    /// <summary>
    /// [zm op:new_line] Ends the line in the current window.
    /// </summary>
    public void NewLine()
    {
        if (CurrentWindow == Upper)
        {
            // [zm 8.7.3.1] The upper window never scrolls, so on its
            // last line a newline only returns to the left.
            UpperWindow.CursorColumn = 1;
            if (UpperWindow.CursorRow < Math.Max(UpperWindow.Lines, 1))
            {
                UpperWindow.CursorRow++;
            }

            _upperChanged = true;
            return;
        }

        FlushWord();
        NewLineLower();
    }

    /// <summary>
    /// [zm op:set_window] Selects the window text goes to.
    /// </summary>
    /// <remarks>
    /// [zm 8.7.2] Whenever the upper window is selected its cursor goes
    /// to the top left. Coming back to the lower window is a point at
    /// which the upper window's contents are settled, so the frontend
    /// is told to repaint.
    /// </remarks>
    public void SetWindow(int window)
    {
        if (window is not (Lower or Upper))
        {
            throw new ArgumentOutOfRangeException(nameof(window), window, "Only windows 0 and 1 exist before Version 6.");
        }

        FlushWord();
        CurrentWindow = window;

        if (window == Upper)
        {
            UpperWindow.CursorRow = 1;
            UpperWindow.CursorColumn = 1;
        }
        else
        {
            Sync();
        }
    }

    /// <summary>
    /// [zm op:split_window] Gives the upper window a height in lines,
    /// or 0 to unsplit.
    /// </summary>
    /// <remarks>
    /// [zm 8.7.2.1.1] The upper window's cursor stays where it is if
    /// that is still inside the window and otherwise goes to the top
    /// left. [zm 8.6.1.1.2] In Version 3 the upper window is cleared
    /// by a split; [zm 8.6.1] in every other version resizing changes
    /// nothing on screen. [zm 8.7.2.2] The lower window's cursor is
    /// never swallowed, which a scrolling stream needs no help with.
    /// </remarks>
    public void SplitWindow(int lines)
    {
        FlushWord();

        lines = Math.Clamp(lines, 0, MaxUpperLines);
        UpperWindow.Lines = lines;

        if (UpperWindow.CursorRow > lines)
        {
            UpperWindow.CursorRow = 1;
            UpperWindow.CursorColumn = 1;
        }

        if (_version == ZMachineVersion.V3 && lines > 0)
        {
            UpperWindow.Clear(BlankAttributes);
        }

        _upperChanged = true;
        Sync();
    }

    /// <summary>
    /// [zm op:erase_window] Clears a window, or with -1 the whole screen
    /// unsplit, or with -2 the whole screen as it is split.
    /// </summary>
    /// <remarks>
    /// [zm 8.7.3.3] Erasing -1 clears everything to the lower window's
    /// background, collapses the upper window, and selects the lower
    /// window, whose cursor then starts afresh. [zm 8.7.3.2.1] Erasing
    /// a window puts its cursor at the top left, or at the bottom left
    /// for the lower window in Version 4, which for a stream is the
    /// same fresh line either way.
    /// </remarks>
    /// <returns>False if the number names no window.</returns>
    public bool EraseWindow(int window)
    {
        FlushWord();

        switch (window)
        {
            case -1:
                UpperWindow.Lines = 0;
                UpperWindow.Clear(BlankAttributes);
                UpperWindow.CursorRow = 1;
                UpperWindow.CursorColumn = 1;
                CurrentWindow = Lower;
                EraseLower();
                break;
            case -2:
                UpperWindow.Clear(BlankAttributes);
                EraseLower();
                break;
            case Lower:
                EraseLower();
                break;
            case Upper:
                UpperWindow.Clear(BlankAttributes);
                UpperWindow.CursorRow = 1;
                UpperWindow.CursorColumn = 1;
                break;
            default:
                return false;
        }

        _upperChanged = true;
        Sync();
        return true;
    }

    /// <summary>
    /// [zm op:erase_line] Clears from the cursor to the end of the line
    /// in the current window.
    /// </summary>
    public void EraseLine()
    {
        FlushWord();

        if (CurrentWindow == Upper)
        {
            // [zm 8.7.3.4]
            if (UpperWindow.CursorColumn <= Width)
            {
                UpperWindow.ClearRow(UpperWindow.CursorRow, UpperWindow.CursorColumn, BlankAttributes);
            }

            _upperChanged = true;
        }
        else
        {
            _screen.EraseToEndOfLine(Background);
        }
    }

    /// <summary>
    /// [zm op:set_cursor] Moves the upper window's cursor.
    /// </summary>
    /// <remarks>
    /// [zm 8.7.2.3.1] The opcode has no effect when the lower window is
    /// selected, and it is illegal to move the cursor outside the upper
    /// window. The remarks on section 8 recommend that a row below the
    /// split be met with an implicit split to contain it, since Inform's
    /// menu libraries and Infocom's Sherlock both do this, with a
    /// diagnostic the player can turn off. That is what happens here,
    /// and the return value carries the diagnostic. A column off the
    /// edge goes to column 1, as Frotz has it.
    /// </remarks>
    /// <returns>False if the position was outside the window.</returns>
    public bool SetCursor(int row, int column)
    {
        FlushWord();

        if (CurrentWindow != Upper)
        {
            return true;
        }

        var legal = true;

        if (column < 1 || column > Width)
        {
            column = 1;
            legal = false;
        }

        if (row < 1)
        {
            row = 1;
            legal = false;
        }

        if (row > UpperWindow.Lines)
        {
            legal = false;
            row = Math.Min(row, MaxUpperLines);
            if (row > UpperWindow.Lines)
            {
                SplitWindow(row);
            }
        }

        UpperWindow.CursorRow = Math.Max(row, 1);
        UpperWindow.CursorColumn = column;
        return legal;
    }

    /// <summary>
    /// [zm op:get_cursor] The upper window's cursor as (row, column).
    /// </summary>
    /// <remarks>
    /// [zm 8.7.2.3.2] Whichever window is selected, this is the upper
    /// window's cursor. [zm 8.8.3.2.7] Buffered text is flushed first so
    /// that the position is accurate.
    /// </remarks>
    public (int Row, int Column) GetCursor()
    {
        FlushWord();
        return (UpperWindow.CursorRow, UpperWindow.CursorColumn);
    }

    /// <summary>
    /// [zm op:set_text_style] Sets the style, or adds to it.
    /// </summary>
    /// <remarks>
    /// [zm op:set_text_style] As of Standard 1.1 a non-zero operand
    /// activates every style it names on top of those already set, and
    /// zero turns them all off. Bits above the four styles mean nothing
    /// and are dropped.
    /// </remarks>
    public void SetTextStyle(int style)
    {
        Style = style == 0 ? TextStyle.Roman : Style | (TextStyle)(style & 0x0F);
    }

    /// <summary>
    /// [zm op:buffer_mode] Turns word buffering in the lower window on
    /// or off. Whatever is buffered comes out first.
    /// </summary>
    public void SetBuffering(bool on)
    {
        FlushWord();
        IsBuffering = on;
    }

    /// <summary>
    /// [zm op:set_colour] Sets the colors, with 0 meaning keep the
    /// current one and 1 the default.
    /// </summary>
    /// <remarks>
    /// [zm op:set_colour] Buffered text is flushed in the old colors
    /// first. Colors are recorded whether or not the frontend can show
    /// them, since [zm 8.3.4] an interpreter should not rule colors out
    /// on the game's say-so, and a frontend that cannot show them just
    /// ignores the attributes.
    /// </remarks>
    /// <returns>
    /// False if either number is not a color in this version.
    /// </returns>
    public bool SetColors(ScreenColor foreground, ScreenColor background)
    {
        FlushWord();

        if (!ScreenColors.IsLegal(foreground, _version) || !ScreenColors.IsLegal(background, _version))
        {
            return false;
        }

        Foreground = Resolve(foreground, Foreground, _screen.DefaultForeground);
        Background = Resolve(background, Background, _screen.DefaultBackground);
        return true;

        static ScreenColor Resolve(ScreenColor asked, ScreenColor current, ScreenColor fallback) => asked switch
        {
            ScreenColor.Current => current,
            ScreenColor.Default => fallback,
            _ => asked,
        };
    }

    /// <summary>
    /// [zm op:set_true_colour] Sets the colors from 15-bit values, as
    /// near as the standard colors allow.
    /// </summary>
    /// <returns>
    /// False if either value is a Version 6 special value.
    /// </returns>
    public bool SetTrueColors(short foreground, short background)
    {
        // [zm 8.3.7] -1 default, -2 current, and the rest Version 6 only.
        if (!TryTranslate(foreground, out var fg) || !TryTranslate(background, out var bg))
        {
            return false;
        }

        return SetColors(fg, bg);

        bool TryTranslate(short trueColor, out ScreenColor color)
        {
            switch (trueColor)
            {
                case ScreenColors.TrueDefault:
                    color = ScreenColor.Default;
                    return true;
                case ScreenColors.TrueCurrent:
                    color = ScreenColor.Current;
                    return true;
                case < 0:
                    color = ScreenColor.Current;
                    return false;
                default:
                    // [zm 8.3.7.1] The minimal implementation: the
                    // nearest standard color.
                    color = ScreenColors.Nearest((ushort)trueColor, _version);
                    return true;
            }
        }
    }

    /// <summary>
    /// [zm op:set_font] Selects a font if it is available.
    /// </summary>
    /// <returns>
    /// The previous font, or 0 if the one asked for is unavailable, or
    /// the current font when 0 is asked for.
    /// </returns>
    public int SetFont(int font)
    {
        if (font == 0)
        {
            return Font;
        }

        if (!IsFontAvailable(font))
        {
            return 0;
        }

        var previous = Font;
        Font = font;
        return previous;
    }

    /// <summary>
    /// [zm 8.1.2] Which fonts the frontend can show: the normal font
    /// always, [zm 8.1.4] the picture font never, [zm 8.1.5] the
    /// character graphics font and the fixed pitch font when declared.
    /// </summary>
    public bool IsFontAvailable(int font) => font switch
    {
        TextAttributes.NormalFont => true,
        TextAttributes.CharacterGraphicsFont => _screen.Capabilities.HasFlag(ScreenCapabilities.CharacterGraphicsFont),
        TextAttributes.FixedPitchFont => _screen.Capabilities.HasFlag(ScreenCapabilities.FixedPitch),
        _ => false,
    };

    /// <summary>
    /// [zm op:print_table] Moves to the start of the next row of a
    /// table: down one line in the upper window, back to the column the
    /// table began in, or onto a new line in the lower window.
    /// </summary>
    public void NextTableRow(int startColumn)
    {
        if (CurrentWindow == Upper)
        {
            FlushWord();
            UpperWindow.CursorRow = Math.Min(UpperWindow.CursorRow + 1, Math.Max(UpperWindow.Lines, 1));
            UpperWindow.CursorColumn = startColumn;
            _upperChanged = true;
        }
        else
        {
            NewLine();
        }
    }

    /// <summary>
    /// [zm 8.2] Shows the status line for Versions 1 to 3.
    /// </summary>
    /// <param name="name">
    /// [zm 8.2.2] The short name of the object in the first global.
    /// </param>
    /// <param name="timeGame">
    /// [zm 8.2.1] Whether the right side shows a time rather than a
    /// score and turn count.
    /// </param>
    /// <param name="first">The second global: score, or hours.</param>
    /// <param name="second">The third global: turns, or minutes.</param>
    /// <remarks>
    /// The layout follows the formats the standard's author prefers in
    /// the remarks on section 8: the score and turns as "80/733" and the
    /// time as "12:03 PM", each fitting in eight characters at the right.
    /// [zm 8.2.2.2] A name too long for the room left is broken at its
    /// last space and given an ellipsis. The line is shown in reverse
    /// video, as most interpreters show it.
    /// </remarks>
    public void ShowStatusLine(string name, bool timeGame, short first, short second)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_version > ZMachineVersion.V3)
        {
            return;
        }

        var right = timeGame ? FormatTime(first, second) : FormatScore(first, second);

        // One space at the left, two before the right-hand text, and
        // one after it.
        var room = Width - right.Length - 4;
        if (name.Length > room)
        {
            var cut = room > 3 ? name.LastIndexOf(' ', room - 3) : -1;
            name = (cut > 0 ? name[..cut] : name[..Math.Max(room - 3, 0)]) + "...";
        }

        var attributes = DefaultAttributes with { Style = TextStyle.ReverseVideo | TextStyle.FixedPitch };
        var cells = new Cell[Width];
        Array.Fill(cells, new Cell(' ', attributes));

        for (var i = 0; i < name.Length && i + 1 < Width; i++)
        {
            cells[i + 1] = new Cell(name[i], attributes);
        }

        var start = Width - right.Length - 1;
        for (var i = 0; i < right.Length && start + i >= 0 && start + i < Width; i++)
        {
            cells[start + i] = new Cell(right[i], attributes);
        }

        StatusLine = cells;
        _upperChanged = true;
        Sync();
    }

    /// <summary>
    /// The status line's characters, for display and tests.
    /// </summary>
    public string? StatusLineText =>
        StatusLine is null ? null : string.Concat(StatusLine.Select(c => c.Character));

    /// <summary>
    /// The interpreter is about to wait for input: buffered text comes
    /// out, the [MORE] count starts over, and the frontend gets to
    /// repaint the upper window.
    /// </summary>
    /// <param name="suppressPaging">
    /// [zm 10.2.4] True while commands come from a file, when the
    /// interpreter should not hold up long passages of text.
    /// </param>
    public void PrepareForInput(bool suppressPaging)
    {
        FlushWord();
        _linesSincePause = 0;
        _pagingSuppressed = suppressPaging;
        _upperChanged = true;
        Sync();
    }

    /// <summary>
    /// Input has been read. If the player's return key ended it, the
    /// echo moved the lower window's cursor to a fresh line.
    /// </summary>
    public void InputEnded(bool newLine)
    {
        if (newLine && CurrentWindow == Lower)
        {
            _lowerColumn = 0;
        }
    }

    /// <summary>
    /// Sends out whatever is buffered and lets the frontend repaint,
    /// as before the game quits.
    /// </summary>
    public void Flush()
    {
        FlushWord();
        Sync();
    }

    private static string FormatScore(short score, short turns) =>
        string.Create(CultureInfo.InvariantCulture, $"{score}/{turns}");

    // [zm 8.2.3.2] Twelve-hour clock with AM or PM, so that 4am and 4pm
    // can be told apart.
    private static string FormatTime(short hours, short minutes)
    {
        var meridian = hours >= 12 ? "PM" : "AM";
        var shown = hours % 12;
        if (shown == 0)
        {
            shown = 12;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{shown}:{minutes:D2} {meridian}");
    }

    private void PrintUpper(char character)
    {
        // [zm 8.7.3.1] A character may be printed at the bottom right,
        // after which the cursor stays put, so anything further is
        // dropped rather than wrapped.
        if (UpperWindow.CursorColumn > Width)
        {
            return;
        }

        UpperWindow.Put(UpperWindow.CursorRow, UpperWindow.CursorColumn, new Cell(character, Attributes));
        UpperWindow.CursorColumn++;
        _upperChanged = true;
    }

    // [zm 7.2] Places the buffered word: on a new line first if it would
    // otherwise spread across two, and a word wider than the screen just
    // breaks wherever it must.
    private void FlushWord()
    {
        if (_word.Count == 0)
        {
            return;
        }

        if (IsGrid && _lowerColumn > 0 && _lowerColumn + _word.Count > Width && _word.Count <= Width)
        {
            NewLineLower();
        }

        var word = _word.ToArray();
        _word.Clear();
        EmitCharacters(word);
    }

    // A space at the right edge of a grid is swallowed rather than
    // wrapped, so that the next word starts the new line.
    private void EmitSpace()
    {
        if (IsGrid && _lowerColumn >= Width)
        {
            return;
        }

        EmitCharacters([(' ', Attributes)]);
    }

    // Hands characters to the frontend as runs of equal attributes,
    // breaking the line when a grid runs out of columns.
    private void EmitCharacters(IEnumerable<(char Character, TextAttributes Attributes)> characters)
    {
        var run = new StringBuilder();
        TextAttributes runAttributes = default;

        foreach (var (character, attributes) in characters)
        {
            if (IsGrid && _lowerColumn >= Width)
            {
                EmitRun();
                NewLineLower();
            }

            if (run.Length > 0 && runAttributes != attributes)
            {
                EmitRun();
            }

            runAttributes = attributes;
            run.Append(character);
            _lowerColumn++;
        }

        EmitRun();

        void EmitRun()
        {
            if (run.Length > 0)
            {
                _screen.Print(run.ToString(), runAttributes);
                run.Clear();
            }
        }
    }

    // [zm 8.4.1] The frontend pauses once a screenful has gone by since
    // the player last had a chance to read.
    private void NewLineLower()
    {
        _screen.NewLine();
        _lowerColumn = 0;

        if (!IsGrid || _pagingSuppressed)
        {
            return;
        }

        var lowerHeight = Math.Max(Height - StatusLineRows - UpperWindow.Lines, 2);
        _linesSincePause++;
        if (_linesSincePause >= lowerHeight - 1)
        {
            _screen.MorePrompt();
            _linesSincePause = 0;
        }
    }

    private void EraseLower()
    {
        _screen.EraseLowerWindow(Background);
        _lowerColumn = 0;
        _linesSincePause = 0;
    }

    private void Sync()
    {
        if (_upperChanged)
        {
            _upperChanged = false;
            _screen.UpdateUpperWindow(this);
        }
    }
}

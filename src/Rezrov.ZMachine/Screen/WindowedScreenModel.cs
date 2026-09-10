using System.Text;
using Rezrov.Core.Blorb;
using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Text;

namespace Rezrov.ZMachine.Screen;

/// <summary>
/// Where a picture was drawn on the screen, in cells, so that a
/// frontend can mark the place.
/// </summary>
public sealed record PicturePlacement(int Number, int Row, int Column, int Rows, int Columns);

/// <summary>
/// The screen model of [zm 8.8], for Version 6: eight windows over one
/// screen, each with its own cursor, margins, style, colors, font, and
/// attributes, and pictures from the resource file.
/// </summary>
/// <remarks>
/// [zm 8.8.1] The display is an array of pixels, and everything is
/// measured in units. Here the screen is kept as a grid of character
/// cells, and a unit is whatever fraction of a cell the frontend's
/// font size makes it: a terminal reports a font 1 unit wide and 1
/// high, so its units are cells outright, which is how the curses
/// build of Frotz runs these games too. A frontend with real pixels
/// and a fixed-pitch font reports its font size and the arithmetic
/// still holds, one cell per character.
///
/// [zm 8.8.3] Windows are invisible and usually lie on top of each
/// other. Nothing printed belongs to a window afterward; the cells are
/// the screen, and a window is only where the next thing goes. That
/// is why one grid serves all eight.
///
/// Pictures have sizes, from the resource file, but no pixels here:
/// [zm 8.8.6] the game asks about them with picture_data and lays out
/// around them, and drawing one clears its space and notes where it
/// went. A frontend that cannot show pictures says so, and the game
/// is told through the header that there are none, which the Infocom
/// games answer by running in their text-only modes.
/// </remarks>
public sealed class WindowedScreenModel : IScreenModel
{
    /// <summary>[zm 8.8.3] How many windows there are.</summary>
    public const int WindowCount = 8;

    /// <summary>
    /// [zm 8.8.3] The window number that means the current window.
    /// </summary>
    public const int CurrentWindowCode = -3;

    private readonly IScreen _screen;
    private readonly StoryHeader _header;
    private readonly UnicodeTranslationTable _extraCharacters;
    private readonly ZWindow[] _windows = new ZWindow[WindowCount];
    private readonly List<(char Character, TextAttributes Attributes)> _word = [];
    private readonly List<PicturePlacement> _pictures = [];
    private readonly StringBuilder _streamRun = new();
    private TextAttributes _streamAttributes;
    private int _streamRow = -1;
    private int _streamColumn;
    private Cell[][] _cells;
    private BlorbPictures? _catalog;
    private bool _split;
    private bool _changed;
    private bool _pagingSuppressed;
    private bool _quiet;

    public WindowedScreenModel(IScreen screen, StoryHeader header, ZMemory memory)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(header);

        _screen = screen;
        _header = header;
        _extraCharacters = UnicodeTranslationTable.ForStory(header, memory);

        for (var number = 0; number < WindowCount; number++)
        {
            _windows[number] = new ZWindow(number);
        }

        _cells = NewCells(Width, Height);
        Reset();
    }

    public IScreen Screen => _screen;

    /// <summary>The screen's width in cells.</summary>
    public int Width => Math.Max(_screen.Width, 1);

    /// <summary>The screen's height in cells.</summary>
    public int Height => Math.Max(_screen.Height, 1);

    /// <summary>[zm 8.1.1] A character's width in units.</summary>
    public int FontWidth => Math.Max(_screen.FontWidth, 1);

    /// <summary>[zm 8.1.1] A character's height in units.</summary>
    public int FontHeight => Math.Max(_screen.FontHeight, 1);

    /// <summary>[zm 8.4.3] The screen's width in units.</summary>
    public int UnitsWide => Width * FontWidth;

    /// <summary>[zm 8.4.3] The screen's height in units.</summary>
    public int UnitsHigh => Height * FontHeight;

    /// <summary>The eight windows, by number.</summary>
    public IReadOnlyList<ZWindow> Windows => _windows;

    public int CurrentWindow { get; private set; }

    /// <summary>The window text goes to.</summary>
    public ZWindow Current => _windows[CurrentWindow];

    public bool EchoesToTranscript => Current.CopiesToTranscript;

    /// <summary>
    /// [zm op:set_cursor] Whether the cursor is shown; -1 turns it off
    /// and -2 on again.
    /// </summary>
    public bool CursorVisible { get; private set; } = true;

    /// <summary>
    /// [zm 8.8.3.2.2.1] Whether the Zork Zero order of events applies
    /// to newline interrupts: move to the new line first, then call
    /// the routine. The interpreter sets this for that one story.
    /// </summary>
    public bool ZorkZeroInterruptOrder { get; set; }

    /// <summary>
    /// [zm 8.8.3.2.2] Called with the packed address of a window's
    /// newline interrupt routine when its countdown reaches zero. The
    /// interpreter runs the routine.
    /// </summary>
    public Action<ushort>? NewlineInterrupt { get; set; }

    /// <summary>Where pictures have been drawn since the screen was last cleared.</summary>
    public IReadOnlyList<PicturePlacement> Pictures => _pictures;

    /// <summary>The pictures the game can draw, if a resource file gave any.</summary>
    public BlorbPictures? PictureCatalog => _catalog;

    /// <summary>
    /// Whether pictures can be used: the resource file has some and
    /// the frontend declares it can show them.
    /// </summary>
    public bool PicturesAvailable => _catalog is { Count: > 0 } && _screen.Capabilities.HasFlag(ScreenCapabilities.Pictures);

    /// <summary>
    /// The cursor's row on the screen, in cells from 0, for a frontend
    /// that shows a cursor.
    /// </summary>
    public int CursorRow => Math.Clamp(RowOf(Current.Y + Current.CursorY - 1), 0, Height - 1);

    /// <summary>The cursor's column on the screen, in cells from 0.</summary>
    public int CursorColumn => Math.Clamp(ColumnOf(Current.X + Current.CursorX - 1), 0, Width - 1);

    public int TableColumn => Current.CursorX;

    /// <summary>
    /// The attributes text is printed in right now: the current
    /// window's style, forced to fixed pitch where [zm 8.1] the
    /// header's bit or font 4 says so, and its colors and font.
    /// </summary>
    public TextAttributes Attributes
    {
        get
        {
            var window = Current;
            var style = window.Style;
            if (window.Font == TextAttributes.FixedPitchFont || _header.Flags2.HasFlag(Flags2.ForceFixedPitch))
            {
                style |= TextStyle.FixedPitch;
            }

            return new TextAttributes(style, window.Foreground, window.Background, window.Font);
        }
    }

    /// <summary>The cell at a row and column, counted from 0.</summary>
    public Cell this[int row, int column]
    {
        get
        {
            EnsureSize();
            return _cells[row][column];
        }
    }

    /// <summary>The characters of a row as a string, for display and tests.</summary>
    public string RowText(int row) => string.Concat(_cells[row].Select(c => c.Character));

    private bool IsGrid => _screen.Capabilities.HasFlag(ScreenCapabilities.FixedGrid);

    /// <summary>
    /// [zm 8.8.3.3] Puts the windows as they are at the start: all at
    /// (1,1), window 0 filling the screen and selected, window 1 as
    /// wide as the screen with no height, the rest with no size at
    /// all. Window 0 wraps, scrolls, copies to the transcript, and
    /// buffers; the others only buffer.
    /// </summary>
    /// <remarks>
    /// [zm 8.8.3.3] numbers the attributes from 1 in one sentence and
    /// from 0 elsewhere, and read literally would leave window 0
    /// without wrapping, which [zm 8.8.3.1.1] says it has by default.
    /// Frotz gives window 0 all four, and so does this.
    /// </remarks>
    public void Reset()
    {
        _word.Clear();
        _pictures.Clear();
        _quiet = false;
        _split = false;
        _pagingSuppressed = false;
        CursorVisible = true;
        CurrentWindow = 0;

        foreach (var window in _windows)
        {
            window.Y = 1;
            window.X = 1;
            window.Height = 0;
            window.Width = 0;
            window.CursorY = 1;
            window.CursorX = 1;
            window.LeftMargin = 0;
            window.RightMargin = 0;
            window.NewlineInterruptRoutine = 0;
            window.InterruptCountdown = 0;
            window.Style = TextStyle.Roman;
            window.Foreground = _screen.DefaultForeground;
            window.Background = _screen.DefaultBackground;
            window.TrueForeground = (short)ScreenColors.ToTrueColor(_screen.DefaultForeground);
            window.TrueBackground = (short)ScreenColors.ToTrueColor(_screen.DefaultBackground);
            window.Font = TextAttributes.NormalFont;
            window.FontWidth = FontWidth;
            window.FontHeight = FontHeight;
            window.LineCount = 0;
            window.Attributes = window.Number == 0
                ? WindowAttributes.Wrapping | WindowAttributes.Scrolling | WindowAttributes.Transcript | WindowAttributes.Buffering
                : WindowAttributes.Buffering;
        }

        _windows[0].Height = UnitsHigh;
        _windows[0].Width = UnitsWide;
        _windows[1].Width = UnitsWide;

        _cells = NewCells(Width, Height);
        FillCells(0, 0, Height, Width, Cell.Blank(BlankFor(_windows[0])));
        _screen.EraseLowerWindow(_windows[0].Background);
        _streamRow = -1;
        _streamColumn = 0;
        _changed = true;
        Sync();
    }

    /// <summary>
    /// [zm 8.8.6] Gives the model the pictures from the resource file.
    /// </summary>
    public void UsePictures(BlorbPictures pictures)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        _catalog = pictures;
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
        else if (Zscii.ToUnicode(zscii, ZMachineVersion.V6, _extraCharacters) is { } character)
        {
            Print(character);
        }
    }

    /// <summary>
    /// Prints one character to the current window.
    /// </summary>
    /// <remarks>
    /// [zm 8.8.3.1.2] With buffering on, the character joins the word
    /// being built, and the word is placed when a space or a newline
    /// ends it, so that it wraps whole. Without buffering the
    /// character goes straight to the screen and wraps wherever it
    /// must.
    /// </remarks>
    public void Print(char character)
    {
        if (!Current.Buffers)
        {
            Place(character, Attributes);
        }
        else if (character == ' ')
        {
            FlushWord();
            PlaceSpace();
        }
        else
        {
            _word.Add((character, Attributes));
        }
    }

    public void PrintUnicode(char character) =>
        Print(CanPrint(character) ? character : '?');

    public bool CanPrint(char character) => _screen.CanPrint(character) && !char.IsControl(character);

    /// <summary>
    /// [zm op:new_line] Ends the line in the current window: the cursor
    /// goes to the left margin of the next line, and a window that has
    /// run out of lines scrolls if it may.
    /// </summary>
    public void NewLine()
    {
        FlushWord();
        NewLineIn(Current);
    }

    public void NextTableRow(int startColumn)
    {
        FlushWord();
        var window = Current;
        window.CursorY += FontHeight;
        window.CursorX = startColumn;
        _changed = true;
    }

    /// <summary>
    /// [zm op:set_window] Selects the window text goes to. Each window
    /// keeps its own cursor, so nothing else moves.
    /// </summary>
    /// <returns>False if the number names no window.</returns>
    public bool SetWindow(int window)
    {
        if (Resolve(window) is not { } target)
        {
            return false;
        }

        FlushWord();
        CurrentWindow = target.Number;
        _changed = true;
        Sync();
        return true;
    }

    /// <summary>
    /// [zm 8.8.4.1] Tiles windows 0 and 1 vertically: window 1 gets
    /// the given height at the top and window 0 what is left below.
    /// </summary>
    /// <remarks>
    /// [zm op:split_window] A cursor keeps its absolute position on
    /// the screen, which changes its position relative to a window
    /// whose origin moved, unless it has left the window altogether,
    /// in which case it goes to the window's top left.
    /// </remarks>
    public void SplitWindow(int units)
    {
        FlushWord();
        _split = true;
        units = Math.Clamp(units, 0, UnitsHigh);

        var top = _windows[1];
        var bottom = _windows[0];
        var topCursor = top.Y + top.CursorY - 1;
        var bottomCursor = bottom.Y + bottom.CursorY - 1;

        top.Y = 1;
        top.Height = units;
        bottom.Y = units + 1;
        bottom.Height = UnitsHigh - units;

        Restore(top, topCursor);
        Restore(bottom, bottomCursor);
        _changed = true;
        Sync();

        static void Restore(ZWindow window, int absoluteY)
        {
            var relative = absoluteY - window.Y + 1;
            if (relative < 1 || relative + window.FontHeight - 1 > window.Height)
            {
                window.CursorY = 1;
                window.CursorX = window.LeftMargin + 1;
            }
            else
            {
                window.CursorY = relative;
            }
        }
    }

    /// <summary>
    /// [zm op:erase_window] Clears a window to its background, or with
    /// -1 the whole screen unsplit, or with -2 the whole screen as it
    /// is.
    /// </summary>
    /// <remarks>
    /// [zm 8.8.5.3.1] Erasing -1 clears everything to window 0's
    /// background, [zm 8.8.4.2] unsplits if a split has happened,
    /// and selects window 0. [zm 8.8.5.3.2] Erasing -2 clears to the
    /// current background and changes nothing else. A window's own
    /// cursor goes back to its top left when it is erased.
    /// </remarks>
    /// <returns>False if the number names no window.</returns>
    public bool EraseWindow(int window)
    {
        FlushWord();

        switch (window)
        {
            case -1:
                FillCells(0, 0, Height, Width, Cell.Blank(BlankFor(_windows[0])));
                _pictures.Clear();
                if (_split)
                {
                    SplitWindow(0);
                }

                CurrentWindow = 0;
                ResetCursor(_windows[0]);
                _screen.EraseLowerWindow(_windows[0].Background);
                _streamColumn = 0;
                _streamRow = -1;
                break;
            case -2:
                FillCells(0, 0, Height, Width, Cell.Blank(BlankFor(Current)));
                _pictures.Clear();
                _screen.EraseLowerWindow(Current.Background);
                _streamColumn = 0;
                _streamRow = -1;
                break;
            case >= 0 and < WindowCount:
            {
                var target = _windows[window];
                var (top, left, rows, columns) = CellRect(target);
                FillCells(top, left, rows, columns, Cell.Blank(BlankFor(target)));
                ResetCursor(target);
                break;
            }

            default:
                return false;
        }

        _changed = true;
        Sync();
        return true;
    }

    /// <summary>
    /// [zm op:erase_line] Clears from the cursor: with 1 to the right
    /// margin, otherwise the given number of units less one, clipped
    /// to the margin. [zm 8.8.5.2] The cursor does not move.
    /// </summary>
    public void EraseLine(int value)
    {
        FlushWord();
        var window = Current;
        var from = window.X + window.CursorX - 1;
        var limit = RightLimit(window);
        var to = value == 1 ? limit : Math.Min(from + value - 2, limit);

        if (to >= from)
        {
            var row = RowOf(window.Y + window.CursorY - 1);
            var first = ColumnOf(from);
            var last = ColumnOf(to);
            if (row >= 0 && row < Height && window.CursorY <= window.Height)
            {
                FillCells(row, first, 1, last - first + 1, Cell.Blank(BlankFor(window)));
            }
        }

        _changed = true;
    }

    /// <summary>
    /// [zm op:set_cursor] Moves a window's cursor, or with y of -1
    /// hides the cursor and -2 shows it again.
    /// </summary>
    /// <remarks>
    /// [zm 8.8.3.5] It is illegal to move the cursor outside the
    /// window, and a position outside is brought inside rather than
    /// reported, since the Infocom games do it in passing. A position
    /// outside the margins goes to the left margin of its line, as
    /// the opcode says.
    /// </remarks>
    /// <returns>False if the number names no window.</returns>
    public bool SetCursor(int y, int x, int window = CurrentWindowCode)
    {
        FlushWord();

        if (y == -1)
        {
            CursorVisible = false;
            return true;
        }

        if (y == -2)
        {
            CursorVisible = true;
            return true;
        }

        if (Resolve(window) is not { } target)
        {
            return false;
        }

        target.CursorY = Math.Clamp(y, 1, Math.Max(target.Height, 1));
        target.CursorX = Math.Clamp(x, 1, Math.Max(target.Width, 1));
        KeepInsideMargins(target);
        _changed = true;
        return true;
    }

    /// <summary>
    /// [zm op:get_cursor] The current window's cursor as (y, x) in
    /// units, [zm 8.8.3.2.7] after flushing buffered text so that it
    /// is accurate.
    /// </summary>
    public (int Y, int X) GetCursor()
    {
        FlushWord();
        return (Current.CursorY, Current.CursorX);
    }

    public void SetTextStyle(int style)
    {
        var window = Current;
        window.Style = style == 0 ? TextStyle.Roman : window.Style | (TextStyle)(style & 0x0F);
    }

    /// <summary>
    /// [zm op:buffer_mode] Left undefined in Version 6 by the standard;
    /// this does what Frotz does and sets the current window's
    /// buffering attribute.
    /// </summary>
    public void SetBuffering(bool enabled)
    {
        FlushWord();
        SetAttributes(Current, WindowAttributes.Buffering, enabled);
    }

    /// <summary>
    /// [zm op:set_colour] Sets a window's colors, with 0 meaning keep
    /// the current one, 1 the default, and [zm 8.3.1] -1 the color
    /// under the cursor.
    /// </summary>
    /// <returns>
    /// False if a number is not a color, or [zm 8.3.6] if transparent
    /// is asked for as a foreground, which is a diagnostic and is
    /// otherwise ignored.
    /// </returns>
    public bool SetColors(ScreenColor foreground, ScreenColor background, int window = CurrentWindowCode)
    {
        FlushWord();

        if (Resolve(window) is not { } target
            || !ScreenColors.IsLegal(foreground, ZMachineVersion.V6) || !ScreenColors.IsLegal(background, ZMachineVersion.V6)
            || foreground == ScreenColor.Transparent)
        {
            return false;
        }

        var under = CellUnderCursor(target);
        target.Foreground = Choose(foreground, target.Foreground, _screen.DefaultForeground, under.Attributes.Foreground);
        target.Background = Choose(background, target.Background, _screen.DefaultBackground, under.Attributes.Background);
        target.TrueForeground = (short)ScreenColors.ToTrueColor(target.Foreground);
        target.TrueBackground = target.Background == ScreenColor.Transparent
            ? ScreenColors.TrueTransparent
            : (short)ScreenColors.ToTrueColor(target.Background);
        return true;

        static ScreenColor Choose(ScreenColor asked, ScreenColor current, ScreenColor fallback, ScreenColor underCursor) => asked switch
        {
            ScreenColor.Current => current,
            ScreenColor.Default => fallback,
            ScreenColor.UnderCursor => underCursor,
            _ => asked,
        };
    }

    /// <summary>
    /// [zm op:set_true_colour] Sets a window's colors from 15-bit
    /// values, shown as the nearest standard colors, with the exact
    /// values kept for [zm 8.8.3.2.8] the true color properties.
    /// </summary>
    public bool SetTrueColors(short foreground, short background, int window = CurrentWindowCode)
    {
        FlushWord();

        if (Resolve(window) is not { } target || foreground == ScreenColors.TrueTransparent || foreground < ScreenColors.TrueTransparent
            || background < ScreenColors.TrueTransparent)
        {
            return false;
        }

        var under = CellUnderCursor(target);
        target.Foreground = Translate(foreground, target.Foreground, _screen.DefaultForeground, under.Attributes.Foreground);
        target.Background = Translate(background, target.Background, _screen.DefaultBackground, under.Attributes.Background);
        target.TrueForeground = foreground >= 0 ? foreground : (short)ScreenColors.ToTrueColor(target.Foreground);
        target.TrueBackground = background switch
        {
            >= 0 => background,
            ScreenColors.TrueTransparent => ScreenColors.TrueTransparent,
            _ => (short)ScreenColors.ToTrueColor(target.Background),
        };
        return true;

        static ScreenColor Translate(short trueColor, ScreenColor current, ScreenColor fallback, ScreenColor underCursor) => trueColor switch
        {
            ScreenColors.TrueDefault => fallback,
            ScreenColors.TrueCurrent => current,
            ScreenColors.TrueUnderCursor => underCursor,
            ScreenColors.TrueTransparent => ScreenColor.Transparent,
            _ => ScreenColors.Nearest((ushort)trueColor, ZMachineVersion.V6),
        };
    }

    /// <summary>
    /// [zm op:set_font] Selects a font for a window if it is available.
    /// </summary>
    /// <returns>
    /// The previous font, or 0 if the one asked for is unavailable or
    /// the window does not exist, or the current font when 0 is asked
    /// for.
    /// </returns>
    public int SetFont(int font, int window = CurrentWindowCode)
    {
        if (Resolve(window) is not { } target)
        {
            return 0;
        }

        if (font == 0)
        {
            return target.Font;
        }

        if (!IsFontAvailable(font))
        {
            return 0;
        }

        var previous = target.Font;
        target.Font = font;
        return previous;
    }

    /// <summary>
    /// [zm 8.1.2] Which fonts the frontend can show, as for the other
    /// versions.
    /// </summary>
    public bool IsFontAvailable(int font) => font switch
    {
        TextAttributes.NormalFont => true,
        TextAttributes.CharacterGraphicsFont => _screen.Capabilities.HasFlag(ScreenCapabilities.CharacterGraphicsFont),
        TextAttributes.FixedPitchFont => _screen.Capabilities.HasFlag(ScreenCapabilities.FixedPitch),
        _ => false,
    };

    /// <summary>
    /// [zm op:set_margins] Sets a window's margins, and if the cursor
    /// is now outside them moves it to the left margin.
    /// </summary>
    public bool SetMargins(int left, int right, int window = CurrentWindowCode)
    {
        if (Resolve(window) is not { } target)
        {
            return false;
        }

        FlushWord();
        target.LeftMargin = Math.Max(left, 0);
        target.RightMargin = Math.Max(right, 0);
        KeepInsideMargins(target);
        return true;
    }

    /// <summary>
    /// [zm op:move_window] Moves a window. Nothing on the screen
    /// changes; only where the next thing goes.
    /// </summary>
    public bool MoveWindow(int window, int y, int x)
    {
        if (Resolve(window) is not { } target)
        {
            return false;
        }

        FlushWord();
        target.Y = y;
        target.X = x;
        return true;
    }

    /// <summary>
    /// [zm op:window_size] Resizes a window. [zm 8.8.3.4] A cursor
    /// left outside goes to the left margin on the top line.
    /// </summary>
    public bool WindowSize(int window, int height, int width)
    {
        if (Resolve(window) is not { } target)
        {
            return false;
        }

        FlushWord();
        target.Height = Math.Max(height, 0);
        target.Width = Math.Max(width, 0);

        if (target.CursorY + FontHeight - 1 > target.Height || target.CursorX + FontWidth - 1 > target.Width)
        {
            ResetCursor(target);
        }

        return true;
    }

    /// <summary>
    /// [zm op:window_style] Changes a window's attributes: sets them
    /// outright (0), sets the given bits (1), clears them (2), or
    /// flips them (3).
    /// </summary>
    public bool WindowStyle(int window, int flags, int operation)
    {
        if (Resolve(window) is not { } target)
        {
            return false;
        }

        FlushWord();
        var bits = (WindowAttributes)(flags & 0x0F);
        target.Attributes = operation switch
        {
            1 => target.Attributes | bits,
            2 => target.Attributes & ~bits,
            3 => target.Attributes ^ bits,
            _ => bits,
        };
        return true;
    }

    /// <summary>
    /// [zm op:get_wind_prop] Reads a window property, [zm 8.8.3.2.7]
    /// after flushing buffered text so that the cursor is accurate.
    /// </summary>
    /// <returns>Null if the window or property does not exist.</returns>
    public int? GetProperty(int window, int property)
    {
        if (Resolve(window) is not { } target || property is < 0 or > 17)
        {
            return null;
        }

        FlushWord();
        return target.Property(property);
    }

    /// <summary>
    /// [zm op:put_wind_prop] Writes a window property. Only 0 to 15
    /// may be written.
    /// </summary>
    /// <returns>False if the window or property does not exist or cannot be written.</returns>
    public bool PutProperty(int window, int property, int value)
    {
        if (Resolve(window) is not { } target)
        {
            return false;
        }

        FlushWord();
        return target.PutProperty(property, value);
    }

    /// <summary>
    /// [zm op:scroll_window] Scrolls a window's contents up by the
    /// given number of units, or down for a negative number, filling
    /// with the window's background. [zm 8.8.3.6] Any window can be
    /// scrolled, whatever its scrolling attribute.
    /// </summary>
    public bool ScrollWindow(int window, int units)
    {
        if (Resolve(window) is not { } target)
        {
            return false;
        }

        FlushWord();
        ScrollCells(target, units);
        _changed = true;
        return true;
    }

    /// <summary>
    /// [zm op:picture_data] The size a picture will be drawn at, in
    /// units, or null if there is no such picture.
    /// </summary>
    public (int Height, int Width)? PictureSize(int number)
    {
        if (!PicturesAvailable || _catalog!.ScaledSize(number, UnitsWide, UnitsHigh) is not { } size)
        {
            return null;
        }

        return (size.Height, size.Width);
    }

    /// <summary>
    /// [zm op:picture_data] For picture 0: how many pictures there
    /// are and the release number of the picture file.
    /// </summary>
    public (int Count, int Release) PictureSummary =>
        PicturesAvailable ? (_catalog!.Count, _catalog.Release) : (0, 0);

    /// <summary>
    /// [zm op:draw_picture] Draws a picture at (y,x) in the current
    /// window, or at the cursor for a coordinate of 0. Here that means
    /// clearing its space and noting where it went.
    /// </summary>
    /// <returns>
    /// False if there is no such picture, or [blorb 2.3] it is a
    /// placeholder rectangle, which may not be drawn.
    /// </returns>
    public bool DrawPicture(int number, int y, int x)
    {
        if (!PicturesAvailable)
        {
            // The game was told there are no pictures. Zork Zero draws
            // them anyway, so the honest thing is to do nothing, as an
            // interpreter without graphics always has.
            return true;
        }

        if (_catalog!.Find(number) is not { } picture || picture.Kind == PictureKind.Rectangle || PictureSize(number) is not { } size)
        {
            return false;
        }

        FlushWord();
        if (PictureCells(size, y, x) is { } rect)
        {
            FillCells(rect.Top, rect.Left, rect.Rows, rect.Columns, Cell.Blank(BlankFor(Current)));
            _pictures.Add(new PicturePlacement(number, rect.Top, rect.Left, rect.Rows, rect.Columns));
            _changed = true;
        }

        return true;
    }

    /// <summary>
    /// [zm op:erase_picture] Clears the space a picture would occupy
    /// to the current window's background.
    /// </summary>
    /// <returns>False if there is no such picture.</returns>
    public bool ErasePicture(int number, int y, int x)
    {
        if (!PicturesAvailable)
        {
            return true;
        }

        if (PictureSize(number) is not { } size)
        {
            return false;
        }

        FlushWord();
        if (PictureCells(size, y, x) is { } rect)
        {
            FillCells(rect.Top, rect.Left, rect.Rows, rect.Columns, Cell.Blank(BlankFor(Current)));
            _pictures.RemoveAll(p => p.Row >= rect.Top && p.Column >= rect.Left
                && p.Row + p.Rows <= rect.Top + rect.Rows && p.Column + p.Columns <= rect.Left + rect.Columns);
            _changed = true;
        }

        return true;
    }

    public void PrepareForInput(bool suppressPaging)
    {
        FlushWord();
        _pagingSuppressed = suppressPaging;

        // [zm 8.8.3.2.6] The player has had a chance to read, so every
        // window's count of unread lines starts over.
        foreach (var window in _windows)
        {
            if (window.LineCount != ZWindow.NeverPause)
            {
                window.LineCount = 0;
            }
        }

        _changed = true;
        Sync();
    }

    /// <summary>
    /// Records typed input at the cursor, since the frontend showed it
    /// as it was typed and the cells and cursor must agree with what
    /// is on the screen.
    /// </summary>
    public void InputEnded(LineInput? typed)
    {
        if (typed is null)
        {
            return;
        }

        _quiet = true;
        try
        {
            foreach (var code in typed.Text)
            {
                if (Zscii.ToUnicode(code, ZMachineVersion.V6, _extraCharacters) is { } character)
                {
                    Place(character, Attributes);
                }
            }

            if (typed.Terminator == Zscii.Newline)
            {
                NewLineIn(Current);
            }
        }
        finally
        {
            _quiet = false;
        }

        _changed = true;
    }

    public void Flush()
    {
        FlushWord();
        FlushStream();
        Sync();
    }

    // The window a number names: 0 to 7, or -3 for the current one.
    private ZWindow? Resolve(int window) => window switch
    {
        CurrentWindowCode => Current,
        >= 0 and < WindowCount => _windows[window],
        _ => null,
    };

    private TextAttributes BlankFor(ZWindow window) =>
        new(TextStyle.Roman, window.Foreground, window.Background == ScreenColor.Transparent ? _screen.DefaultBackground : window.Background, window.Font);

    private static void SetAttributes(ZWindow window, WindowAttributes bit, bool on) =>
        window.Attributes = on ? window.Attributes | bit : window.Attributes & ~bit;

    private static void ResetCursor(ZWindow window)
    {
        window.CursorY = 1;
        window.CursorX = window.LeftMargin + 1;
        if (window.LineCount != ZWindow.NeverPause)
        {
            window.LineCount = 0;
        }
    }

    // [zm op:set_cursor] and [zm op:set_margins] A cursor outside the
    // margins goes to the left margin of its line.
    private void KeepInsideMargins(ZWindow window)
    {
        if (window.CursorX < window.LeftMargin + 1 || window.CursorX + FontWidth - 1 > window.Width - window.RightMargin)
        {
            window.CursorX = window.LeftMargin + 1;
        }
    }

    // The last usable x coordinate on the current line of a window, in
    // absolute units.
    private static int RightLimit(ZWindow window) => window.X + window.Width - 1 - window.RightMargin;

    private static bool Fits(ZWindow window, int units) =>
        window.X + window.CursorX - 1 + units - 1 <= RightLimit(window);

    private Cell CellUnderCursor(ZWindow window)
    {
        var row = RowOf(window.Y + window.CursorY - 1);
        var column = ColumnOf(window.X + window.CursorX - 1);
        return row >= 0 && row < Height && column >= 0 && column < Width ? _cells[row][column] : Cell.Blank(BlankFor(window));
    }

    // [zm 8.8.3.1.2] Places the buffered word: on a new line first if
    // it would otherwise spread across two and the window wraps.
    private void FlushWord()
    {
        if (_word.Count == 0)
        {
            return;
        }

        var window = Current;
        if (window.Wraps && !Fits(window, _word.Count * FontWidth) && window.CursorX > window.LeftMargin + 1)
        {
            NewLineIn(window);
        }

        var word = _word.ToArray();
        _word.Clear();
        foreach (var (character, attributes) in word)
        {
            Place(character, attributes);
        }
    }

    // A space that does not fit is swallowed rather than wrapped, so
    // that the next word starts the new line.
    private void PlaceSpace()
    {
        if (!Fits(Current, FontWidth))
        {
            return;
        }

        Place(' ', Attributes);
    }

    // Puts one character at the cursor of the current window, wrapping
    // or dropping it when the line is full, and advances the cursor.
    private void Place(char character, TextAttributes attributes)
    {
        var window = Current;

        if (!Fits(window, FontWidth))
        {
            if (!window.Wraps)
            {
                // [zm 8.8.3.1.1] The cursor moves to the right margin and
                // stays there, and further text is ignored.
                window.CursorX = Math.Max(window.Width - window.RightMargin + 1, 1);
                return;
            }

            NewLineIn(window);
            if (!Fits(window, FontWidth))
            {
                return;
            }
        }

        // [zm 8.8.3] Text is clipped to the window, which here means
        // its width: Zork Zero keeps its status text in a window five
        // units high and expects it to show, as Infocom's own
        // interpreters showed it, so height is not clipped, only the
        // screen's edge.
        EnsureSize();
        var row = RowOf(window.Y + window.CursorY - 1);
        var column = ColumnOf(window.X + window.CursorX - 1);
        var inside = row >= 0 && row < Height && column >= 0 && column < Width;

        if (inside)
        {
            // [zm 8.3.6] Transparent text takes whatever background is
            // already there.
            if (attributes.Background == ScreenColor.Transparent)
            {
                attributes = attributes with { Background = _cells[row][column].Attributes.Background };
            }

            _cells[row][column] = new Cell(character, attributes);
        }

        window.CursorX += FontWidth;
        _changed = true;

        if (!_quiet && !IsGrid)
        {
            Stream(character, attributes, row);
        }
    }

    // [zm 8.8.3.2.2] The newline itself, with the interrupt countdown
    // before it, or [zm 8.8.3.2.2.1] after it for Zork Zero.
    private void NewLineIn(ZWindow window)
    {
        if (!ZorkZeroInterruptOrder)
        {
            Countdown(window);
        }

        window.CursorX = window.LeftMargin + 1;
        window.CursorY += FontHeight;

        if (window.CursorY + FontHeight - 1 > window.Height)
        {
            window.CursorY = Math.Max(window.CursorY - FontHeight, 1);

            if (window.Scrolls)
            {
                PauseIfDue(window);
                ScrollCells(window, FontHeight);
            }

            // A window that does not scroll keeps its cursor on its last
            // line, as the upper window does before Version 6, and text
            // printed there overprints.
        }

        if (window.LineCount != ZWindow.NeverPause)
        {
            window.LineCount++;
        }

        if (ZorkZeroInterruptOrder)
        {
            Countdown(window);
        }

        _changed = true;

        if (!_quiet && !IsGrid)
        {
            FlushStream();
            _screen.NewLine();
            _streamColumn = 0;
            _streamRow = -1;
        }
    }

    private void Countdown(ZWindow window)
    {
        if (window.InterruptCountdown != 0 && --window.InterruptCountdown == 0)
        {
            NewlineInterrupt?.Invoke(window.NewlineInterruptRoutine);
        }
    }

    // [zm 8.8.3.2.6] The frontend pauses when a window is about to
    // scroll away lines the player has not had a chance to read.
    private void PauseIfDue(ZWindow window)
    {
        if (!IsGrid || _pagingSuppressed || _quiet || window.LineCount == ZWindow.NeverPause)
        {
            return;
        }

        var lines = window.Height / FontHeight;
        if (lines >= 2 && window.LineCount >= lines - 1)
        {
            _changed = true;
            Sync();
            _screen.MorePrompt();
            window.LineCount = 0;
        }
    }

    private void ScrollCells(ZWindow window, int units)
    {
        EnsureSize();
        var (top, left, rows, columns) = CellRect(window);
        if (rows <= 0 || columns <= 0)
        {
            return;
        }

        var by = units / FontHeight;
        var blank = Cell.Blank(BlankFor(window));

        if (Math.Abs(by) >= rows)
        {
            FillCells(top, left, rows, columns, blank);
            return;
        }

        if (by > 0)
        {
            for (var row = top; row < top + rows - by; row++)
            {
                Array.Copy(_cells[row + by], left, _cells[row], left, columns);
            }

            FillCells(top + rows - by, left, by, columns, blank);
        }
        else if (by < 0)
        {
            by = -by;
            for (var row = top + rows - 1; row >= top + by; row--)
            {
                Array.Copy(_cells[row - by], left, _cells[row], left, columns);
            }

            FillCells(top, left, by, columns, blank);
        }
    }

    // A picture's cells: at (y,x) in the current window, or at the
    // cursor for a zero coordinate, clipped to the window and screen.
    private (int Top, int Left, int Rows, int Columns)? PictureCells((int Height, int Width) size, int y, int x)
    {
        var window = Current;
        var absoluteY = window.Y + (y == 0 ? window.CursorY : y) - 1;
        var absoluteX = window.X + (x == 0 ? window.CursorX : x) - 1;

        var top = Math.Max(RowOf(absoluteY), RowOf(window.Y));
        var left = Math.Max(ColumnOf(absoluteX), ColumnOf(window.X));
        var bottom = Math.Min(LastRowOf(absoluteY, size.Height), LastRowOf(window.Y, window.Height));
        var right = Math.Min(LastColumnOf(absoluteX, size.Width), LastColumnOf(window.X, window.Width));

        top = Math.Max(top, 0);
        left = Math.Max(left, 0);
        bottom = Math.Min(bottom, Height - 1);
        right = Math.Min(right, Width - 1);

        if (bottom < top || right < left || size.Height <= 0 || size.Width <= 0)
        {
            return null;
        }

        return (top, left, bottom - top + 1, right - left + 1);
    }

    // The cells a window covers, clipped to the screen.
    private (int Top, int Left, int Rows, int Columns) CellRect(ZWindow window)
    {
        if (window.Height <= 0 || window.Width <= 0)
        {
            return (0, 0, 0, 0);
        }

        var top = Math.Max(RowOf(window.Y), 0);
        var left = Math.Max(ColumnOf(window.X), 0);
        var bottom = Math.Min(LastRowOf(window.Y, window.Height), Height - 1);
        var right = Math.Min(LastColumnOf(window.X, window.Width), Width - 1);
        return bottom < top || right < left ? (0, 0, 0, 0) : (top, left, bottom - top + 1, right - left + 1);
    }

    // The cell a character at a position in units lands in: the one
    // holding the character's middle, so that a character straddling
    // two cells goes where most of it is. Zork Zero keeps a status band
    // five units high and starts its text at unit 6, and the middle
    // rule puts the band's text in the first row and the text's first
    // line in the second, as the pixels would.
    private int RowOf(int unitsY) => (int)Math.Floor((unitsY - 1 + (FontHeight / 2)) / (double)FontHeight);

    private int ColumnOf(int unitsX) => (int)Math.Floor((unitsX - 1 + (FontWidth / 2)) / (double)FontWidth);

    // The last row or column a window or picture covers: that of the
    // last whole character that fits inside it.
    private int LastRowOf(int unitsY, int unitsHigh) => RowOf(Math.Max(unitsY + unitsHigh - FontHeight, unitsY));

    private int LastColumnOf(int unitsX, int unitsWide) => ColumnOf(Math.Max(unitsX + unitsWide - FontWidth, unitsX));

    // The frontend may have changed size since the cells were made, as
    // a terminal does when its window is dragged. The cells follow, and
    // what still fits is kept; the windows stay as the game laid them
    // out, since they are the game's to move.
    private void EnsureSize()
    {
        if (_cells.Length == Height && _cells[0].Length == Width)
        {
            return;
        }

        var cells = NewCells(Width, Height);
        var blank = Cell.Blank(BlankFor(_windows[0]));
        for (var row = 0; row < Height; row++)
        {
            Array.Fill(cells[row], blank);
            if (row < _cells.Length)
            {
                Array.Copy(_cells[row], cells[row], Math.Min(Width, _cells[row].Length));
            }
        }

        _cells = cells;
        _changed = true;
    }

    private void FillCells(int top, int left, int rows, int columns, Cell cell)
    {
        EnsureSize();
        for (var row = Math.Max(top, 0); row < Math.Min(top + rows, Height); row++)
        {
            var from = Math.Max(left, 0);
            var count = Math.Min(left + columns, Width) - from;
            if (count > 0)
            {
                Array.Fill(_cells[row], cell, from, count);
            }
        }
    }

    private static Cell[][] NewCells(int width, int height)
    {
        var cells = new Cell[height][];
        for (var row = 0; row < height; row++)
        {
            cells[row] = new Cell[width];
        }

        return cells;
    }

    // For a frontend that takes text as a stream rather than painting
    // the grid: text is sent as it is placed, with a line break whenever
    // the cursor has moved to another row since the last text.
    private void Stream(char character, TextAttributes attributes, int row)
    {
        if (_streamRow != row && _streamColumn > 0)
        {
            FlushStream();
            _screen.NewLine();
            _streamColumn = 0;
        }

        if (_streamRun.Length > 0 && _streamAttributes != attributes)
        {
            FlushStream();
        }

        _streamAttributes = attributes;
        _streamRun.Append(character);
        _streamRow = row;
        _streamColumn++;
    }

    private void FlushStream()
    {
        if (_streamRun.Length > 0)
        {
            _screen.Print(_streamRun.ToString(), _streamAttributes);
            _streamRun.Clear();
        }
    }

    private void Sync()
    {
        if (!_changed)
        {
            return;
        }

        _changed = false;
        FlushStream();
        _screen.UpdateWindows(this);
    }
}

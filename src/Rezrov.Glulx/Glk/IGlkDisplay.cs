namespace Rezrov.Glulx.Glk;

/// <summary>
/// What a frontend provides for Glk to show its windows on and take the
/// player's input from: a size, a place for text buffer output to go as
/// it is printed, and a way to wait for a line, a key, or the timer.
/// </summary>
/// <remarks>
/// The library keeps the window tree, the layout, and the contents of
/// text grid windows itself; a frontend reads those when it paints.
/// Text buffer output is different, since a buffer is a stream of text
/// that a frontend may show as it arrives, so it is handed over one
/// character at a time.
///
/// [glk #line_events] Showing the line the player types in a text
/// buffer window is the display's job, since a console shows it as it
/// is typed; the library only writes it into the game's buffer, into a
/// text grid, and to the echo stream.
/// </remarks>
public interface IGlkDisplay
{
    /// <summary>
    /// The width of the whole display in character cells.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// The height of the whole display in character cells.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// [glk #window_textbuf] A character printed to a text buffer
    /// window, in the stream's current style, and [glk #link_creating]
    /// as part of the link of that value, or of no link for zero.
    /// </summary>
    void Print(GlkWindow window, uint character, GlkStyle style, uint link);

    /// <summary>
    /// [glk #mouse_events] Whether a click in a window of this kind can
    /// be reported, which the gestalt answer passes to the game. A
    /// display with no pointer, and the default here, says no.
    /// </summary>
    bool CanReportMouse(WindowType type) => false;

    /// <summary>
    /// [glk #link_testing] Whether a link selected in a window of this
    /// kind can be reported, on the same terms.
    /// </summary>
    bool CanReportHyperlinks(WindowType type) => false;

    /// <summary>[glk op:window_clear] A window was cleared.</summary>
    void Clear(GlkWindow window);

    /// <summary>
    /// [glk #window_arrangement] The window tree changed or was laid
    /// out again, with <paramref name="root"/> at its top, or null when
    /// the last window closed.
    /// </summary>
    void Arranged(GlkWindow? root);

    /// <summary>
    /// [glk op:select] Waits for the player: a line in one of the
    /// windows with a line request, whose request says what is already
    /// in the buffer, a key in one with a character request, or the
    /// timer after <paramref name="timeout"/> if that comes first.
    /// With nothing requested and no timer, there is nothing to wait
    /// for and the answer is that input has ended.
    /// </summary>
    GlkInput WaitForInput(IReadOnlyList<GlkWindow> lineRequests, IReadOnlyList<GlkWindow> charRequests, TimeSpan? timeout);

    /// <summary>
    /// [glk #sound_playing] An event became ready on another thread, a
    /// sound's end, while the display may be waiting: a wait under way
    /// should return <see cref="GlkInput.Woken"/> as soon as it can,
    /// so the library can hand the event over. A display whose waits
    /// cannot be cut short, or that is never asked to play sounds, may
    /// do nothing.
    /// </summary>
    void Wake();
}

/// <summary>
/// A display over a TextWriter and a TextReader, which is what the
/// command line program has: one stream of text out, lines of text in,
/// no styles, and text grid windows kept but not shown.
/// </summary>
public sealed class TextWriterGlkDisplay : IGlkDisplay
{
    private readonly TextWriter _writer;
    private readonly TextReader _reader;
    private readonly bool _hasPointer;
    private GlkWindow? _root;

    /// <param name="writer">Where text buffer output goes.</param>
    /// <param name="reader">
    /// Where lines come from, or none for a display that never gets
    /// input.
    /// </param>
    /// <param name="width">The display's width in characters.</param>
    /// <param name="height">The display's height in lines.</param>
    /// <param name="hasPointer">
    /// [glk #mouse_events] Whether the reader can touch a window or
    /// select a link, by giving a line of the form <c>[click 3,1]</c>
    /// or <c>[link 5]</c> where input is expected. A player at a
    /// console has no pointer and the default is false; a script that
    /// means to exercise a game's links says otherwise.
    /// </param>
    public TextWriterGlkDisplay(TextWriter writer, TextReader? reader = null, int width = 80, int height = 24, bool hasPointer = false)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
        _reader = reader ?? TextReader.Null;
        _hasPointer = hasPointer;
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public void Print(GlkWindow window, uint character, GlkStyle style, uint link)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Type == WindowType.TextBuffer)
        {
            // [glk #link_creating] A stream of text has no way to show
            // that a run of it is a link, and no way to select one.
            _writer.Write(GlkText.ToString(character));
        }
    }

    public void Clear(GlkWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        // [glk #window_textbuf] Clearing a buffer may do any number of
        // things; on a plain stream of text, a blank line is the most
        // that can be done.
        if (window.Type == WindowType.TextBuffer)
        {
            _writer.WriteLine();
        }
    }

    public void Arranged(GlkWindow? root) => _root = root;

    public bool CanReportMouse(WindowType type) => _hasPointer && type == WindowType.TextGrid;

    public bool CanReportHyperlinks(WindowType type) => _hasPointer && type is WindowType.TextBuffer or WindowType.TextGrid;

    public GlkInput WaitForInput(IReadOnlyList<GlkWindow> lineRequests, IReadOnlyList<GlkWindow> charRequests, TimeSpan? timeout)
    {
        ArgumentNullException.ThrowIfNull(lineRequests);
        ArgumentNullException.ThrowIfNull(charRequests);

        // A line of text can only go to one window, so the first window
        // asking gets it. The reader echoes what it reads, or the
        // terminal already has, so the line is not printed again.
        _writer.Flush();
        if (lineRequests.Count > 0)
        {
            var line = _reader.ReadLine();
            return line is null ? GlkInput.Ended : Pointer(line) ?? GlkInput.Line(lineRequests[0], line);
        }

        // [glk #char_events] A key is a line too, on a console: its first
        // character, or the enter key for an empty line.
        if (charRequests.Count > 0)
        {
            var line = _reader.ReadLine();
            if (line is null)
            {
                return GlkInput.Ended;
            }

            if (Pointer(line) is { } pointed)
            {
                return pointed;
            }

            var key = line.Length == 0 ? GlkKeyCode.Return : (uint)char.ConvertToUtf32(line, 0);
            return GlkInput.KeyPress(charRequests[0], key);
        }

        // [glk #mouse_events] Nothing to type into, but a window may be
        // waiting to be touched, which is a line of its own kind.
        while (Listening(window => window.MouseRequest || window.HyperlinkRequest) is not null)
        {
            var line = _reader.ReadLine();
            if (line is null)
            {
                return GlkInput.Ended;
            }

            if (Pointer(line) is { } touched)
            {
                return touched;
            }
        }

        // [glk #timer_events] Nothing to type into, so the timer is all
        // there is to wait for.
        if (timeout is { } wait)
        {
            Thread.Sleep(wait);
            return GlkInput.Timer;
        }

        return GlkInput.Ended;
    }

    public void Wake()
    {
        // A console read cannot be cut short, and nothing here plays a
        // sound whose end would need to.
    }

    /// <summary>
    /// [glk #mouse_events] and [glk #link_events] A line that touches a
    /// window or selects a link rather than being typed into the game:
    /// <c>[click 3,1]</c> for the cell of whichever window is waiting
    /// for a click, and <c>[link 5]</c> for the window waiting for a
    /// link. A line of either shape with nothing waiting for it, and
    /// every other line, is typed as it stands, so that a script whose
    /// click lands nowhere says so plainly in its recording rather
    /// than quietly doing nothing.
    /// </summary>
    private GlkInput? Pointer(string line)
    {
        if (!_hasPointer || line.Length < 3 || line[0] != '[' || line[^1] != ']')
        {
            return null;
        }

        var inside = line[1..^1];

        if (inside.StartsWith("click ", StringComparison.Ordinal) && Listening(window => window.MouseRequest) is { } clicked)
        {
            var at = inside[6..].Split(',');
            if (at.Length == 2 && uint.TryParse(at[0], out var column) && uint.TryParse(at[1], out var row))
            {
                return GlkInput.MouseClick(clicked, column, row);
            }
        }

        if (inside.StartsWith("link ", StringComparison.Ordinal) && Listening(window => window.HyperlinkRequest) is { } selected
            && uint.TryParse(inside[5..], out var value) && value != 0)
        {
            return GlkInput.LinkSelected(selected, value);
        }

        return null;
    }

    // The first window of the tree that matches, or null for none. A
    // console has no pointer to aim, so the window waiting to be
    // touched is the one that is touched.
    private GlkWindow? Listening(Func<GlkWindow, bool> wanted) => Leaves(_root).FirstOrDefault(wanted);

    private static IEnumerable<GlkWindow> Leaves(GlkWindow? window) => window switch
    {
        null => [],
        PairWindow pair => Leaves(pair.First).Concat(Leaves(pair.Second)),
        _ => [window],
    };
}

/// <summary>Turning Glk characters into text.</summary>
public static class GlkText
{
    /// <summary>
    /// A code point as a string, or a question mark for one that is not
    /// a character at all.
    /// </summary>
    public static string ToString(uint character) =>
        character <= 0x10FFFF && character is < 0xD800 or > 0xDFFF
            ? char.ConvertFromUtf32((int)character)
            : "?";
}

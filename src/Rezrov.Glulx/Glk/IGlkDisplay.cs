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
    /// window, in the stream's current style.
    /// </summary>
    void Print(GlkWindow window, uint character, GlkStyle style);

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

    /// <param name="writer">Where text buffer output goes.</param>
    /// <param name="reader">
    /// Where lines come from, or none for a display that never gets
    /// input.
    /// </param>
    /// <param name="width">The display's width in characters.</param>
    /// <param name="height">The display's height in lines.</param>
    public TextWriterGlkDisplay(TextWriter writer, TextReader? reader = null, int width = 80, int height = 24)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
        _reader = reader ?? TextReader.Null;
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public void Print(GlkWindow window, uint character, GlkStyle style)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Type == WindowType.TextBuffer)
        {
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

    public void Arranged(GlkWindow? root)
    {
    }

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
            return line is null ? GlkInput.Ended : GlkInput.Line(lineRequests[0], line);
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

            var key = line.Length == 0 ? GlkKeyCode.Return : (uint)char.ConvertToUtf32(line, 0);
            return GlkInput.KeyPress(charRequests[0], key);
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

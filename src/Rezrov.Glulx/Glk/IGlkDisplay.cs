namespace Rezrov.Glulx.Glk;

/// <summary>
/// What a frontend provides for Glk to show its windows on: a size, and
/// a place for text buffer output to go as it is printed.
/// </summary>
/// <remarks>
/// The library keeps the window tree, the layout, and the contents of
/// text grid windows itself; a frontend reads those when it paints.
/// Text buffer output is different, since a buffer is a stream of text
/// that a frontend may show as it arrives, so it is handed over one
/// character at a time.
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
}

/// <summary>
/// A display that writes text buffer output to a TextWriter, which is
/// what the command line program can do: one stream of text, no
/// styles, and text grid windows kept but not shown.
/// </summary>
public sealed class TextWriterGlkDisplay : IGlkDisplay
{
    private readonly TextWriter _writer;

    public TextWriterGlkDisplay(TextWriter writer, int width = 80, int height = 24)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
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

using Rezrov.ZMachine.Text;

namespace Rezrov.ZMachine.Input;

/// <summary>
/// A keyboard made from a <see cref="TextReader"/>: the console, a
/// string, or a file of plain text, one command per line.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="Execution.TextWriterOutput"/>, and just
/// as plain. It has no clock, so it never runs a timer and reports that
/// [zm 10.5.3] timed input is unavailable, and it has no notion of a
/// cursor or an input line, so a game that wants a character at a time
/// gets the first character of the next line instead. Both are what a
/// terminal that reads a line at a time can honestly offer, and a screen
/// model will do better.
///
/// When the reader runs out, reading throws an
/// <see cref="EndOfStreamException"/> rather than inventing a command,
/// so that a caller driving a game from a script finds out the script
/// was too short.
/// </remarks>
public sealed class TextReaderInput : IInput
{
    private readonly TextReader _reader;
    private readonly UnicodeTranslationTable _extraCharacters;

    public TextReaderInput(TextReader reader, StoryHeader header, ZMemory memory)
    {
        ArgumentNullException.ThrowIfNull(reader);

        _reader = reader;
        _extraCharacters = UnicodeTranslationTable.ForStory(header, memory);
    }

    /// <summary>A reader has no clock.</summary>
    public bool SupportsTimedInput => false;

    /// <summary>
    /// [zm 10.2.3] There is no way to ask, so there is no file.
    /// </summary>
    public TextReader? OpenCommandFile() => null;

    public LineInput ReadLine(LineInputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var line = _reader.ReadLine() ?? throw new EndOfStreamException("The input has ended.");
        var text = new List<ushort>(request.Initial);

        foreach (var character in line)
        {
            if (text.Count >= request.MaxLength)
            {
                break;
            }

            // [zm 10.7] Only ZSCII characters can be read, so a character
            // with no code is as if it had not been typed. A newline
            // cannot be here, since the reader split on it.
            if (Zscii.FromUnicode(character, _extraCharacters) is { } zscii)
            {
                text.Add(zscii);
            }
        }

        return new LineInput(text, Zscii.Newline);
    }

    public ushort ReadKey(InputTimer? timer)
    {
        // The first character of the next line, or the return itself when
        // the line is empty, which is the best a line-at-a-time reader can
        // do and is what a console gives anyway.
        var line = _reader.ReadLine() ?? throw new EndOfStreamException("The input has ended.");

        foreach (var character in line)
        {
            if (Zscii.FromUnicode(character, _extraCharacters) is { } zscii)
            {
                return zscii;
            }
        }

        return Zscii.Newline;
    }
}

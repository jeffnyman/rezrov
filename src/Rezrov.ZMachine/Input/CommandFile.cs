using Rezrov.ZMachine.Text;

namespace Rezrov.ZMachine.Input;

/// <summary>
/// Input stream 1: a file of commands, read back in the format that
/// output stream 4 writes.
/// </summary>
/// <remarks>
/// [zm 10.2] and [zm 10.2.1] The second input stream is a file whose
/// format is the one output stream 4 records in. [zm 7.1.2.3] That
/// stream holds the player's whole commands and the keypresses read by
/// read_char, one per line, and the remarks on section 7 suggest a style
/// for the parts plain text cannot carry, which Frotz adopted and this
/// follows so that the two can exchange files:
///
/// <code>
/// take lamp              an ordinary command
/// turn it on.[154]       command, full stop, then keypad 9
/// look unde[0]           timed out input
/// [254][10][6]           mouse-click at (10,6)
/// </code>
///
/// A code in square brackets is a ZSCII value, which is how a terminating
/// function key, a timeout, an extra character, or a click gets written,
/// and a plain character is itself. A line's last code, if it is a
/// terminator, is the key that ended the command; otherwise the command
/// ended with a carriage return, which is never written.
///
/// [zm 10.2.2] A game cannot tell which stream it is reading from, and an
/// interpreter is free to play a whole game from a file, which is what
/// the interpreter's command-line option for a script does.
/// </remarks>
public sealed class CommandFile
{
    private readonly TextReader _reader;
    private readonly UnicodeTranslationTable _extraCharacters;

    public CommandFile(TextReader reader, UnicodeTranslationTable extraCharacters)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(extraCharacters);

        _reader = reader;
        _extraCharacters = extraCharacters;
    }

    /// <summary>
    /// Reads the next command, or returns null when the file has ended.
    /// </summary>
    /// <remarks>
    /// A timer in the request is ignored: the file records what the
    /// player ended up typing, and a timed-out read is in it as a line
    /// ending in [0], so the interrupt routine has already had its say.
    /// Frotz replays the same way.
    /// </remarks>
    public LineInput? ReadLine(LineInputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var codes = ReadCodes();
        if (codes is null)
        {
            return null;
        }

        var text = new List<ushort>(request.Initial);
        ushort terminator = Zscii.Newline;

        foreach (var code in codes)
        {
            if (code == Zscii.Null || request.Terminators.IsTerminator(code))
            {
                terminator = code;
                break;
            }

            if (text.Count < request.MaxLength)
            {
                text.Add(code);
            }
        }

        return new LineInput(text, terminator);
    }

    /// <summary>
    /// Reads the next keypress, or returns null when the file has ended.
    /// A line with nothing on it is the return key.
    /// </summary>
    public ushort? ReadKey()
    {
        var codes = ReadCodes();
        if (codes is null)
        {
            return null;
        }

        return codes.Count > 0 ? codes[0] : Zscii.Newline;
    }

    /// <summary>
    /// Closes the file. The interpreter does this when the file runs out
    /// or the game goes back to the keyboard.
    /// </summary>
    public void Close() => _reader.Dispose();

    // One line of the file as ZSCII codes, or null at the end.
    private List<ushort>? ReadCodes()
    {
        var line = _reader.ReadLine();
        if (line is null)
        {
            return null;
        }

        var codes = new List<ushort>();

        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '[' && TryReadBracketedCode(line, ref i, out var code))
            {
                // Frotz writes its own hot keys as codes from 1000 up,
                // which mean nothing to any other interpreter.
                if (code >= 1000)
                {
                    continue;
                }

                codes.Add(code);

                // [zm 10.3.2] A click is followed by its coordinates,
                // which are consumed here so that they are not taken for
                // keys. There is no mouse to give them to yet, since the
                // interpreter offers none, so they are dropped.
                if (code is Zscii.SingleClick or Zscii.DoubleClick or Zscii.MenuClick)
                {
                    for (var coordinate = 0; coordinate < 2 && i + 1 < line.Length && line[i + 1] == '['; coordinate++)
                    {
                        i++;
                        TryReadBracketedCode(line, ref i, out _);
                    }
                }
            }
            else if (Zscii.FromUnicode(line[i], _extraCharacters) is { } zscii)
            {
                codes.Add(zscii);
            }
        }

        return codes;
    }

    // Reads [digits] starting at the bracket at index, leaving index on
    // the closing bracket, or returns false and leaves index alone so
    // that a bare bracket is taken as the character it is.
    private static bool TryReadBracketedCode(string line, ref int index, out ushort code)
    {
        var value = 0;
        var end = index + 1;

        while (end < line.Length && char.IsAsciiDigit(line[end]))
        {
            value = (value * 10) + (line[end] - '0');
            end++;
        }

        if (end == index + 1 || end >= line.Length || line[end] != ']' || value > ushort.MaxValue)
        {
            code = 0;
            return false;
        }

        code = (ushort)value;
        index = end;
        return true;
    }
}

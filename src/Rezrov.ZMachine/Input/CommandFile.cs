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
/// read_char, one per line (though a line of several keys is read out
/// one at a time, see <see cref="ReadKey"/>), and the remarks on section
/// 7 suggest a style
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

    // The keys of the line being handed out one read_char at a time,
    // and how many of them have gone.
    private List<ushort>? _keys;
    private int _keyIndex;
    private string? _pending;

    /// <summary>
    /// [zm 10.3.2] The position of the last click read, from the two
    /// codes that follow a click character in the file.
    /// </summary>
    public MouseClick? LastClick { get; private set; }

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
    /// A timer in the request is not this class's business: a timed-out
    /// read that was recorded is in the file as a line ending in [0],
    /// and the interpreter decides, before asking for a line, whether
    /// the game's timer has its say first.
    /// </remarks>
    public LineInput? ReadLine(LineInputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Whatever a line of keys still held is left behind: a command
        // starts on a line of its own.
        _keys = null;
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
    /// A line with nothing on it is the return key, and a line with more
    /// than one code on it gives them to the reads that follow, one each,
    /// before the next line is looked at.
    /// </summary>
    /// <remarks>
    /// The format has one key per line, so a longer line is not something
    /// a recording writes. It is what a person writes for a game that
    /// takes its whole command through read_char, as Bureaucracy and
    /// Custard do: the command on one line, then a line with nothing on
    /// it for the return that ends it.
    /// </remarks>
    public ushort? ReadKey()
    {
        if (_keys is not null && _keyIndex < _keys.Count)
        {
            return _keys[_keyIndex++];
        }

        _keys = null;
        var codes = ReadCodes();
        if (codes is null)
        {
            return null;
        }

        if (codes.Count == 0)
        {
            return Zscii.Newline;
        }

        _keys = codes;
        _keyIndex = 1;
        return codes[0];
    }

    /// <summary>
    /// Whether the file has anything left: a line still to read, or
    /// keys of the last one still to hand out.
    /// </summary>
    public bool HasMore => HasPendingKeys || PeekLine() is not null;

    /// <summary>
    /// Whether keys of the last line read are still to be handed out.
    /// </summary>
    public bool HasPendingKeys => _keys is not null && _keyIndex < _keys.Count;

    /// <summary>
    /// Takes a line of <c>[0]</c>, the code a recording writes for a
    /// read that timed out, if that is what comes next. The interpreter
    /// asks this after a timer has ended a replayed read on its own, so
    /// that a recorded timeout is not then taken for a keypress.
    /// </summary>
    public bool TakeTimeout()
    {
        if (HasPendingKeys)
        {
            return false;
        }

        if (PeekLine() != "[0]")
        {
            return false;
        }

        _keys = null;
        NextLine();
        return true;
    }

    /// <summary>
    /// Closes the file. The interpreter does this when the file runs out
    /// or the game goes back to the keyboard.
    /// </summary>
    public void Close() => _reader.Dispose();

    // One line of lookahead over the reader, so that the end of the file
    // and a recorded timeout can be seen before a line is taken.
    private string? PeekLine()
    {
        _pending ??= _reader.ReadLine();
        return _pending;
    }

    private string? NextLine()
    {
        var line = PeekLine();
        _pending = null;
        return line;
    }

    /// <summary>
    /// [zm 7.1.2.3] Writes a finished command as one line of the format
    /// this class reads: plain characters as themselves, anything else
    /// as a bracketed code, and the terminating key last unless it was
    /// the return key, which is never written.
    /// </summary>
    public static string Format(IReadOnlyList<ushort> text, ushort terminator, MouseClick? click = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var line = new System.Text.StringBuilder(text.Count + 8);
        foreach (var code in text)
        {
            AppendCode(line, code);
        }

        if (terminator != Zscii.Newline)
        {
            AppendCode(line, terminator);
            AppendClick(line, terminator, click);
        }

        return line.ToString();
    }

    /// <summary>
    /// [zm 7.1.2.3] Writes a keypress read by read_char as one line:
    /// the key, or nothing at all for the return key, and for a click
    /// its position after it.
    /// </summary>
    public static string FormatKey(ushort key, MouseClick? click = null)
    {
        if (key == Zscii.Newline)
        {
            return "";
        }

        var line = new System.Text.StringBuilder(8);
        AppendCode(line, key);
        AppendClick(line, key, click);
        return line.ToString();
    }

    // [zm 7.1.2.3] A click is written with its x and y after it, as
    // "[254][10][6]" in the remarks on section 7.
    private static void AppendClick(System.Text.StringBuilder line, ushort code, MouseClick? click)
    {
        if (click is not null && code is Zscii.SingleClick or Zscii.DoubleClick or Zscii.MenuClick)
        {
            line.Append('[').Append(click.X).Append("][").Append(click.Y).Append(']');
        }
    }

    // A printable ASCII character other than the bracket is itself;
    // everything else, the bracket included, is [code].
    private static void AppendCode(System.Text.StringBuilder line, ushort code)
    {
        if (code is > Zscii.Space and <= Zscii.Tilde && code != '[')
        {
            line.Append((char)code);
        }
        else if (code == Zscii.Space)
        {
            line.Append(' ');
        }
        else
        {
            line.Append('[').Append(code).Append(']');
        }
    }

    // One line of the file as ZSCII codes, or null at the end.
    private List<ushort>? ReadCodes()
    {
        var line = NextLine();
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

                // [zm 10.3.2] A click is followed by its coordinates, x
                // then y, which are kept for the interpreter rather than
                // taken for keys.
                if (code is Zscii.SingleClick or Zscii.DoubleClick or Zscii.MenuClick)
                {
                    var position = new int[2];
                    for (var coordinate = 0; coordinate < 2 && i + 1 < line.Length && line[i + 1] == '['; coordinate++)
                    {
                        i++;
                        TryReadBracketedCode(line, ref i, out var value);
                        position[coordinate] = value;
                    }

                    LastClick = new MouseClick(position[0], position[1], 1);
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

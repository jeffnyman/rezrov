using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Text;

namespace Rezrov.ZMachine.Streams;

/// <summary>
/// The four output streams of section 7, and the routing of every
/// printed character among them.
/// </summary>
/// <remarks>
/// [zm 7.1] Text is output through a selection of streams, possibly
/// several at once: [zm 7.1.1] 1 is the screen and 2 the transcript,
/// and [zm 7.1.2] from Version 3 there are also 3, a table in memory,
/// and 4, a file of the player's commands. This sits between the
/// interpreter and the <see cref="ScreenModel"/>, which is stream 1,
/// so that the interpreter prints to one place and the streams decide
/// where it goes. Frotz's stream.c is the same arrangement.
/// </remarks>
public sealed class OutputStreams : IOutput
{
    /// <summary>
    /// [zm 7.1.2.1.1] Stream 3 may be selected while already selected,
    /// to a depth of 16; a seventeenth time is an error.
    /// </summary>
    public const int MaxMemoryDepth = 16;

    private readonly IScreenModel _screen;
    private readonly GameState _state;
    private readonly StoryHeader _header;
    private readonly UnicodeTranslationTable _extraCharacters;
    private readonly IFileChooser _files;
    private readonly Stack<MemoryStream> _memory = new();
    private TextWriter? _transcript;
    private bool _transcriptDeclined;
    private TextWriter? _record;

    public OutputStreams(IScreenModel screen, GameState state, StoryHeader header, UnicodeTranslationTable extraCharacters, IFileChooser files)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(extraCharacters);
        ArgumentNullException.ThrowIfNull(files);

        _screen = screen;
        _state = state;
        _header = header;
        _extraCharacters = extraCharacters;
        _files = files;
    }

    /// <summary>
    /// [zm 7.1.1] Whether stream 1, the screen, is selected. It starts
    /// selected, and [zm 7.3] in Versions 1 and 2 always is.
    /// </summary>
    public bool ScreenSelected { get; private set; } = true;

    /// <summary>
    /// [zm 7.1.1] Whether stream 2, the transcript, is selected, which
    /// [zm 7.4] bit 0 of Flags 2 always agrees with.
    /// </summary>
    public bool TranscriptSelected { get; private set; }

    /// <summary>
    /// [zm 7.1.2.1.1] How many tables stream 3 is writing into, nested.
    /// </summary>
    public int MemoryDepth => _memory.Count;

    /// <summary>
    /// [zm 7.1.2.3] Whether stream 4, the record of commands, is
    /// selected.
    /// </summary>
    public bool RecordSelected => _record is not null;

    /// <summary>
    /// [zm op:output_stream] Selects a stream (positive) or deselects
    /// it (negative). Stream 3 takes the table to write into.
    /// </summary>
    public StreamSelection Select(int stream, ushort table)
    {
        // [zm op:output_stream] Whatever is buffered for the screen is
        // shown before the streams change, as Frotz also flushes.
        _screen.Flush();

        switch (stream)
        {
            case 0:
                // [zm op:output_stream] If stream is 0, nothing happens.
                return StreamSelection.Selected;

            case 1:
                ScreenSelected = true;
                return StreamSelection.Selected;
            case -1:
                ScreenSelected = false;
                return StreamSelection.Selected;

            case 2:
                return SelectTranscript() ? StreamSelection.Selected : StreamSelection.Unavailable;
            case -2:
                DeselectTranscript();
                return StreamSelection.Selected;

            case 3:
                if (_memory.Count >= MaxMemoryDepth)
                {
                    return StreamSelection.TooDeep;
                }

                // [zm 7.1.2.1] The table's contents, even its size word,
                // are ignored on selection and unspecified while it is
                // selected.
                _memory.Push(new MemoryStream(table));
                return StreamSelection.Selected;
            case -3:
                if (_memory.Count > 0)
                {
                    CloseMemory();
                }

                return StreamSelection.Selected;

            case 4:
                if (_record is null)
                {
                    _record = _files.OpenCommandRecord();
                    if (_record is null)
                    {
                        return StreamSelection.Unavailable;
                    }
                }

                return StreamSelection.Selected;
            case -4:
                _record?.Flush();
                _record = null;
                return StreamSelection.Selected;

            default:
                return StreamSelection.Unknown;
        }
    }

    /// <summary>
    /// [zm 7.4] and [zm 11.1.2.1] Brings stream 2 into line with bit 0
    /// of Flags 2, which the game may have set or cleared directly, and
    /// the bit into line with the stream when a file cannot be had.
    /// </summary>
    /// <returns>
    /// False if the game asked for a transcript and none is available.
    /// </returns>
    public bool SyncWithHeader()
    {
        var wanted = _header.Flags2.HasFlag(Flags2.Transcripting);
        if (wanted == TranscriptSelected)
        {
            return true;
        }

        if (wanted)
        {
            return SelectTranscript();
        }

        DeselectTranscript();
        return true;
    }

    /// <summary>
    /// Prints one ZSCII code to every selected stream.
    /// </summary>
    /// <remarks>
    /// [zm 7.1.2.2] While stream 3 is selected no text goes anywhere
    /// else, though the other streams stay selected. Otherwise the code
    /// goes to the screen and, for text in the lower window, to the
    /// transcript. The upper window is left out of the transcript, as
    /// Frotz leaves it out, since a status line rewritten every turn is
    /// not what a transcript is for. Stream 4 never sees printed text.
    /// </remarks>
    public void Print(ushort zscii)
    {
        if (_memory.Count > 0)
        {
            // [zm 7.1.2.2.1] Newlines are written to stream 3 as ZSCII
            // 13, which is what a newline is in ZSCII anyway; the codes
            // arrive here unchanged.
            _memory.Peek().Write(_state, (byte)zscii, _screen.FontWidth);
            return;
        }

        if (ScreenSelected)
        {
            _screen.Print(zscii);
        }

        if (_transcript is not null && TranscriptSelected && _screen.EchoesToTranscript)
        {
            WriteToTranscript(zscii);
        }
    }

    /// <summary>
    /// [zm op:print_unicode] Prints a Unicode character to every
    /// selected stream.
    /// </summary>
    /// <remarks>
    /// [zm 7.5.3] To stream 3 it is converted to ZSCII if possible and
    /// is otherwise a question mark. [zm 7.5.2] The transcript may use
    /// any representation, and here it is the character itself.
    /// </remarks>
    public void PrintUnicode(char character)
    {
        if (_memory.Count > 0)
        {
            var zscii = Zscii.FromUnicode(character, _extraCharacters);
            _memory.Peek().Write(_state, (byte)(zscii is { } code && Zscii.IsDefinedForInputAndOutput(code) ? code : '?'), _screen.FontWidth);
            return;
        }

        if (ScreenSelected)
        {
            _screen.PrintUnicode(character);
        }

        if (_transcript is not null && TranscriptSelected && _screen.EchoesToTranscript)
        {
            _transcript.Write(_screen.CanPrint(character) ? character : '?');
        }
    }

    /// <summary>
    /// [zm 7.1.1.1] In Versions 1 to 5 the player's input to read is
    /// echoed to the transcript, so that what was typed appears in it.
    /// The screen's echo is the keyboard's own.
    /// </summary>
    public void EchoInput(IReadOnlyList<ushort> text, ushort terminator)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (_transcript is null || !TranscriptSelected || _header.Version == ZMachineVersion.V6)
        {
            return;
        }

        foreach (var code in text)
        {
            WriteToTranscript(code);
        }

        if (terminator == Zscii.Newline)
        {
            _transcript.Write((char)0x0A);
        }
    }

    /// <summary>
    /// [zm 7.1.2.3] Writes a finished command to stream 4, in the
    /// format <see cref="CommandFile"/> reads back.
    /// </summary>
    public void RecordCommand(IReadOnlyList<ushort> text, ushort terminator)
    {
        if (_record is not null)
        {
            _record.Write(CommandFile.Format(text, terminator));
            _record.Write((char)0x0A);
        }
    }

    /// <summary>
    /// [zm 7.1.2.3] Writes a keypress read by read_char to stream 4.
    /// </summary>
    public void RecordKey(ushort key)
    {
        if (_record is not null)
        {
            _record.Write(CommandFile.FormatKey(key));
            _record.Write((char)0x0A);
        }
    }

    /// <summary>
    /// [zm 6.1.3] A restart empties the stream 3 nesting with the rest
    /// of the game's state, while [zm 11.1.2.1] the transcript survives.
    /// </summary>
    public void Reset()
    {
        _memory.Clear();
        ScreenSelected = true;
    }

    /// <summary>
    /// Writes out anything held back, as before the game quits.
    /// </summary>
    public void Flush()
    {
        _screen.Flush();
        _transcript?.Flush();
        _record?.Flush();
    }

    private bool SelectTranscript()
    {
        // [zm 7.1.1.2] A Mind Forever Voyaging turns the transcript off
        // and on again several times in quick succession, so the frontend
        // is asked for a file once per session and the answer is kept,
        // whether it was a file or a refusal.
        if (_transcript is null && !_transcriptDeclined)
        {
            _transcript = _files.OpenTranscript();
            _transcriptDeclined = _transcript is null;
        }

        TranscriptSelected = _transcript is not null;
        SetTranscriptBit(TranscriptSelected);
        return TranscriptSelected;
    }

    private void DeselectTranscript()
    {
        _transcript?.Flush();
        TranscriptSelected = false;
        SetTranscriptBit(false);
    }

    // [zm 7.4] The bit must hold the current status of stream 2 whichever
    // way it was changed.
    private void SetTranscriptBit(bool on) =>
        _header.Flags2 = on ? _header.Flags2 | Flags2.Transcripting : _header.Flags2 & ~Flags2.Transcripting;

    private void WriteToTranscript(ushort zscii)
    {
        if (zscii == Zscii.Newline)
        {
            _transcript!.Write((char)0x0A);
        }
        else if (Zscii.ToUnicode(zscii, _header.Version, _extraCharacters) is { } character)
        {
            _transcript!.Write(character);
        }
    }

    // [zm 7.1.2.1] When the stream is deselected, the first word of the
    // table holds the number of characters printed, and in Version 6
    // the header word at $30 holds the width of the text in units,
    // which is how a game measures a string before placing it.
    private void CloseMemory()
    {
        var stream = _memory.Pop();
        _state.WriteWord(stream.Table, (ushort)stream.Count);

        if (_header.Version == ZMachineVersion.V6)
        {
            _header.OutputStream3Width = (ushort)Math.Min(stream.Width, ushort.MaxValue);
        }
    }

    /// <summary>
    /// [zm 7.1.2.1] One table stream 3 is writing into: the characters
    /// go from byte 2 onward, and the interpreter does no overflow
    /// checking, since making the table large enough is the game's job.
    /// </summary>
    private sealed class MemoryStream(int table)
    {
        public int Table { get; } = table;

        public int Count { get; private set; }

        /// <summary>The width of the text in units, for Version 6.</summary>
        public int Width { get; private set; }

        public void Write(GameState state, byte zscii, int characterWidth)
        {
            state.WriteByte(Table + 2 + Count, zscii);
            Count++;
            if (zscii != Zscii.Newline)
            {
                Width += characterWidth;
            }
        }
    }
}

/// <summary>The outcome of selecting an output stream.</summary>
public enum StreamSelection
{
    /// <summary>The stream is now as asked.</summary>
    Selected,

    /// <summary>[zm 7.1] There is no such stream.</summary>
    Unknown,

    /// <summary>
    /// [zm 7.1.2.1.1] Stream 3 was already nested 16 deep, and the
    /// interpreter should halt.
    /// </summary>
    TooDeep,

    /// <summary>
    /// [zm 7.6.5.2] The frontend has no file for the stream, and the
    /// player should be warned.
    /// </summary>
    Unavailable,
}

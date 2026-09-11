using System.Diagnostics.CodeAnalysis;

namespace Rezrov.Glulx.Glk;

/// <summary>
/// A Glk stream: something characters are printed to or read from.
/// </summary>
/// <remarks>
/// [glk #stream] Every window has a stream, and streams can also lead
/// to memory and to files. Each stream counts the characters written to
/// it and read from it, one per call whatever became of the character,
/// and carries its own current style.
/// </remarks>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Stream is the name Glk gives the object, and it is not a System.IO stream.")]
public abstract class GlkStream : GlkObject
{
    protected GlkStream(uint rock, bool readable, bool writable)
        : base(rock)
    {
        Readable = readable;
        Writable = writable;
    }

    public bool Readable { get; }

    public bool Writable { get; }

    /// <summary>[glk #stream] One per character read.</summary>
    public uint ReadCount { get; private set; }

    /// <summary>[glk #stream] One per character written.</summary>
    public uint WriteCount { get; private set; }

    /// <summary>[glk #stream_style] The style new text gets.</summary>
    public GlkStyle Style { get; private set; }

    /// <summary>
    /// [glk #stream_positions] The read and write mark, in characters.
    /// A window stream has none and answers zero.
    /// </summary>
    public virtual uint Position => 0;

    /// <summary>Prints one character.</summary>
    public void PutChar(uint character)
    {
        WriteCount++;
        Write(character);
    }

    /// <summary>
    /// Reads one character, or -1 at the end of the stream.
    /// </summary>
    public int GetChar()
    {
        var character = Read();
        if (character >= 0)
        {
            ReadCount++;
        }

        return character;
    }

    /// <summary>[glk op:set_style_stream] Changes the style.</summary>
    public virtual void SetStyle(GlkStyle style) => Style = style;

    /// <summary>[glk op:stream_set_position] Moves the mark.</summary>
    public virtual void SetPosition(int position, SeekMode mode)
    {
    }

    /// <summary>
    /// [glk #stream_close] Lets go of whatever the stream holds, once
    /// the library has forgotten it; a file is flushed and closed.
    /// </summary>
    internal virtual void Close()
    {
    }

    protected abstract void Write(uint character);

    protected virtual int Read() => -1;
}

/// <summary>
/// [glk #window_streams] The stream of a window, which prints to the
/// window and [glk #echo_streams] copies to its echo stream.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Stream is the name Glk gives the object, and it is not a System.IO stream.")]
public sealed class WindowStream : GlkStream
{
    internal WindowStream(GlkWindow window)
        : base(0, false, true)
    {
        Window = window;
    }

    public GlkWindow Window { get; }

    public override void SetStyle(GlkStyle style)
    {
        base.SetStyle(style);

        // [glk #echo_streams] Style commands are replicated too.
        Window.EchoStream?.SetStyle(style);
    }

    protected override void Write(uint character)
    {
        Window.Put(character, Style);
        Window.EchoStream?.PutChar(character);
    }
}

/// <summary>
/// [glk #memory-streams] A stream over a buffer in the game's memory,
/// of bytes or of 32-bit words.
/// </summary>
/// <remarks>
/// Characters are written straight into memory as they arrive, which
/// the specification allows, and everything past the end of the buffer
/// is thrown away while still being counted. A buffer at address zero
/// or of length zero takes nothing and gives back the end of the
/// stream at once.
/// </remarks>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Stream is the name Glk gives the object, and it is not a System.IO stream.")]
public sealed class GlkMemoryStream : GlkStream
{
    private readonly GlulxMemory _memory;
    private uint _position;

    internal GlkMemoryStream(GlulxMemory memory, uint address, uint length, bool unicode, FileMode mode, uint rock)
        : base(rock, mode is FileMode.Read or FileMode.ReadWrite, mode is FileMode.Write or FileMode.ReadWrite or FileMode.WriteAppend)
    {
        _memory = memory;
        Address = address;
        Length = address == 0 ? 0 : length;
        IsUnicode = unicode;
    }

    /// <summary>Where the buffer is in the game's memory.</summary>
    public uint Address { get; }

    /// <summary>The buffer's length in characters.</summary>
    public uint Length { get; }

    /// <summary>
    /// Whether a character is a four-byte word or a byte.
    /// </summary>
    public bool IsUnicode { get; }

    public override uint Position => _position;

    public override void SetPosition(int position, SeekMode mode)
    {
        // [glk #stream_positions] Relative to the start, the mark, or
        // the end, and never outside the buffer.
        long target = mode switch
        {
            SeekMode.Current => _position + (long)position,
            SeekMode.End => Length + (long)position,
            _ => position,
        };

        _position = (uint)Math.Clamp(target, 0, Length);
    }

    protected override void Write(uint character)
    {
        if (_position >= Length)
        {
            return;
        }

        if (IsUnicode)
        {
            _memory.WriteWord(Address + (4 * _position), character);
        }
        else
        {
            // [glk #memory-streams] A character beyond 255 in a byte
            // buffer is stored as a question mark.
            _memory.WriteByte(Address + _position, character > 0xFF ? (byte)'?' : (byte)character);
        }

        _position++;
    }

    protected override int Read()
    {
        if (_position >= Length)
        {
            return -1;
        }

        var character = IsUnicode
            ? _memory.ReadWord(Address + (4 * _position))
            : _memory.ReadByte(Address + _position);
        _position++;
        return (int)character;
    }
}

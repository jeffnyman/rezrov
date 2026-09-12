using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Rezrov.Glulx.Glk;

/// <summary>
/// A stream over stored bytes, in a file or a resource, read and written
/// in one of the four encodings the specification lists.
/// </summary>
/// <remarks>
/// [glk #file_streams] A byte stream stores Latin-1, a character beyond
/// 255 becoming a question mark, whether text or binary; a Unicode
/// stream stores four-byte big-endian words in binary form and UTF-8 in
/// text form. Text mode may convert newlines to the platform's, but the
/// newline the game writes, 0x0A, is the one every text editor now
/// reads, so it is kept, and a file reads back exactly as it was
/// written. [glk #resource_streams] A resource is read the same way,
/// and never remaps newlines either.
///
/// [glk #stream_positions] Positions count characters: bytes in a byte
/// stream, words in a binary Unicode stream, and bytes of UTF-8 in a
/// text Unicode stream, where the specification allows the count to
/// be in the native encoding.
/// </remarks>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Stream is the name Glk gives the object, and it is not a System.IO stream.")]
public abstract class GlkEncodedStream : GlkStream
{
    private readonly Stream _bytes;
    private readonly byte[] _buffer = new byte[4];

    private protected GlkEncodedStream(Stream bytes, bool unicode, bool text, bool readable, bool writable, uint rock)
        : base(rock, readable, writable)
    {
        _bytes = bytes;
        IsUnicode = unicode;
        IsText = text;
    }

    /// <summary>
    /// Whether a character is a Unicode value or a Latin-1 byte.
    /// </summary>
    public bool IsUnicode { get; }

    /// <summary>[glk #fileref] Whether the contents are text.</summary>
    public bool IsText { get; }

    public override uint Position => (uint)(_bytes.Position / Unit);

    // How many bytes of the store one character position is.
    private int Unit => IsUnicode && !IsText ? 4 : 1;

    public override void SetPosition(int position, SeekMode mode)
    {
        // [glk #stream_positions] Relative to the start, the mark, or
        // the end, and never outside the store.
        var offset = (long)position * Unit;
        var target = mode switch
        {
            SeekMode.Current => _bytes.Position + offset,
            SeekMode.End => _bytes.Length + offset,
            _ => offset,
        };

        _bytes.Position = Math.Clamp(target, 0, _bytes.Length);
    }

    internal override void Close() => _bytes.Dispose();

    protected override void Write(uint character)
    {
        if (!IsUnicode)
        {
            _bytes.WriteByte(character > 0xFF ? (byte)'?' : (byte)character);
        }
        else if (IsText)
        {
            // [glk #file_streams] UTF-8, without a byte order mark. A
            // value that is not a character at all is a question mark.
            if (!Rune.TryCreate(character, out var rune))
            {
                rune = new Rune('?');
            }

            var length = rune.EncodeToUtf8(_buffer);
            _bytes.Write(_buffer, 0, length);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(_buffer, character);
            _bytes.Write(_buffer, 0, 4);
        }
    }

    protected override int Read()
    {
        if (!IsUnicode)
        {
            return _bytes.ReadByte();
        }

        if (!IsText)
        {
            // [glk #file_streams] A whole word or the end of the store.
            return _bytes.ReadAtLeast(_buffer, 4, false) == 4 ? (int)BinaryPrimitives.ReadUInt32BigEndian(_buffer) : -1;
        }

        // [glk #file_streams] UTF-8: the first byte says how many follow,
        // and a sequence that is not UTF-8 ends the stream, as the
        // reference library has it.
        var first = _bytes.ReadByte();
        if (first < 0)
        {
            return -1;
        }

        var length = first switch
        {
            < 0x80 => 1,
            >= 0xC0 and < 0xE0 => 2,
            >= 0xE0 and < 0xF0 => 3,
            >= 0xF0 and < 0xF8 => 4,
            _ => 0,
        };

        if (length == 0)
        {
            return -1;
        }

        _buffer[0] = (byte)first;
        if (_bytes.ReadAtLeast(_buffer.AsSpan(1, length - 1), length - 1, false) != length - 1)
        {
            return -1;
        }

        return Rune.DecodeFromUtf8(_buffer.AsSpan(0, length), out var rune, out _) == OperationStatus.Done ? rune.Value : -1;
    }
}

/// <summary>
/// [glk #file_streams] A stream over a file in permanent storage, of
/// bytes or of Unicode characters, in text or binary form.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Stream is the name Glk gives the object, and it is not a System.IO stream.")]
public sealed class GlkFileStream : GlkEncodedStream
{
    internal GlkFileStream(Stream file, string name, bool unicode, bool text, FileMode mode, uint rock)
        : base(file, unicode, text, mode is FileMode.Read or FileMode.ReadWrite, mode is not FileMode.Read, rock)
    {
        Name = name;
    }

    /// <summary>
    /// The name of the file, as the file system knows it.
    /// </summary>
    public string Name { get; }
}

/// <summary>
/// [glk #resource_streams] A stream that reads one of the resource
/// file's data chunks, as bytes or as Unicode characters.
/// </summary>
/// <remarks>
/// [glk #resource_streams] A TEXT chunk is text, a BINA chunk binary,
/// and a FORM chunk binary too, read from its own FORM header on, so
/// that an AIFF sound or a Quetzal saved game inside the resource file
/// comes out as a whole file; TEXT and BINA give their contents alone.
/// </remarks>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Stream is the name Glk gives the object, and it is not a System.IO stream.")]
public sealed class GlkResourceStream : GlkEncodedStream
{
    internal GlkResourceStream(ReadOnlyMemory<byte> data, uint number, bool unicode, bool text, uint rock)
        : base(new MemoryStream(data.ToArray(), writable: false), unicode, text, true, false, rock)
    {
        Number = number;
    }

    /// <summary>
    /// [glk op:stream_open_resource] The number of the data resource.
    /// </summary>
    public uint Number { get; }
}

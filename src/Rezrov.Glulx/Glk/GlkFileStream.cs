using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Rezrov.Glulx.Glk;

/// <summary>
/// [glk #file_streams] A stream over a file in permanent storage, of
/// bytes or of Unicode characters, in text or binary form.
/// </summary>
/// <remarks>
/// [glk #file_streams] The four encodings the specification lists: a
/// byte stream stores Latin-1, a character beyond 255 becoming a
/// question mark, whether text or binary; a Unicode stream stores
/// four-byte big-endian words in binary form and UTF-8 in text form.
/// Text mode may convert newlines to the platform's, but the newline
/// the game writes, 0x0A, is the one every text editor now reads, so
/// it is kept, and a file reads back exactly as it was written.
///
/// [glk #stream_positions] Positions count characters: bytes in a byte
/// stream, words in a binary Unicode stream, and bytes of UTF-8 in a
/// text Unicode stream, where the specification allows the count to
/// be in the native encoding.
/// </remarks>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Stream is the name Glk gives the object, and it is not a System.IO stream.")]
public sealed class GlkFileStream : GlkStream
{
    private readonly Stream _file;
    private readonly byte[] _bytes = new byte[4];

    internal GlkFileStream(Stream file, string name, bool unicode, bool text, FileMode mode, uint rock)
        : base(rock, mode is FileMode.Read or FileMode.ReadWrite, mode is not FileMode.Read)
    {
        _file = file;
        Name = name;
        IsUnicode = unicode;
        IsText = text;
    }

    /// <summary>
    /// The name of the file, as the file system knows it.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Whether a character is a Unicode value or a Latin-1 byte.
    /// </summary>
    public bool IsUnicode { get; }

    /// <summary>[glk #fileref] Whether the file is text.</summary>
    public bool IsText { get; }

    public override uint Position => (uint)(_file.Position / Unit);

    // How many bytes of the file one character position is.
    private int Unit => IsUnicode && !IsText ? 4 : 1;

    public override void SetPosition(int position, SeekMode mode)
    {
        // [glk #stream_positions] Relative to the start, the mark, or
        // the end, and never outside the file.
        var offset = (long)position * Unit;
        var target = mode switch
        {
            SeekMode.Current => _file.Position + offset,
            SeekMode.End => _file.Length + offset,
            _ => offset,
        };

        _file.Position = Math.Clamp(target, 0, _file.Length);
    }

    protected override void Write(uint character)
    {
        if (!IsUnicode)
        {
            _file.WriteByte(character > 0xFF ? (byte)'?' : (byte)character);
        }
        else if (IsText)
        {
            // [glk #file_streams] UTF-8, without a byte order mark. A
            // value that is not a character at all is a question mark.
            if (!Rune.TryCreate(character, out var rune))
            {
                rune = new Rune('?');
            }

            var length = rune.EncodeToUtf8(_bytes);
            _file.Write(_bytes, 0, length);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(_bytes, character);
            _file.Write(_bytes, 0, 4);
        }
    }

    protected override int Read()
    {
        if (!IsUnicode)
        {
            return _file.ReadByte();
        }

        if (!IsText)
        {
            // [glk #file_streams] A whole word or the end of the file.
            return _file.ReadAtLeast(_bytes, 4, false) == 4 ? (int)BinaryPrimitives.ReadUInt32BigEndian(_bytes) : -1;
        }

        // [glk #file_streams] UTF-8: the first byte says how many follow,
        // and a sequence that is not UTF-8 ends the stream, as the
        // reference library has it.
        var first = _file.ReadByte();
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

        _bytes[0] = (byte)first;
        if (_file.ReadAtLeast(_bytes.AsSpan(1, length - 1), length - 1, false) != length - 1)
        {
            return -1;
        }

        return Rune.DecodeFromUtf8(_bytes.AsSpan(0, length), out var rune, out _) == OperationStatus.Done ? rune.Value : -1;
    }

    internal override void Close() => _file.Dispose();
}

using System.Buffers.Binary;

namespace Rezrov.Core.Audio;

/// <summary>
/// Reads an AIFF file into samples a mixer can play.
/// </summary>
/// <remarks>
/// [blorb 3] The sampled sounds of a resource file are AIFF, and
/// [blorb 16] a resource of that format is the whole form, so what
/// arrives here begins with FORM. An AIFF holds its format in a COMM
/// chunk and its samples in an SSND chunk, in any order and among any
/// number of chunks this does not care about, such as the markers and
/// instrument settings a tracker wrote.
///
/// Uncompressed samples are signed and big-endian, at 8, 16, 24, or 32
/// bits. The later AIFC form names a compression, of which the two
/// that are not compression at all are understood: NONE, which is the
/// same big-endian samples, and sowt, which is the same samples the
/// other way round. Anything else, a compressed sound or a form with
/// no samples in it, is refused, which a frontend reports as a sound
/// it cannot play.
/// </remarks>
public static class AiffReader
{
    /// <summary>
    /// Decodes an AIFF, or returns null if the bytes are not one, or
    /// are one this cannot decode.
    /// </summary>
    public static AiffSound? Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 12 || !file[..4].SequenceEqual("FORM"u8))
        {
            return null;
        }

        var kind = file.Slice(8, 4);
        if (!kind.SequenceEqual("AIFF"u8) && !kind.SequenceEqual("AIFC"u8))
        {
            return null;
        }

        // The form's own length may claim more than arrived, so the
        // chunk walk is bounded by what is actually here.
        var length = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(file[4..]) + 8L, file.Length);

        var channels = 0;
        var frames = 0;
        var bits = 0;
        var rate = 0.0;
        var littleEndian = false;
        ReadOnlySpan<byte> samples = default;
        var haveFormat = false;

        for (var at = 12; at + 8 <= length;)
        {
            var id = file.Slice(at, 4);
            var size = BinaryPrimitives.ReadUInt32BigEndian(file[(at + 4)..]);
            var body = file[(at + 8)..];
            if (size > (uint)body.Length)
            {
                // A chunk that runs off the end is as much of one as
                // there is, which is how a truncated file still plays.
                size = (uint)body.Length;
            }

            body = body[..(int)size];

            if (id.SequenceEqual("COMM"u8))
            {
                if (body.Length < 18)
                {
                    return null;
                }

                channels = BinaryPrimitives.ReadUInt16BigEndian(body);
                frames = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(body[2..]), int.MaxValue);
                bits = BinaryPrimitives.ReadUInt16BigEndian(body[6..]);
                rate = ReadExtended(body[8..18]);
                haveFormat = true;

                if (kind.SequenceEqual("AIFC"u8) && body.Length >= 22)
                {
                    var compression = body.Slice(18, 4);
                    if (compression.SequenceEqual("sowt"u8))
                    {
                        littleEndian = true;
                    }
                    else if (!compression.SequenceEqual("NONE"u8))
                    {
                        // A sound that would need decompressing.
                        return null;
                    }
                }
            }
            else if (id.SequenceEqual("SSND"u8))
            {
                if (body.Length < 8)
                {
                    return null;
                }

                // The offset is dead space before the samples, which a
                // block-aligned file uses and most files leave at zero.
                var offset = BinaryPrimitives.ReadUInt32BigEndian(body);
                samples = offset < (uint)(body.Length - 8) ? body[(int)(8 + offset)..] : default;
            }

            // Chunks are padded to an even length, and the pad byte is
            // not counted in the size.
            at += 8 + (int)size + (int)(size & 1);
        }

        if (!haveFormat || channels is < 1 or > 2 || frames <= 0 || rate <= 0 || samples.IsEmpty)
        {
            return null;
        }

        var width = bits switch
        {
            <= 8 => 1,
            <= 16 => 2,
            <= 24 => 3,
            <= 32 => 4,
            _ => 0,
        };

        if (width == 0)
        {
            return null;
        }

        // A file whose samples run out early is played as far as it goes.
        var available = samples.Length / (width * channels);
        frames = Math.Min(frames, available);
        if (frames <= 0)
        {
            return null;
        }

        var decoded = new float[frames * channels];
        for (var i = 0; i < decoded.Length; i++)
        {
            decoded[i] = Sample(samples.Slice(i * width, width), littleEndian);
        }

        return new AiffSound(decoded, channels, rate);
    }

    // One signed sample, scaled to between minus one and one.
    private static float Sample(ReadOnlySpan<byte> bytes, bool littleEndian)
    {
        switch (bytes.Length)
        {
            case 1:
                return (sbyte)bytes[0] / 128f;
            case 2:
            {
                var value = littleEndian
                    ? BinaryPrimitives.ReadInt16LittleEndian(bytes)
                    : BinaryPrimitives.ReadInt16BigEndian(bytes);
                return value / 32768f;
            }

            case 3:
            {
                var high = littleEndian ? bytes[2] : bytes[0];
                var middle = bytes[1];
                var low = littleEndian ? bytes[0] : bytes[2];
                var value = ((sbyte)high << 16) | (middle << 8) | low;
                return value / 8388608f;
            }

            default:
            {
                var value = littleEndian
                    ? BinaryPrimitives.ReadInt32LittleEndian(bytes)
                    : BinaryPrimitives.ReadInt32BigEndian(bytes);
                return value / 2147483648f;
            }
        }
    }

    // The sample rate is an 80-bit extended floating point number, the
    // one format of the old Apple machines AIFF came from: a sign, a
    // fifteen-bit exponent biased by 16383, and a sixty-four bit
    // significand whose top bit is written out rather than implied.
    private static double ReadExtended(ReadOnlySpan<byte> bytes)
    {
        var exponent = BinaryPrimitives.ReadUInt16BigEndian(bytes);
        var significand = BinaryPrimitives.ReadUInt64BigEndian(bytes[2..]);
        var sign = (exponent & 0x8000) != 0 ? -1.0 : 1.0;
        exponent &= 0x7FFF;

        if (exponent == 0 && significand == 0)
        {
            return 0;
        }

        if (exponent == 0x7FFF)
        {
            // An infinity or a number that is not one: no rate at all.
            return 0;
        }

        return sign * significand * Math.Pow(2, exponent - 16383 - 63);
    }
}

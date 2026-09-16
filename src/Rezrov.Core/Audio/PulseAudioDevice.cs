using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Rezrov.Core.Audio;

/// <summary>
/// Audio output on Linux, through the simple interface of PulseAudio.
/// </summary>
/// <remarks>
/// PulseAudio's simple interface is four functions: open a stream for a
/// format, write samples to it, and close it. Writing blocks until the
/// server has room, which is the pacing this needs and saves it from
/// keeping its own clock. The library it calls, libpulse-simple, is on
/// every desktop that runs PulseAudio and on the PipeWire systems that
/// replaced it, since those ship its compatibility layer; where it is
/// missing there is no sound, which is what the game is told.
///
/// The target length asks the server for a short buffer, so that a
/// sound effect is heard when the game plays it rather than a moment
/// later. Everything else about the stream is left to the server.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class PulseAudioDevice : IAudioDevice
{
    private const int BufferFrames = 1024;

    // The sample format of a pa_sample_spec: signed 16-bit, little
    // endian, which is what the mixer produces.
    private const int SampleFormatS16Le = 3;

    // pa_stream_direction_t: playback.
    private const int DirectionPlayback = 1;

    // Every field of a pa_buffer_attr this does not set is left to the
    // server, which is what all ones means.
    private const uint Default = uint.MaxValue;

    private readonly short[] _buffer;
    private readonly byte[] _bytes;
    private readonly nint _stream;

    private Thread? _worker;
    private volatile bool _stopping;
    private bool _disposed;

    private PulseAudioDevice(nint stream, int sampleRate, int channels)
    {
        _stream = stream;
        SampleRate = sampleRate;
        Channels = channels;
        _buffer = new short[BufferFrames * channels];
        _bytes = new byte[_buffer.Length * sizeof(short)];
    }

    public int SampleRate { get; }

    public int Channels { get; }

    /// <summary>
    /// Opens the machine's audio output, or returns null where there is
    /// none to open: another operating system, no PulseAudio library,
    /// or no sound server to talk to.
    /// </summary>
    public static PulseAudioDevice? TryOpen(int sampleRate = 44100, int channels = 2)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var format = new SampleSpec
        {
            Format = SampleFormatS16Le,
            Rate = (uint)sampleRate,
            Channels = (byte)channels,
        };

        // A target length of a few buffers: long enough not to break up,
        // short enough that a sound is heard when it is played.
        var buffering = new BufferAttributes
        {
            MaximumLength = Default,
            TargetLength = (uint)(BufferFrames * channels * sizeof(short) * 4),
            Prebuffer = Default,
            MinimumRequest = Default,
            FragmentSize = Default,
        };

        nint stream;
        try
        {
            stream = SimpleNew(0, "Rezrov", DirectionPlayback, 0, "Game sound", ref format, 0, ref buffering, out _);
        }
        catch (DllNotFoundException)
        {
            // No PulseAudio on this machine, which is not an error: the
            // game is told there is no sound.
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }

        return stream == 0 ? null : new PulseAudioDevice(stream, sampleRate, channels);
    }

    public void Start(AudioFill fill)
    {
        ArgumentNullException.ThrowIfNull(fill);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_worker is not null)
        {
            return;
        }

        _worker = new Thread(() => Feed(fill))
        {
            IsBackground = true,
            Name = "Audio",
        };

        _worker.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping = true;

        // A write blocks for at most the length of one buffer, so the
        // worker is out of the library long before this gives up; the
        // stream is only closed once it certainly is.
        if (_worker?.Join(TimeSpan.FromSeconds(2)) ?? true)
        {
            SimpleFree(_stream);
        }
    }

    [DllImport("libpulse-simple.so.0", EntryPoint = "pa_simple_new", ExactSpelling = true, CharSet = CharSet.Ansi)]
    private static extern nint SimpleNew(nint server, string name, int direction, nint device, string streamName, ref SampleSpec format, nint channelMap, ref BufferAttributes buffering, out int error);

    [DllImport("libpulse-simple.so.0", EntryPoint = "pa_simple_write", ExactSpelling = true)]
    private static extern int SimpleWrite(nint stream, byte[] data, nint bytes, out int error);

    [DllImport("libpulse-simple.so.0", EntryPoint = "pa_simple_drain", ExactSpelling = true)]
    private static extern int SimpleDrain(nint stream, out int error);

    [DllImport("libpulse-simple.so.0", EntryPoint = "pa_simple_free", ExactSpelling = true)]
    private static extern void SimpleFree(nint stream);

    private void Feed(AudioFill fill)
    {
        while (!_stopping)
        {
            fill(_buffer);
            Buffer.BlockCopy(_buffer, 0, _bytes, 0, _bytes.Length);

            if (SimpleWrite(_stream, _bytes, _bytes.Length, out _) < 0)
            {
                return;
            }
        }

        // What is already with the server is played out rather than cut
        // off, which matters for the last sound of a session.
        _ = SimpleDrain(_stream, out _);
    }

    /// <summary>
    /// A pa_sample_spec: the format of the samples, how many a second,
    /// and how many channels they are in.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SampleSpec
    {
        public int Format;
        public uint Rate;
        public byte Channels;
    }

    /// <summary>
    /// A pa_buffer_attr: how much sound the server should hold. Every
    /// field left at all ones is the server's own choice.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BufferAttributes
    {
        public uint MaximumLength;
        public uint TargetLength;
        public uint Prebuffer;
        public uint MinimumRequest;
        public uint FragmentSize;
    }
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Rezrov.Core.Audio;

/// <summary>
/// Audio output on Windows, through the waveOut functions of winmm.
/// </summary>
/// <remarks>
/// The oldest of the Windows audio interfaces and the plainest: open a
/// device for a format, hand it buffers, and it plays them in the order
/// they were handed over. It needs no library beyond the one every
/// Windows has, which keeps the published program a single file, and
/// it asks nothing of the audio stack that a game's sound effects
/// would notice.
///
/// A few buffers are kept in flight at once, so that the next is
/// already queued while one plays and the sound does not break up if a
/// turn of the game takes a moment. They and their headers are pinned,
/// since the device reads them long after the call that handed them
/// over has returned, and a moving garbage collector would otherwise
/// be free to shift them.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WaveOutDevice : IAudioDevice
{
    private const int BufferCount = 4;
    private const int BufferFrames = 1024;

    private const uint WaveFormatPcm = 1;
    private const uint HeaderDone = 0x00000001;
    private const uint HeaderPrepared = 0x00000002;
    private const uint HeaderInQueue = 0x00000010;

    private readonly short[][] _buffers = new short[BufferCount][];
    private readonly GCHandle[] _bufferHandles = new GCHandle[BufferCount];
    private readonly WaveHeader[] _headers = new WaveHeader[BufferCount];
    private readonly GCHandle _headersHandle;
    private readonly nint _device;
    private readonly int _headerSize = Marshal.SizeOf<WaveHeader>();

    private Thread? _worker;
    private volatile bool _stopping;
    private bool _disposed;

    private WaveOutDevice(nint device, int sampleRate, int channels)
    {
        _device = device;
        SampleRate = sampleRate;
        Channels = channels;

        _headersHandle = GCHandle.Alloc(_headers, GCHandleType.Pinned);
        for (var i = 0; i < BufferCount; i++)
        {
            _buffers[i] = new short[BufferFrames * channels];
            _bufferHandles[i] = GCHandle.Alloc(_buffers[i], GCHandleType.Pinned);
            _headers[i] = new WaveHeader
            {
                Data = _bufferHandles[i].AddrOfPinnedObject(),
                BufferLength = (uint)(_buffers[i].Length * sizeof(short)),
            };
        }
    }

    public int SampleRate { get; }

    public int Channels { get; }

    /// <summary>
    /// Opens the machine's audio output, or returns null where there is
    /// none to open: another operating system, a machine with no sound
    /// card, or a device another program has taken.
    /// </summary>
    public static WaveOutDevice? TryOpen(int sampleRate = 44100, int channels = 2)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var format = new WaveFormat
        {
            FormatTag = (ushort)WaveFormatPcm,
            Channels = (ushort)channels,
            SamplesPerSecond = (uint)sampleRate,
            AverageBytesPerSecond = (uint)(sampleRate * channels * sizeof(short)),
            BlockAlign = (ushort)(channels * sizeof(short)),
            BitsPerSample = 16,
            Size = 0,
        };

        // WAVE_MAPPER, which is whichever device the machine calls its
        // own, and no callback: the worker asks the headers themselves
        // whether the device has finished with them.
        return WaveOutOpen(out var device, unchecked((uint)-1), ref format, 0, 0, 0) == 0
            ? new WaveOutDevice(device, sampleRate, channels)
            : null;
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
        _worker?.Join(TimeSpan.FromSeconds(1));

        // Everything queued is dropped, and only then can the headers be
        // unprepared and the memory let go.
        _ = WaveOutReset(_device);
        for (var i = 0; i < BufferCount; i++)
        {
            if ((_headers[i].Flags & HeaderPrepared) != 0)
            {
                _ = WaveOutUnprepareHeader(_device, HeaderAddress(i), (uint)_headerSize);
            }
        }

        _ = WaveOutClose(_device);

        if (_headersHandle.IsAllocated)
        {
            _headersHandle.Free();
        }

        foreach (var handle in _bufferHandles)
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }
    }

    // Every argument and every field of the two structures is a plain
    // number or an address, so nothing here needs marshalling and the
    // published native program keeps these calls as they are.
    [DllImport("winmm", EntryPoint = "waveOutOpen", ExactSpelling = true)]
    private static extern uint WaveOutOpen(out nint device, uint deviceId, ref WaveFormat format, nint callback, nint instance, uint flags);

    [DllImport("winmm", EntryPoint = "waveOutPrepareHeader", ExactSpelling = true)]
    private static extern uint WaveOutPrepareHeader(nint device, nint header, uint size);

    [DllImport("winmm", EntryPoint = "waveOutUnprepareHeader", ExactSpelling = true)]
    private static extern uint WaveOutUnprepareHeader(nint device, nint header, uint size);

    [DllImport("winmm", EntryPoint = "waveOutWrite", ExactSpelling = true)]
    private static extern uint WaveOutWrite(nint device, nint header, uint size);

    [DllImport("winmm", EntryPoint = "waveOutReset", ExactSpelling = true)]
    private static extern uint WaveOutReset(nint device);

    [DllImport("winmm", EntryPoint = "waveOutClose", ExactSpelling = true)]
    private static extern uint WaveOutClose(nint device);

    private nint HeaderAddress(int index) => _headersHandle.AddrOfPinnedObject() + (index * _headerSize);

    private void Feed(AudioFill fill)
    {
        while (!_stopping)
        {
            var queued = false;

            for (var i = 0; i < BufferCount && !_stopping; i++)
            {
                // A buffer the device still holds is left alone; every
                // other one is filled again and handed back.
                if ((_headers[i].Flags & HeaderInQueue) != 0)
                {
                    continue;
                }

                fill(_buffers[i]);

                if ((_headers[i].Flags & HeaderPrepared) == 0 && WaveOutPrepareHeader(_device, HeaderAddress(i), (uint)_headerSize) != 0)
                {
                    return;
                }

                _headers[i].Flags &= ~HeaderDone;
                if (WaveOutWrite(_device, HeaderAddress(i), (uint)_headerSize) != 0)
                {
                    return;
                }

                queued = true;
            }

            if (!queued)
            {
                // Every buffer is with the device, so there is time to
                // spare: a pause well under the length of one buffer.
                Thread.Sleep(2);
            }
        }
    }

    /// <summary>
    /// The WAVEFORMATEX the device is opened for: plain samples, no
    /// extra bytes after the structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSecond;
        public uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }

    /// <summary>
    /// The WAVEHDR of one buffer. The device writes its flags, so this
    /// lives in pinned memory that both sides read.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public nint Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public nint User;
        public uint Flags;
        public uint Loops;
        public nint Next;
        public nint Reserved;
    }
}

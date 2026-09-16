using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Rezrov.Core.Audio;

/// <summary>
/// Audio output on macOS, through the audio queues of AudioToolbox.
/// </summary>
/// <remarks>
/// An audio queue is given a format and a few buffers, and plays them
/// in the order they are handed over. Unlike the other platforms it
/// asks rather than being told: when it has finished with a buffer it
/// calls back on a thread of its own, and the buffer is filled again
/// and handed back. The queue is primed with every buffer full before
/// it starts, so there is sound from the first moment.
///
/// AudioToolbox is part of the system, so nothing has to be installed
/// and the published program stays a single file.
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class AudioQueueDevice : IAudioDevice
{
    private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    private const int BufferCount = 3;
    private const int BufferFrames = 1024;

    // 'lpcm', the four characters that name linear PCM, and the flags
    // that say the samples are signed integers packed end to end.
    private const uint LinearPcm = 0x6C70636D;
    private const uint SignedInteger = 4;
    private const uint Packed = 8;

    // The offsets of the fields of an AudioQueueBuffer that matter: the
    // memory to write into, and how much of it was written. The first
    // field is a four byte capacity, and the pointer after it is
    // aligned to eight.
    private const int CapacityOffset = 0;
    private const int DataOffset = 8;
    private const int SizeOffset = 16;

    private readonly nint[] _buffers = new nint[BufferCount];
    private readonly short[] _samples;
    private readonly nint _queue;
    private GCHandle _self;
    private AudioFill? _fill;
    private volatile bool _stopping;
    private bool _disposed;

    private AudioQueueDevice(int sampleRate, int channels, Description format)
    {
        SampleRate = sampleRate;
        Channels = channels;
        _samples = new short[BufferFrames * channels];

        // The queue holds a handle to this device, so that the callback,
        // which is static because the system calls it, can find it.
        _self = GCHandle.Alloc(this);
        var status = NewOutput(ref format, Marshal.GetFunctionPointerForDelegate(Finished), GCHandle.ToIntPtr(_self), 0, 0, 0, out _queue);
        if (status != 0)
        {
            _self.Free();
            throw new InvalidOperationException($"The audio queue could not be made: {status}.");
        }

        var bytes = (uint)(_samples.Length * sizeof(short));
        for (var i = 0; i < BufferCount; i++)
        {
            if (AllocateBuffer(_queue, bytes, out _buffers[i]) != 0)
            {
                _buffers[i] = 0;
                continue;
            }

            // The buffer says how much it holds, in the field this
            // expects to find first. If that is not what was asked for,
            // the structure is not laid out as this believes and the
            // address the samples would be written to is not to be
            // trusted: better no sound than a wild write.
            if (Marshal.ReadInt32(_buffers[i], CapacityOffset) != bytes || Marshal.ReadIntPtr(_buffers[i], DataOffset) == 0)
            {
                _self.Free();
                _ = DisposeQueue(_queue, true);
                throw new InvalidOperationException("The audio queue gave back a buffer of a shape this does not know.");
            }
        }
    }

    // Kept in a field of its own so that the collector cannot take the
    // delegate while the system still holds its address.
    private static readonly OutputCallback Finished = BufferFinished;

    private delegate void OutputCallback(nint userData, nint queue, nint buffer);

    public int SampleRate { get; }

    public int Channels { get; }

    /// <summary>
    /// Opens the machine's audio output, or returns null where there is
    /// none to open: another operating system, or a queue the system
    /// refused to make.
    /// </summary>
    public static AudioQueueDevice? TryOpen(int sampleRate = 44100, int channels = 2)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        var bytesPerFrame = (uint)(channels * sizeof(short));
        var format = new Description
        {
            SampleRate = sampleRate,
            FormatId = LinearPcm,
            FormatFlags = SignedInteger | Packed,
            BytesPerPacket = bytesPerFrame,
            FramesPerPacket = 1,
            BytesPerFrame = bytesPerFrame,
            ChannelsPerFrame = (uint)channels,
            BitsPerChannel = 16,
            Reserved = 0,
        };

        try
        {
            return new AudioQueueDevice(sampleRate, channels, format);
        }
        catch (Exception e) when (e is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public void Start(AudioFill fill)
    {
        ArgumentNullException.ThrowIfNull(fill);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_fill is not null)
        {
            return;
        }

        _fill = fill;

        // Every buffer is filled and handed over before the queue is
        // started, so that it has a run of sound to play at once.
        foreach (var buffer in _buffers)
        {
            if (buffer != 0)
            {
                Refill(buffer);
            }
        }

        _ = StartQueue(_queue, 0);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping = true;

        // Stopping at once ends the callbacks, so nothing is filling a
        // buffer by the time the queue and its memory are let go.
        _ = StopQueue(_queue, true);
        _ = DisposeQueue(_queue, true);

        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    [DllImport(AudioToolbox, EntryPoint = "AudioQueueNewOutput", ExactSpelling = true)]
    private static extern int NewOutput(ref Description format, nint callback, nint userData, nint runLoop, nint runLoopMode, uint flags, out nint queue);

    [DllImport(AudioToolbox, EntryPoint = "AudioQueueAllocateBuffer", ExactSpelling = true)]
    private static extern int AllocateBuffer(nint queue, uint bytes, out nint buffer);

    [DllImport(AudioToolbox, EntryPoint = "AudioQueueEnqueueBuffer", ExactSpelling = true)]
    private static extern int EnqueueBuffer(nint queue, nint buffer, uint packetCount, nint packets);

    [DllImport(AudioToolbox, EntryPoint = "AudioQueueStart", ExactSpelling = true)]
    private static extern int StartQueue(nint queue, nint startTime);

    [DllImport(AudioToolbox, EntryPoint = "AudioQueueStop", ExactSpelling = true)]
    private static extern int StopQueue(nint queue, [MarshalAs(UnmanagedType.I1)] bool immediate);

    [DllImport(AudioToolbox, EntryPoint = "AudioQueueDispose", ExactSpelling = true)]
    private static extern int DisposeQueue(nint queue, [MarshalAs(UnmanagedType.I1)] bool immediate);

    // The system has finished with a buffer and wants another.
    private static void BufferFinished(nint userData, nint queue, nint buffer)
    {
        if (GCHandle.FromIntPtr(userData).Target is AudioQueueDevice device)
        {
            device.Refill(buffer);
        }
    }

    private void Refill(nint buffer)
    {
        if (_stopping || _fill is not { } fill)
        {
            return;
        }

        fill(_samples);

        // The buffer's own memory belongs to the queue, so the samples
        // are copied into it and its length written beside them.
        var bytes = _samples.Length * sizeof(short);
        Marshal.Copy(_samples, 0, Marshal.ReadIntPtr(buffer, DataOffset), _samples.Length);
        Marshal.WriteInt32(buffer, SizeOffset, bytes);
        _ = EnqueueBuffer(_queue, buffer, 0, 0);
    }

    /// <summary>
    /// An AudioStreamBasicDescription: what one sample is, how many of
    /// them make a second, and how they are laid out.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Description
    {
        public double SampleRate;
        public uint FormatId;
        public uint FormatFlags;
        public uint BytesPerPacket;
        public uint FramesPerPacket;
        public uint BytesPerFrame;
        public uint ChannelsPerFrame;
        public uint BitsPerChannel;
        public uint Reserved;
    }
}

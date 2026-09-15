namespace Rezrov.Core.Audio;

/// <summary>
/// Fills a buffer with the next stretch of sound, one sample per
/// channel per frame with the channels interleaved. Everything the
/// buffer already held is replaced, silence included.
/// </summary>
public delegate void AudioFill(Span<short> buffer);

/// <summary>
/// Somewhere for mixed samples to go: the machine's audio output.
/// </summary>
/// <remarks>
/// The device decides its own rate and how many channels it takes, and
/// asks for samples as it needs them, on a thread of its own. Each
/// platform needs its own, since nothing in the runtime plays audio;
/// where none is built, there is no sound, and a game is told so
/// through the header bit the Z-machine standard defines and the
/// gestalt answers Glk defines.
/// </remarks>
public interface IAudioDevice : IDisposable
{
    /// <summary>Samples of one channel a second.</summary>
    int SampleRate { get; }

    /// <summary>How many channels the device takes.</summary>
    int Channels { get; }

    /// <summary>
    /// Starts asking <paramref name="fill"/> for samples, and goes on
    /// until the device is disposed.
    /// </summary>
    void Start(AudioFill fill);
}

namespace Rezrov.Core.Audio;

/// <summary>
/// The sound a frontend plays through: an audio device, the mixer
/// feeding it, and the sounds decoded so far.
/// </summary>
/// <remarks>
/// One engine serves a whole session, and both machines play through
/// the same one, since the Z-machine's sound effects and Glk's sound
/// channels are the same sounds out of the same kind of resource file.
/// A frontend asks for one with <see cref="Create"/>, which answers
/// null on a machine with no audio output built or available, and that
/// answer is what the game is told when it asks whether sound can be
/// played at all.
///
/// Decoded sounds are kept, since a game plays the same few over and
/// over, and a sound that cannot be decoded is remembered as such so
/// the attempt is made once.
/// </remarks>
public sealed class AudioEngine : IDisposable
{
    private readonly IAudioDevice _device;
    private readonly Dictionary<int, AiffSound?> _sounds = [];
    private readonly Lock _lock = new();

    /// <param name="device">Where the mixed samples go.</param>
    public AudioEngine(IAudioDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        Mixer = new AudioMixer(device.SampleRate, device.Channels);
        device.Start(Mixer.Fill);
    }

    /// <summary>The mixer the sounds are played on.</summary>
    public AudioMixer Mixer { get; }

    /// <summary>
    /// An engine on the machine's own audio output, or null where there
    /// is none.
    /// </summary>
    public static AudioEngine? Create()
    {
        // Windows is the only platform with an output built so far;
        // elsewhere there is no sound, which is what a game is told.
        var device = OperatingSystem.IsWindows() ? WaveOutDevice.TryOpen() : null;
        return device is null ? null : new AudioEngine(device);
    }

    /// <summary>
    /// The decoded sound of a resource, or null for one whose format
    /// this cannot play, such as the tracker music a resource file may
    /// hold. The bytes are read once for each number.
    /// </summary>
    public AiffSound? Decode(int number, ReadOnlyMemory<byte> data)
    {
        lock (_lock)
        {
            if (_sounds.TryGetValue(number, out var known))
            {
                return known;
            }

            var sound = AiffReader.Read(data.Span);
            _sounds[number] = sound;
            return sound;
        }
    }

    /// <summary>
    /// Forgets a decoded sound, which a game says it is done with.
    /// </summary>
    public void Forget(int number)
    {
        lock (_lock)
        {
            _sounds.Remove(number);
        }
    }

    public void Dispose()
    {
        Mixer.StopAll();
        _device.Dispose();
    }
}

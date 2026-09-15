namespace Rezrov.Core.Audio;

/// <summary>
/// Mixes the sounds that are playing into one stream of samples for an
/// audio device, at the device's rate, however many channels it has.
/// </summary>
/// <remarks>
/// Everything a game can ask of a sound happens here: how many times it
/// repeats, how loud it is, a volume that slides from one level to
/// another over a stretch of time, a pause that holds it where it is,
/// and the moment a play ends, which is what the Z-machine's
/// end-of-sound routine and Glk's notification events are waiting for.
/// The sounds of the games that have them were recorded at whatever
/// rate suited the machine of the day, from nine to thirty-one thousand
/// samples a second, so every voice is resampled as it is mixed, which
/// is a walk along the sound at a fractional step with a straight line
/// drawn between the two samples on either side.
///
/// Nothing here touches an audio device or a clock: time passes only as
/// samples are asked for. A test can therefore ask for exactly as many
/// samples as it likes and know exactly what should come back, and know
/// when a sound must have ended. The callbacks are called after the
/// mixing is done and outside the lock, so what they do next, which is
/// usually to queue an event, cannot deadlock against the mixer.
/// </remarks>
public sealed class AudioMixer
{
    private readonly List<AudioVoice> _voices = [];
    private readonly Lock _lock = new();

    /// <param name="sampleRate">The device's samples a second.</param>
    /// <param name="channels">The device's channels, 1 or 2.</param>
    public AudioMixer(int sampleRate = 44100, int channels = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channels, 2);

        SampleRate = sampleRate;
        Channels = channels;
    }

    /// <summary>The samples a second this mixes for.</summary>
    public int SampleRate { get; }

    /// <summary>The channels this mixes for.</summary>
    public int Channels { get; }

    /// <summary>How many voices are playing or paused.</summary>
    public int VoiceCount
    {
        get
        {
            lock (_lock)
            {
                return _voices.Count;
            }
        }
    }

    /// <summary>Whether any voice is playing and not paused.</summary>
    public bool IsSounding
    {
        get
        {
            lock (_lock)
            {
                return _voices.Exists(voice => !voice.Paused);
            }
        }
    }

    /// <summary>Starts a sound and returns the voice playing it.</summary>
    /// <param name="sound">The sound.</param>
    /// <param name="repeats">
    /// How many times to play it in all, or -1 to keep playing until
    /// stopped.
    /// </param>
    /// <param name="volume">
    /// How loud, 1 being the sound as recorded; more than 1 is louder
    /// than the recording and may clip against the other voices.
    /// </param>
    /// <param name="paused">
    /// Whether it starts held at its beginning.
    /// </param>
    /// <param name="cycleEnded">
    /// Called as each play of the sound ends, the last included, from
    /// whatever thread is asking for samples.
    /// </param>
    public AudioVoice Play(AiffSound sound, int repeats = 1, float volume = 1, bool paused = false, Action? cycleEnded = null)
    {
        ArgumentNullException.ThrowIfNull(sound);

        var voice = new AudioVoice(sound, repeats, volume, paused, cycleEnded, SampleRate);
        lock (_lock)
        {
            _voices.Add(voice);
        }

        return voice;
    }

    /// <summary>Stops every voice, with no callbacks.</summary>
    public void StopAll()
    {
        lock (_lock)
        {
            foreach (var voice in _voices)
            {
                voice.Stop();
            }

            _voices.Clear();
        }
    }

    /// <summary>
    /// Fills a buffer with the next stretch of sound, which is silence
    /// where nothing is playing. The buffer holds one sample per
    /// channel per frame, the channels interleaved.
    /// </summary>
    public void Fill(Span<short> buffer)
    {
        var frames = buffer.Length / Channels;
        List<Action>? callbacks = null;

        lock (_lock)
        {
            buffer.Clear();

            if (_voices.Count == 0)
            {
                return;
            }

            Span<float> frame = stackalloc float[2];

            for (var f = 0; f < frames; f++)
            {
                frame.Clear();

                foreach (var voice in _voices)
                {
                    voice.Mix(frame[..Channels], ref callbacks);
                }

                for (var c = 0; c < Channels; c++)
                {
                    // A sum past the ends of the range is as loud as the
                    // device can be, which is what clipping sounds like.
                    var value = Math.Clamp(frame[c], -1f, 1f);
                    buffer[(f * Channels) + c] = (short)MathF.Round(value * 32767f, MidpointRounding.AwayFromZero);
                }
            }

            _voices.RemoveAll(voice => voice.HasEnded);
        }

        if (callbacks is null)
        {
            return;
        }

        foreach (var callback in callbacks)
        {
            callback();
        }
    }
}

/// <summary>
/// One sound being played by a mixer: where it has reached, how loud it
/// is, and whether it is held.
/// </summary>
/// <remarks>
/// A voice is what a frontend keeps for a sound it was asked to play,
/// so that the game can later stop it, pause it, or change its volume.
/// Every method is safe to call while the mixer is running.
/// </remarks>
public sealed class AudioVoice
{
    private readonly AiffSound _sound;
    private readonly int _deviceRate;
    private readonly double _step;
    private readonly Action? _cycleEnded;
    private readonly Lock _lock = new();

    private double _position;
    private int _playsLeft;
    private float _volume;
    private float _targetVolume;
    private float _volumeStep;
    private Action? _volumeReached;

    internal AudioVoice(AiffSound sound, int repeats, float volume, bool paused, Action? cycleEnded, int deviceRate)
    {
        _sound = sound;
        _playsLeft = repeats;
        _volume = volume;
        _targetVolume = volume;
        _cycleEnded = cycleEnded;
        _deviceRate = deviceRate;

        // How far along the sound one of the device's samples goes: a
        // sound recorded slower than the device plays steps by less
        // than a whole sample, and one recorded faster steps by more.
        _step = sound.SampleRate / deviceRate;
        Paused = paused;
    }

    /// <summary>Whether the voice is held where it is.</summary>
    public bool Paused { get; private set; }

    /// <summary>
    /// Whether the voice is finished and will make no more sound.
    /// </summary>
    public bool HasEnded { get; private set; }

    /// <summary>How loud the voice is now, a fade included.</summary>
    public float Volume
    {
        get
        {
            lock (_lock)
            {
                return _volume;
            }
        }
    }

    /// <summary>
    /// Ends the voice at once. Nothing it was asked to report is
    /// reported: a sound stopped by hand did not end by itself.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            HasEnded = true;
            _volumeReached = null;
        }
    }

    /// <summary>Holds the voice where it is.</summary>
    public void Pause()
    {
        lock (_lock)
        {
            Paused = true;
        }
    }

    /// <summary>Lets the voice go on from where it was held.</summary>
    public void Resume()
    {
        lock (_lock)
        {
            Paused = false;
        }
    }

    /// <summary>
    /// Changes how loud the voice is, at once or by sliding to the new
    /// level over a time.
    /// </summary>
    /// <param name="volume">The volume to reach.</param>
    /// <param name="duration">
    /// How long the slide takes, or zero to change at once.
    /// </param>
    /// <param name="reached">
    /// Called when the new volume is reached, from whatever thread is
    /// asking for samples. A slide that another interrupts never
    /// reaches its volume and never calls this.
    /// </param>
    public void SetVolume(float volume, TimeSpan duration, Action? reached = null)
    {
        lock (_lock)
        {
            // Whatever slide was under way is abandoned where it is.
            _targetVolume = volume;
            _volumeReached = reached;

            var frames = duration.TotalSeconds * _deviceRate;
            if (frames < 1 || HasEnded)
            {
                _volume = volume;
                _volumeStep = 0;
                var finished = _volumeReached;
                _volumeReached = null;
                finished?.Invoke();
                return;
            }

            _volumeStep = (float)((volume - _volume) / frames);
        }
    }

    // Adds this voice's next frame into the frame being mixed, and
    // collects whatever it has to report.
    internal void Mix(Span<float> frame, ref List<Action>? callbacks)
    {
        lock (_lock)
        {
            if (HasEnded)
            {
                return;
            }

            if (!Paused)
            {
                var first = (int)_position;
                var blend = (float)(_position - first);

                for (var c = 0; c < frame.Length; c++)
                {
                    // Between the two samples on either side of where
                    // the walk has reached.
                    var from = _sound.Frame(first, c);
                    var to = _sound.Frame(first + 1, c);
                    frame[c] += ((from * (1 - blend)) + (to * blend)) * _volume;
                }

                _position += _step;
                if (_position >= _sound.Frames)
                {
                    EndCycle(ref callbacks);
                }
            }

            StepVolume(ref callbacks);
        }
    }

    private void EndCycle(ref List<Action>? callbacks)
    {
        // A sound that has played the number of times asked for is
        // over; one with plays left starts again, keeping whatever
        // fraction of a sample it had gone past the end.
        if (_playsLeft > 0)
        {
            _playsLeft--;
        }

        if (_playsLeft == 0)
        {
            HasEnded = true;
            _position = _sound.Frames;
        }
        else
        {
            _position -= _sound.Frames;
        }

        if (_cycleEnded is { } ended)
        {
            (callbacks ??= []).Add(ended);
        }
    }

    private void StepVolume(ref List<Action>? callbacks)
    {
        if (_volumeStep == 0)
        {
            return;
        }

        _volume += _volumeStep;
        var reached = _volumeStep > 0 ? _volume >= _targetVolume : _volume <= _targetVolume;
        if (!reached)
        {
            return;
        }

        _volume = _targetVolume;
        _volumeStep = 0;
        if (_volumeReached is { } completed)
        {
            _volumeReached = null;
            (callbacks ??= []).Add(completed);
        }
    }
}

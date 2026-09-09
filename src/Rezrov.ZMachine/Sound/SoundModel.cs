using System.Collections.Concurrent;
using Rezrov.Core.Blorb;

namespace Rezrov.ZMachine.Sound;

/// <summary>
/// The sound model of section 9: which sounds exist, which are playing,
/// which may interrupt which, and when the game's routine is called.
/// </summary>
/// <remarks>
/// The frontend's <see cref="ISound"/> makes the noise and reports when
/// each play of a sound ends. Everything else is here: [zm 9.4.2] one
/// sample and one piece of music at a time, [zm 9.4.4] the routine
/// called only when a sound ends by itself, and the rule from the
/// remarks on section 9 that a new sample started while one begun
/// since the last keyboard input is still playing waits for that one to
/// finish a cycle, which is what The Lurking Horror needs to sound as
/// it did on Infocom's slow Amiga interpreter. [blorb 14.3] Music is
/// never held up that way.
///
/// A frontend reports the end of a cycle from any thread, so the report
/// is queued and acted on when the interpreter asks, at a point where
/// running a game routine is safe.
/// </remarks>
public sealed class SoundModel
{
    private readonly ISound _sound;
    private readonly ZMachineVersion _version;
    private readonly Dictionary<int, SoundResource> _resources = [];
    private readonly ConcurrentQueue<int> _endedCycles = new();
    private Playing? _sample;
    private Playing? _music;
    private Request? _waitingSample;

    public SoundModel(ISound sound, ZMachineVersion version)
    {
        ArgumentNullException.ThrowIfNull(sound);

        _sound = sound;
        _version = version;
    }

    /// <summary>The frontend making the noise.</summary>
    public ISound Frontend => _sound;

    /// <summary>
    /// [zm 9.1.2] Whether sounds beyond a bleep can be played at all.
    /// </summary>
    public bool CanPlaySounds => _sound.CanPlaySounds;

    /// <summary>The sounds available, by number.</summary>
    public IReadOnlyDictionary<int, SoundResource> Resources => _resources;

    /// <summary>The number of the sample now playing, or null.</summary>
    public int? PlayingSample => _sample?.Sound.Number;

    /// <summary>The number of the music now playing, or null.</summary>
    public int? PlayingMusic => _music?.Sound.Number;

    /// <summary>
    /// Whether a frontend has reported the end of a cycle that has not
    /// been acted on yet. Cheap enough to ask before every instruction.
    /// </summary>
    public bool HasPendingEvents => !_endedCycles.IsEmpty;

    /// <summary>
    /// [zm 9.1] Takes the sounds from a resource file, which is where a
    /// modern interpreter finds them, with [blorb 11.4] the looping
    /// table a Version 3 game relies on.
    /// </summary>
    public void LoadResources(BlorbFile blorb)
    {
        ArgumentNullException.ThrowIfNull(blorb);

        foreach (var resource in blorb.Resources)
        {
            if (resource.Usage != ResourceUsage.Sound)
            {
                continue;
            }

            var loopsForever = blorb.LoopingSounds.TryGetValue(resource.Number, out var forever) && forever;
            _resources[resource.Number] = new SoundResource(resource.Number, resource.ChunkType, resource.Data, PlaysOnce: !loopsForever);
        }
    }

    /// <summary>
    /// Makes a sound available, for tests and other sources.
    /// </summary>
    public void AddResource(SoundResource sound)
    {
        ArgumentNullException.ThrowIfNull(sound);
        _resources[sound.Number] = sound;
    }

    /// <summary>[zm 9.2] Bleeps, high for 1 and low for 2.</summary>
    public void Bleep(int number) => _sound.Bleep(number);

    /// <summary>[zm 9.4.1] Prepares a sound for use.</summary>
    /// <returns>False if there is no such sound.</returns>
    public bool Prepare(int number)
    {
        if (!_resources.TryGetValue(number, out var sound))
        {
            return false;
        }

        _sound.Prepare(sound);
        return true;
    }

    /// <summary>
    /// [zm 9.4.2] Starts a sound, stopping whatever of its kind was
    /// playing, or holding it back as the remarks on section 9 ask.
    /// </summary>
    /// <param name="number">The sound.</param>
    /// <param name="volume">[zm 9.3] 1 to 8.</param>
    /// <param name="repeats">
    /// [zm 9.4.3] Plays in all, or -1 forever. Ignored in Version 3,
    /// where [blorb 11.4] the resource file decides.
    /// </param>
    /// <param name="routine">
    /// [zm 9.4.4] The routine to call when the sound ends by itself, or
    /// 0 for none.
    /// </param>
    /// <returns>False if there is no such sound.</returns>
    public bool Play(int number, int volume, int repeats, ushort routine)
    {
        if (!_resources.TryGetValue(number, out var sound))
        {
            return false;
        }

        // [zm 9.1.2] With no audio the game was told, and a sound it
        // asks for anyway plays nowhere and ends never.
        if (!_sound.CanPlaySounds)
        {
            return true;
        }

        if (_version == ZMachineVersion.V3)
        {
            repeats = sound.PlaysOnce ? 1 : -1;
        }

        var request = new Request(sound, Math.Clamp(volume, 1, 8), repeats, routine);

        if (sound.IsMusic)
        {
            // [blorb 14.3] New music interrupts old music at once.
            Start(request, ref _music);
            return true;
        }

        if (_sample is { StartedSinceInput: true })
        {
            // The remarks on section 9: wait for the earlier sample to
            // finish a cycle, then replace it.
            _waitingSample = request;
            return true;
        }

        Start(request, ref _sample);
        return true;
    }

    /// <summary>
    /// [zm op:sound_effect] Stops a sound if it is playing, without
    /// calling its routine.
    /// </summary>
    public void Stop(int number)
    {
        if (_sample?.Sound.Number == number)
        {
            StopPlaying(ref _sample);
        }

        if (_music?.Sound.Number == number)
        {
            StopPlaying(ref _music);
        }

        if (_waitingSample?.Sound.Number == number)
        {
            _waitingSample = null;
        }
    }

    /// <summary>[zm op:sound_effect] Stops everything.</summary>
    public void StopAll()
    {
        StopPlaying(ref _sample);
        StopPlaying(ref _music);
        _waitingSample = null;
    }

    /// <summary>
    /// [zm 9.4.5] The game is done with a sound: it is stopped if it
    /// plays and the frontend may forget it.
    /// </summary>
    public void Finish(int number)
    {
        Stop(number);
        if (_resources.TryGetValue(number, out var sound))
        {
            _sound.Finish(sound);
        }
    }

    /// <summary>[zm op:sound_effect] Finishes with everything.</summary>
    public void FinishAll()
    {
        StopAll();
        foreach (var sound in _resources.Values)
        {
            _sound.Finish(sound);
        }
    }

    /// <summary>
    /// The player has been asked for input, so anything playing was
    /// started before it, and a new sample may replace it at once.
    /// </summary>
    public void InputHappened()
    {
        if (_sample is not null)
        {
            _sample.StartedSinceInput = false;
        }

        if (_music is not null)
        {
            _music.StartedSinceInput = false;
        }
    }

    /// <summary>
    /// Acts on the cycle ends the frontend has reported, and returns
    /// the routines the game asked to have called, [zm 9.4.4] for the
    /// sounds that ended by themselves. The interpreter calls these as
    /// interrupt routines.
    /// </summary>
    public IReadOnlyList<ushort> Update()
    {
        var routines = new List<ushort>();

        while (_endedCycles.TryDequeue(out var number))
        {
            if (_sample?.Sound.Number == number)
            {
                CycleEnded(ref _sample, routines);

                // A sample held back for this one takes over now.
                if (_waitingSample is { } waiting && (_sample is null || _sample.Sound.Number == number))
                {
                    _waitingSample = null;
                    Start(waiting, ref _sample);
                }
            }
            else if (_music?.Sound.Number == number)
            {
                CycleEnded(ref _music, routines);
            }
        }

        return routines;
    }

    private static void CycleEnded(ref Playing? playing, List<ushort> routines)
    {
        playing!.CyclesPlayed++;

        if (playing.Repeats >= 0 && playing.CyclesPlayed >= playing.Repeats)
        {
            // [zm 9.4.4] Ended by itself, having played the requested
            // number of times: now, and only now, the routine.
            if (playing.Routine != 0)
            {
                routines.Add(playing.Routine);
            }

            playing = null;
        }
    }

    private void Start(Request request, ref Playing? slot)
    {
        StopPlaying(ref slot);

        var playing = new Playing(request.Sound, request.Repeats, request.Routine);
        slot = playing;
        var number = request.Sound.Number;
        _sound.Play(request.Sound, request.Volume, request.Repeats, () => _endedCycles.Enqueue(number));
    }

    private void StopPlaying(ref Playing? slot)
    {
        if (slot is null)
        {
            return;
        }

        // [zm 9.4.4] Stopped, so its routine is never called.
        var sound = slot.Sound;
        slot = null;
        _sound.StopPlaying(sound);
    }

    private sealed class Playing(SoundResource sound, int repeats, ushort routine)
    {
        public SoundResource Sound { get; } = sound;

        public int Repeats { get; } = repeats;

        public ushort Routine { get; } = routine;

        public int CyclesPlayed { get; set; }

        public bool StartedSinceInput { get; set; } = true;
    }

    private sealed record Request(SoundResource Sound, int Volume, int Repeats, ushort Routine);
}

using Rezrov.Core.Audio;

namespace Rezrov.ZMachine.Sound;

/// <summary>
/// Sound through an audio engine: the game's sound effects played on
/// the machine's audio output, and a bleep where there is none.
/// </summary>
/// <remarks>
/// [zm 9.1] What is left to a frontend is making the noise: which
/// sounds may play at once, when the game's end-of-sound routine runs,
/// and the queueing The Lurking Horror needs all live in
/// <see cref="SoundModel"/> above this. Each sound the model starts
/// becomes a voice on the mixer, kept here by its number so that the
/// model can stop it again.
///
/// [zm 9.1.2] With no audio output on this machine there is no engine,
/// <see cref="CanPlaySounds"/> is false, and the interpreter clears
/// the header bit that tells the game so. The bleeps still work: a
/// short tone through the mixer where there is one, and the console's
/// own beep on Windows where there is not.
/// </remarks>
public sealed class EngineSound : ISound
{
    private readonly AudioEngine? _engine;
    private readonly Dictionary<int, AudioVoice> _playing = [];

    /// <param name="engine">
    /// The machine's audio output, or null for a terminal with none.
    /// </param>
    public EngineSound(AudioEngine? engine = null)
    {
        _engine = engine;
    }

    public bool CanPlaySounds => _engine is not null;

    public void Bleep(int number)
    {
        // [zm 9.2] High-pitched for 1, low-pitched for 2.
        var frequency = number == 2 ? 440 : 880;

        if (_engine is { } engine)
        {
            engine.Mixer.Play(AudioTone.Sine(frequency, TimeSpan.FromMilliseconds(120), engine.Mixer.SampleRate), volume: 0.4f);
            return;
        }

        // Only Windows has a console beep with a pitch, and it holds up
        // the game while it sounds; elsewhere the terminal bell would
        // have to go through the driver, and silence is the honest
        // answer.
        if (OperatingSystem.IsWindows())
        {
            Console.Beep(frequency, 120);
        }
    }

    public void Prepare(SoundResource sound)
    {
        ArgumentNullException.ThrowIfNull(sound);

        // [zm 9.4.1] Reading the sound now is the whole of preparing it.
        _engine?.Decode(sound.Number, sound.Data);
    }

    public void Play(SoundResource sound, int volume, int repeats, Action cycleEnded)
    {
        ArgumentNullException.ThrowIfNull(sound);
        ArgumentNullException.ThrowIfNull(cycleEnded);

        if (_engine is not { } engine || engine.Decode(sound.Number, sound.Data) is not { } decoded)
        {
            // A sound in a format with no decoder plays silently and
            // never ends, which is what a game with no audio at all
            // gets, and no game in the corpus has one.
            return;
        }

        StopPlaying(sound);

        // [zm 9.3] Volume runs from 1 to 8, 8 being loudest, which is
        // taken as a straight fraction of the sound as recorded.
        lock (_playing)
        {
            _playing[sound.Number] = engine.Mixer.Play(decoded, repeats, Math.Clamp(volume, 1, 8) / 8f, cycleEnded: cycleEnded);
        }
    }

    public void StopPlaying(SoundResource sound)
    {
        ArgumentNullException.ThrowIfNull(sound);

        lock (_playing)
        {
            if (_playing.Remove(sound.Number, out var voice))
            {
                voice.Stop();
            }
        }
    }

    public void Finish(SoundResource sound)
    {
        ArgumentNullException.ThrowIfNull(sound);

        // [zm 9.4.5] Done with for a while: stopped, and forgotten, so
        // the next use reads it again.
        StopPlaying(sound);
        _engine?.Forget(sound.Number);
    }
}

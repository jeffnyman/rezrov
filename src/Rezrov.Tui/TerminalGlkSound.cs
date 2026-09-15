using Rezrov.Core.Audio;
using Rezrov.Core.Blorb;
using Rezrov.Glulx.Glk;

namespace Rezrov.Tui;

/// <summary>
/// Glk sound on a terminal: each channel's sound played as a voice on
/// the machine's audio output.
/// </summary>
/// <remarks>
/// [glk #sound] The channels, what each call means, and the events the
/// ends of sounds become are the library's; what is left here is to
/// start a sound, hold it, change how loud it is, and say when it ends
/// by itself. The library's channel carries the volume and the pause
/// to apply, so a sound started on a channel that is paused or quiet
/// begins that way.
///
/// [glk #sound_testing] With no audio output on this machine there is
/// no engine, <see cref="CanPlaySounds"/> is false, and the gestalt
/// answers tell the game there is no sound rather than playing
/// nothing.
/// </remarks>
public sealed class TerminalGlkSound : IGlkSound
{
    private readonly AudioEngine? _engine;
    private readonly Dictionary<uint, AudioVoice> _playing = [];

    /// <param name="engine">
    /// The machine's audio output, or null for a terminal with none.
    /// </param>
    public TerminalGlkSound(AudioEngine? engine = null)
    {
        _engine = engine;
    }

    public bool CanPlaySounds => _engine is not null;

    public bool Play(GlkSoundChannel channel, BlorbResource sound, uint repeats, Action ended)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(sound);
        ArgumentNullException.ThrowIfNull(ended);

        // [glk #sound_playing] A sound the frontend cannot play is a
        // failure the game hears about: the tracker music of [blorb 3]
        // has no decoder here, and nor has anything else that is not
        // sampled sound.
        if (_engine is not { } engine || engine.Decode(sound.Number, sound.Data) is not { } decoded)
        {
            return false;
        }

        StopPlaying(channel);

        // [glk op:schannel_play_ext] The end is reported once, after the
        // last repetition; a sound repeating forever never reports one,
        // so there is nothing to count it with.
        var plays = repeats == uint.MaxValue ? -1 : (int)Math.Min(repeats, int.MaxValue);
        Action? cycleEnded = null;
        if (plays > 0)
        {
            var left = plays;
            cycleEnded = () =>
            {
                if (Interlocked.Decrement(ref left) == 0)
                {
                    ended();
                }
            };
        }

        lock (_playing)
        {
            _playing[channel.Id] = engine.Mixer.Play(decoded, plays, Amplitude(channel.Volume), channel.Paused, cycleEnded);
        }

        return true;
    }

    public void StopPlaying(GlkSoundChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        lock (_playing)
        {
            if (_playing.Remove(channel.Id, out var voice))
            {
                voice.Stop();
            }
        }
    }

    public void Pause(GlkSoundChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        Voice(channel)?.Pause();
    }

    public void Unpause(GlkSoundChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        Voice(channel)?.Resume();
    }

    public void SetVolume(GlkSoundChannel channel, uint volume, uint duration, Action? changed)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Voice(channel) is { } voice)
        {
            voice.SetVolume(Amplitude(volume), TimeSpan.FromMilliseconds(duration), changed);
            return;
        }

        // [glk op:schannel_set_volume_ext deviates] A channel with
        // nothing playing has no samples to slide the volume across, so
        // the new volume is simply what the next sound starts at and a
        // change that asked to be told when it finished is told at once
        // rather than after a silent wait.
        changed?.Invoke();
    }

    public void LoadHint(BlorbResource sound, bool load)
    {
        ArgumentNullException.ThrowIfNull(sound);

        // [glk op:sound_load_hint] Reading the sound, or forgetting it.
        if (load)
        {
            _engine?.Decode(sound.Number, sound.Data);
        }
        else
        {
            _engine?.Forget(sound.Number);
        }
    }

    public void Close(GlkSoundChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        StopPlaying(channel);
    }

    // [glk #sound_channels] Full volume is 0x10000, and a game may ask
    // for more than that, which the mixer allows and clips.
    private static float Amplitude(uint volume) => volume / (float)GlkSoundChannel.FullVolume;

    private AudioVoice? Voice(GlkSoundChannel channel)
    {
        lock (_playing)
        {
            return _playing.TryGetValue(channel.Id, out var voice) ? voice : null;
        }
    }
}

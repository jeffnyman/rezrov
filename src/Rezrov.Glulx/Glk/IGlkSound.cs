using Rezrov.Core.Blorb;

namespace Rezrov.Glulx.Glk;

/// <summary>
/// What a frontend provides for Glk sound: the playing of sound
/// resources on the library's channels.
/// </summary>
/// <remarks>
/// [glk #sound] The library keeps the channels, decides what each call
/// means, and turns the ends of sounds and of volume changes into the
/// events the game asked for; what is left for a frontend is to make
/// noise. A frontend with no audio says so through
/// <see cref="CanPlaySounds"/>, which the gestalt answers pass on to the
/// game, and is never asked to play anything.
///
/// A frontend that plays sounds calls the action it is given when a
/// sound ends by itself or a volume change completes, on whatever
/// thread it likes; the library takes it from there when the game next
/// waits for an event, and asks the display to wake if it is waiting
/// already. A channel's <see cref="GlkSoundChannel.Volume"/> and
/// <see cref="GlkSoundChannel.Paused"/> are set before a sound is
/// started on it, so a frontend reads them when it starts one.
/// </remarks>
public interface IGlkSound
{
    /// <summary>
    /// [glk #sound_testing] Whether sounds can be played at all. The
    /// gestalt answers for sound follow it.
    /// </summary>
    bool CanPlaySounds { get; }

    /// <summary>
    /// [glk #sound_playing] Starts a sound on a channel, stopping
    /// whatever the channel was playing.
    /// </summary>
    /// <param name="channel">
    /// The channel, with its volume and pause.
    /// </param>
    /// <param name="sound">The sound resource to play.</param>
    /// <param name="repeats">
    /// How many times to play it in all, never zero, or 0xFFFFFFFF
    /// forever.
    /// </param>
    /// <param name="ended">
    /// To be called once, when the last repetition ends by itself, from
    /// any thread. Never called for a sound that is stopped, replaced,
    /// or repeating forever.
    /// </param>
    /// <returns>
    /// False if the sound cannot be played, for a format the frontend
    /// has no decoder for, say.
    /// </returns>
    bool Play(GlkSoundChannel channel, BlorbResource sound, uint repeats, Action ended);

    /// <summary>
    /// [glk op:schannel_stop] Stops the channel's sound, if it has one.
    /// </summary>
    void StopPlaying(GlkSoundChannel channel);

    /// <summary>
    /// [glk op:schannel_pause] Holds the channel's sound where it is,
    /// and any sound started on the channel later, until unpaused.
    /// </summary>
    void Pause(GlkSoundChannel channel);

    /// <summary>
    /// [glk op:schannel_unpause] Lets the channel's sound go on from
    /// where it was held.
    /// </summary>
    void Unpause(GlkSoundChannel channel);

    /// <summary>
    /// [glk op:schannel_set_volume_ext] Changes the channel's volume,
    /// at once or smoothly over a duration.
    /// </summary>
    /// <param name="channel">The channel, whose volume is already the
    /// new one.</param>
    /// <param name="volume">
    /// [glk #sound_channels] The volume: 0 is silence, 0x10000 full,
    /// more overdriven.
    /// </param>
    /// <param name="duration">
    /// Milliseconds for the change to take, or zero for at once.
    /// </param>
    /// <param name="changed">
    /// To be called when a change with a duration completes, from any
    /// thread, or null when nothing waits on it. A change interrupted
    /// by another never completes.
    /// </param>
    void SetVolume(GlkSoundChannel channel, uint volume, uint duration, Action? changed);

    /// <summary>
    /// [glk op:sound_load_hint] The game intends to use a sound soon,
    /// or is done with it for a while.
    /// </summary>
    void LoadHint(BlorbResource sound, bool load);

    /// <summary>
    /// [glk op:schannel_destroy] The channel is gone: its sound stops,
    /// and whatever the frontend keeps for it can go.
    /// </summary>
    void Close(GlkSoundChannel channel);
}

/// <summary>
/// A frontend with no audio: no sound can be played, and the gestalt
/// answers say so.
/// </summary>
public sealed class NoGlkSound : IGlkSound
{
    public static NoGlkSound Instance { get; } = new();

    public bool CanPlaySounds => false;

    public bool Play(GlkSoundChannel channel, BlorbResource sound, uint repeats, Action ended) => false;

    public void StopPlaying(GlkSoundChannel channel)
    {
    }

    public void Pause(GlkSoundChannel channel)
    {
    }

    public void Unpause(GlkSoundChannel channel)
    {
    }

    public void SetVolume(GlkSoundChannel channel, uint volume, uint duration, Action? changed)
    {
    }

    public void LoadHint(BlorbResource sound, bool load)
    {
    }

    public void Close(GlkSoundChannel channel)
    {
    }
}

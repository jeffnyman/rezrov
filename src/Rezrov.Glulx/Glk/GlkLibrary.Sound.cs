using System.Collections.Concurrent;
using Rezrov.Core.Blorb;

namespace Rezrov.Glulx.Glk;

/// <summary>
/// [glk #sound] The sound channels: creating and destroying them,
/// playing on them, and the events the ends of sounds and of volume
/// changes become.
/// </summary>
/// <remarks>
/// The frontend's <see cref="IGlkSound"/> makes the noise and reports
/// the end of a sound or a volume change from whatever thread it
/// likes, so each report is queued and turned into an event when the
/// game next waits for one. A report is only honored if the sound or
/// change it belongs to is still the channel's current one: [glk
/// #sound_playing] a sound that was stopped, replaced, or on a channel
/// since destroyed gives no event, and neither does a volume change
/// another interrupted.
/// </remarks>
public sealed partial class GlkLibrary
{
    private readonly IGlkSound _sound;
    private readonly ConcurrentQueue<SoundReport> _soundReports = new();

    /// <summary>[glk #sound] Every sound channel.</summary>
    public GlkRegistry<GlkSoundChannel> SoundChannels { get; } = new();

    /// <summary>
    /// Whether the frontend has reported an end that has not become an
    /// event yet.
    /// </summary>
    public bool HasPendingSoundEvents => !_soundReports.IsEmpty;

    /// <summary>
    /// [glk op:schannel_create_ext] Makes a channel at a volume, or
    /// [glk op:schannel_create] at full volume, or returns null where
    /// no sound can be played, as gestalt_Sound has said.
    /// </summary>
    public GlkSoundChannel? CreateSoundChannel(uint rock, uint volume = GlkSoundChannel.FullVolume)
    {
        if (!_sound.CanPlaySounds)
        {
            return null;
        }

        var channel = new GlkSoundChannel(rock, volume);
        SoundChannels.Add(channel);
        return channel;
    }

    /// <summary>
    /// [glk op:schannel_destroy] Destroys a channel, stopping its sound
    /// with no event.
    /// </summary>
    public void DestroySoundChannel(GlkSoundChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        Forget(channel);
        _sound.Close(channel);
        SoundChannels.Remove(channel);
    }

    /// <summary>
    /// [glk op:schannel_play_ext] Plays a sound on a channel, or [glk
    /// op:schannel_play] once with no event.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="number">The sound resource's number.</param>
    /// <param name="repeats">
    /// How many times to play it, 0xFFFFFFFF for forever, or zero for
    /// not at all.
    /// </param>
    /// <param name="notify">
    /// Nonzero to ask for a sound notification event when the last
    /// repetition ends by itself.
    /// </param>
    /// <returns>Whether the sound started, or was asked not to.</returns>
    public bool PlaySound(GlkSoundChannel channel, uint number, uint repeats = 1, uint notify = 0)
    {
        ArgumentNullException.ThrowIfNull(channel);

        // [glk #sound_playing] Whatever was playing stops first, with
        // no event, even when the new sound is the same one, and even
        // when there is no new sound to play.
        StopSound(channel);
        if (repeats == 0)
        {
            return true;
        }

        if (Resources?.Find(ResourceUsage.Sound, (int)number) is not { } resource)
        {
            return false;
        }

        var generation = ++channel.Generation;
        if (!_sound.Play(channel, resource, repeats, () => Report(new SoundReport(channel, generation, false, number, notify))))
        {
            return false;
        }

        channel.Playing = number;
        channel.Notify = notify;
        return true;
    }

    /// <summary>
    /// [glk op:schannel_play_multi] Starts a sound on each of several
    /// channels, once each, with one notify value for all.
    /// </summary>
    /// <returns>How many of them started.</returns>
    public uint PlaySounds(IReadOnlyList<GlkSoundChannel> channels, IReadOnlyList<uint> numbers, uint notify)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(numbers);

        var started = 0u;
        for (var i = 0; i < Math.Min(channels.Count, numbers.Count); i++)
        {
            if (PlaySound(channels[i], numbers[i], 1, notify))
            {
                started++;
            }
        }

        return started;
    }

    /// <summary>
    /// [glk op:schannel_stop] Stops a channel's sound, with no event.
    /// </summary>
    public void StopSound(GlkSoundChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (channel.Playing is null)
        {
            return;
        }

        Forget(channel);
        _sound.StopPlaying(channel);
    }

    /// <summary>
    /// [glk op:schannel_pause] Pauses a channel, playing or not.
    /// </summary>
    public void PauseSound(GlkSoundChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (channel.Paused)
        {
            return;
        }

        channel.Paused = true;
        _sound.Pause(channel);
    }

    /// <summary>[glk op:schannel_unpause] Unpauses a channel.</summary>
    public void UnpauseSound(GlkSoundChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (!channel.Paused)
        {
            return;
        }

        channel.Paused = false;
        _sound.Unpause(channel);
    }

    /// <summary>
    /// [glk op:schannel_set_volume_ext] Changes a channel's volume, at
    /// once or over a duration in milliseconds, or [glk
    /// op:schannel_set_volume] at once with no event.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="volume">The new volume.</param>
    /// <param name="duration">Milliseconds, or zero for at once.</param>
    /// <param name="notify">
    /// Nonzero to ask for a volume notification event when the change
    /// completes.
    /// </param>
    public void SetSoundVolume(GlkSoundChannel channel, uint volume, uint duration = 0, uint notify = 0)
    {
        ArgumentNullException.ThrowIfNull(channel);

        // [glk #sound_playing] One change at a time: a new one
        // interrupts the last, whose event then never comes.
        var generation = ++channel.VolumeGeneration;
        channel.Volume = volume;

        if (duration == 0)
        {
            // A change made at once has completed.
            _sound.SetVolume(channel, volume, 0, null);
            if (notify != 0)
            {
                Report(new SoundReport(channel, generation, true, 0, notify));
            }

            return;
        }

        _sound.SetVolume(channel, volume, duration, notify == 0 ? null : () => Report(new SoundReport(channel, generation, true, 0, notify)));
    }

    /// <summary>
    /// [glk op:sound_load_hint] Passes a loading hint on for a sound
    /// that exists.
    /// </summary>
    public void SoundLoadHint(uint number, bool load)
    {
        if (Resources?.Find(ResourceUsage.Sound, (int)number) is { } resource)
        {
            _sound.LoadHint(resource, load);
        }
    }

    /// <summary>
    /// The next sound or volume notification event, if a report is
    /// waiting that still belongs to its channel's current sound or
    /// volume change, or null for none.
    /// </summary>
    private GlkEvent? NextSoundEvent()
    {
        while (_soundReports.TryDequeue(out var report))
        {
            var channel = report.Channel;
            if (SoundChannels.Find(channel.Id) != channel)
            {
                continue;
            }

            if (report.IsVolume)
            {
                if (report.Generation == channel.VolumeGeneration)
                {
                    // [glk op:schannel_set_volume_ext] No window, zero,
                    // and the notify value.
                    return new GlkEvent(EventType.VolumeNotify, null, 0, report.Notify);
                }

                continue;
            }

            if (report.Generation != channel.Generation)
            {
                continue;
            }

            // The sound ended by itself, so the channel is idle, whether
            // or not the game asked to hear of it.
            channel.Playing = null;
            channel.Notify = 0;
            if (report.Notify != 0)
            {
                // [glk op:schannel_play_ext] No window, the resource
                // number, and the notify value.
                return new GlkEvent(EventType.SoundNotify, null, report.Sound, report.Notify);
            }
        }

        return null;
    }

    private void Report(SoundReport report)
    {
        _soundReports.Enqueue(report);
        _display.Wake();
    }

    // The channel's sound, if any, is no longer its current one: any
    // report of its end is dropped.
    private static void Forget(GlkSoundChannel channel)
    {
        channel.Generation++;
        channel.Playing = null;
        channel.Notify = 0;
    }

    private GlkSoundChannel? SoundChannel(GlkCall call, int index)
    {
        var id = call.ObjectId(index);
        var channel = SoundChannels.Find(id);
        if (channel is null)
        {
            Warn($"{call.Function.Name}: invalid sound channel id {id}.");
        }

        return channel;
    }

    /// <summary>
    /// What a frontend reported: the end of a sound, or of a volume
    /// change, on a channel, and which one by its generation.
    /// </summary>
    private sealed record SoundReport(GlkSoundChannel Channel, uint Generation, bool IsVolume, uint Sound, uint Notify);
}

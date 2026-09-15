using Rezrov.Core.Blorb;
using Rezrov.Glulx.Glk;

namespace Rezrov.Tests;

/// <summary>
/// A Glk sound frontend for tests: records every call, declines the
/// formats it is told to, and lets a test end a sound or a volume
/// change whenever it likes, including one the library has since
/// stopped, as a frontend running late might.
/// </summary>
internal sealed class RecordingGlkSound : IGlkSound
{
    public bool CanPlaySounds { get; init; } = true;

    /// <summary>
    /// Chunk types the frontend cannot play, such as "MOD ".
    /// </summary>
    public HashSet<string> Unplayable { get; } = [];

    /// <summary>
    /// Every call, as "Play 1 3 r1 v10000", "Stop 1", "Volume 1 8000
    /// over 0", and so on.
    /// </summary>
    public List<string> Calls { get; } = [];

    /// <summary>
    /// The end callback of every sound started, by channel, in order.
    /// </summary>
    public List<(uint Channel, Action Ended)> Started { get; } = [];

    /// <summary>
    /// The completion callback of every gradual volume change, by
    /// channel, in order.
    /// </summary>
    public List<(uint Channel, Action Changed)> Fades { get; } = [];

    public bool Play(GlkSoundChannel channel, BlorbResource sound, uint repeats, Action ended)
    {
        var count = repeats == uint.MaxValue ? "forever" : $"r{repeats}";
        Calls.Add($"Play {channel.Id} {sound.Number} {count} v{channel.Volume:X}{(channel.Paused ? " paused" : "")}");
        if (Unplayable.Contains(sound.ChunkType))
        {
            return false;
        }

        Started.Add((channel.Id, ended));
        return true;
    }

    public void StopPlaying(GlkSoundChannel channel) => Calls.Add($"Stop {channel.Id}");

    public void Pause(GlkSoundChannel channel) => Calls.Add($"Pause {channel.Id}");

    public void Unpause(GlkSoundChannel channel) => Calls.Add($"Unpause {channel.Id}");

    public void SetVolume(GlkSoundChannel channel, uint volume, uint duration, Action? changed)
    {
        Calls.Add($"Volume {channel.Id} {volume:X} over {duration}");
        if (changed is not null)
        {
            Fades.Add((channel.Id, changed));
        }
    }

    public void LoadHint(BlorbResource sound, bool load) => Calls.Add($"{(load ? "Load" : "Unload")} {sound.Number}");

    public void Close(GlkSoundChannel channel) => Calls.Add($"Close {channel.Id}");

    /// <summary>
    /// Reports that the last sound started on a channel ended.
    /// </summary>
    public void EndSound(uint channel) => Started.Last(s => s.Channel == channel).Ended();

    /// <summary>
    /// Reports that the last volume change on a channel completed.
    /// </summary>
    public void EndFade(uint channel) => Fades.Last(f => f.Channel == channel).Changed();
}

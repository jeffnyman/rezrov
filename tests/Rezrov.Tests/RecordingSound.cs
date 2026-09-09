using Rezrov.ZMachine.Sound;

namespace Rezrov.Tests;

/// <summary>
/// A sound frontend for tests: records every call, and lets a test end
/// a cycle of a playing sound whenever it likes.
/// </summary>
internal sealed class RecordingSound : ISound
{
    private readonly Dictionary<int, Action> _cycleEnded = [];

    public bool CanPlaySounds { get; init; } = true;

    /// <summary>
    /// Every call, as "Bleep 1", "Play 3 v8 r1", "Stop 3", and so on.
    /// </summary>
    public List<string> Calls { get; } = [];

    public void Bleep(int number) => Calls.Add($"Bleep {number}");

    public void Prepare(SoundResource sound) => Calls.Add($"Prepare {sound.Number}");

    public void Play(SoundResource sound, int volume, int repeats, Action cycleEnded)
    {
        Calls.Add($"Play {sound.Number} v{volume} r{repeats}");
        _cycleEnded[sound.Number] = cycleEnded;
    }

    public void StopPlaying(SoundResource sound)
    {
        Calls.Add($"Stop {sound.Number}");
        _cycleEnded.Remove(sound.Number);
    }

    public void Finish(SoundResource sound) => Calls.Add($"Finish {sound.Number}");

    /// <summary>Reports that one play of a sound has ended.</summary>
    public void EndCycle(int number)
    {
        if (_cycleEnded.TryGetValue(number, out var callback))
        {
            callback();
        }
    }
}

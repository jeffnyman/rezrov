using Rezrov.ZMachine.Sound;

namespace Rezrov.Cli;

/// <summary>
/// The console's idea of sound: the terminal bell for a bleep, and
/// nothing else.
/// </summary>
/// <remarks>
/// [zm 9.1] A bleep is the one sound every game may assume, and the
/// bell character is how a terminal makes it. Sampled sounds and music
/// need an audio device, which a text stream does not have, so
/// <see cref="CanPlaySounds"/> is false and the game is told so through
/// the header.
/// </remarks>
internal sealed class ConsoleSound : ISound
{
    public bool CanPlaySounds => false;

    public void Bleep(int number)
    {
        Console.Out.Write((char)0x07);
        Console.Out.Flush();
    }

    public void Prepare(SoundResource sound)
    {
    }

    public void Play(SoundResource sound, int volume, int repeats, Action cycleEnded)
    {
    }

    public void StopPlaying(SoundResource sound)
    {
    }

    public void Finish(SoundResource sound)
    {
    }
}

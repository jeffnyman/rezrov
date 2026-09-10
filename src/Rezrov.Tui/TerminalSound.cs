using Rezrov.ZMachine.Sound;

namespace Rezrov.Tui;

/// <summary>
/// Sound on a terminal: a bleep where the platform can make one, and
/// nothing more yet.
/// </summary>
/// <remarks>
/// [zm 9.1] A bleep is the one sound every game may assume. Sampled
/// sounds and music need an audio device and a decoder for the Blorb
/// formats, which is a frontend piece still to come, so
/// <see cref="CanPlaySounds"/> is false and the game is told so.
/// </remarks>
public sealed class TerminalSound : ISound
{
    public bool CanPlaySounds => false;

    public void Bleep(int number)
    {
        // [zm 9.2] High for 1, low for 2. Only Windows has a console
        // beep with a pitch; elsewhere the terminal bell would have to
        // go through the driver, and silence is the honest default.
        if (OperatingSystem.IsWindows())
        {
            Console.Beep(number == 2 ? 440 : 880, 120);
        }
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

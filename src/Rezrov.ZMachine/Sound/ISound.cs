namespace Rezrov.ZMachine.Sound;

/// <summary>
/// What a frontend provides for sound: a bleep, and the playing of
/// sound resources in the background.
/// </summary>
/// <remarks>
/// [zm 9.1] The interpreter is expected to know where sound effects
/// come from, and [zm 9.4] they play in the background while the
/// machine goes on working. The rules of section 9, which sound may
/// play at once, when the game's end-of-sound routine runs, and the
/// queueing The Lurking Horror needs, live in <see cref="SoundModel"/>;
/// what is left for a frontend is to make noise. A frontend with no
/// audio says so through <see cref="CanPlaySounds"/>, which the header
/// passes on to the game, and need only bleep, or not even that.
///
/// A frontend that plays sounds calls the callback it is given at the
/// end of every cycle of a sound, on whatever thread it likes; the
/// model takes it from there at a safe point in the interpreter.
/// </remarks>
public interface ISound
{
    /// <summary>
    /// [zm 9.1.2] Whether sounds beyond a bleep can be played. The
    /// interpreter clears bit 7 of Flags 2 when they cannot.
    /// </summary>
    bool CanPlaySounds { get; }

    /// <summary>
    /// [zm 9.2] A bleep: 1 is high-pitched, 2 low-pitched.
    /// </summary>
    void Bleep(int number);

    /// <summary>
    /// [zm 9.4.1] The game intends to use this sound soon; it may be
    /// loaded and decoded ahead of time.
    /// </summary>
    void Prepare(SoundResource sound);

    /// <summary>
    /// [zm 9.4.2] Starts a sound in the background.
    /// </summary>
    /// <param name="sound">The sound.</param>
    /// <param name="volume">[zm 9.3] From 1 to 8, 8 being loudest.</param>
    /// <param name="repeats">
    /// [zm 9.4.3] How many times to play it in all, or -1 forever.
    /// </param>
    /// <param name="cycleEnded">
    /// To be called each time one play of the sound ends, the last
    /// included, from any thread.
    /// </param>
    void Play(SoundResource sound, int volume, int repeats, Action cycleEnded);

    /// <summary>[zm 9.4.2] Stops a sound if it is playing.</summary>
    void StopPlaying(SoundResource sound);

    /// <summary>
    /// [zm 9.4.5] The game is done with this sound for a while; it may
    /// be thrown out of memory.
    /// </summary>
    void Finish(SoundResource sound);
}

/// <summary>
/// A frontend with no audio at all: no sounds, and a bleep that goes
/// nowhere.
/// </summary>
public sealed class NoSound : ISound
{
    public static NoSound Instance { get; } = new();

    public bool CanPlaySounds => false;

    public void Bleep(int number)
    {
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

namespace Rezrov.Core.Audio;

/// <summary>
/// One decoded sound: its samples, how many channels they are in, and
/// how many of them make a second.
/// </summary>
/// <remarks>
/// [blorb 3] and [blorb 16] AIFF is the sampled sound format of a
/// resource file, and the resource is the whole IFF form. Decoding
/// turns it into the one shape a mixer wants, which is samples from
/// minus one to one, whatever width they were stored at.
/// </remarks>
/// <param name="Samples">
/// The samples, from minus one to one, with the channels interleaved,
/// so a stereo sound is left, right, left, right.
/// </param>
/// <param name="Channels">
/// How many channels are interleaved, 1 or 2.
/// </param>
/// <param name="SampleRate">Samples of one channel per second.</param>
public sealed record AiffSound(float[] Samples, int Channels, double SampleRate)
{
    /// <summary>How many samples of one channel there are.</summary>
    public int Frames => Samples.Length / Channels;

    /// <summary>How long the sound lasts.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds(Frames / SampleRate);

    /// <summary>
    /// One channel's sample at a frame, or silence past the end.
    /// </summary>
    public float Frame(int frame, int channel)
    {
        if (frame < 0 || frame >= Frames)
        {
            return 0;
        }

        // A sound with fewer channels than were asked for is heard on
        // every one of them: a mono sound plays in both ears.
        return Samples[(frame * Channels) + (channel % Channels)];
    }
}

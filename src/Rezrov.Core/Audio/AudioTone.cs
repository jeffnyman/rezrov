namespace Rezrov.Core.Audio;

/// <summary>
/// Makes the one sound no resource file carries: a bleep.
/// </summary>
/// <remarks>
/// [zm 9.2] The two bleeps are the only sounds a game may assume, and
/// they are not resources: the interpreter makes them. A sine wave is
/// as plain a tone as there is, and fading it in and out over a few
/// milliseconds keeps it from starting and ending with the click that
/// a waveform cut off mid-swing makes.
/// </remarks>
public static class AudioTone
{
    /// <summary>A tone as a sound a mixer can play.</summary>
    /// <param name="frequency">Cycles a second.</param>
    /// <param name="duration">How long it lasts.</param>
    /// <param name="sampleRate">Samples a second to make it at.</param>
    public static AiffSound Sine(double frequency, TimeSpan duration, int sampleRate = 44100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frequency);
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);

        var frames = Math.Max((int)(duration.TotalSeconds * sampleRate), 1);
        var samples = new float[frames];
        var edge = Math.Min(sampleRate / 200, frames / 2);

        for (var i = 0; i < frames; i++)
        {
            var value = Math.Sin(2 * Math.PI * frequency * i / sampleRate);

            // The first and last few milliseconds rise and fall, so the
            // tone begins and ends at silence.
            if (edge > 0)
            {
                var into = Math.Min(i, frames - 1 - i);
                if (into < edge)
                {
                    value *= (double)into / edge;
                }
            }

            samples[i] = (float)value;
        }

        return new AiffSound(samples, 1, sampleRate);
    }
}

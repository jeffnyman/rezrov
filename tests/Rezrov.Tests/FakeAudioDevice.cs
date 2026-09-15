using Rezrov.Core.Audio;

namespace Rezrov.Tests;

/// <summary>
/// An audio device for tests: it never plays anything, and asks for
/// samples only when a test says to, so a test of a frontend is as
/// exact as a test of the mixer.
/// </summary>
internal sealed class FakeAudioDevice : IAudioDevice
{
    private AudioFill? _fill;

    public FakeAudioDevice(int sampleRate = 8, int channels = 1)
    {
        SampleRate = sampleRate;
        Channels = channels;
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public bool IsDisposed { get; private set; }

    public void Start(AudioFill fill) => _fill = fill;

    /// <summary>
    /// The next frames of sound, as the device would take them.
    /// </summary>
    public short[] Pull(int frames)
    {
        var buffer = new short[frames * Channels];
        _fill?.Invoke(buffer);
        return buffer;
    }

    public void Dispose() => IsDisposed = true;
}

/// <summary>Small AIFF files for tests.</summary>
internal static class TestAiff
{
    /// <summary>
    /// A mono file of 8-bit samples, given as the signed bytes they are
    /// stored as.
    /// </summary>
    public static byte[] Mono(int rate, params sbyte[] samples)
    {
        var comm = new List<byte> { 0, 1 };
        Word(comm, samples.Length);
        comm.AddRange([0, 8]);

        // The rate as an 80-bit extended number: two to the fourteen is
        // 16384, so a rate under that has an exponent no higher than
        // the bias, and the significand carries the rest.
        var exponent = 0;
        var significand = (double)rate;
        while (significand >= 1)
        {
            significand /= 2;
            exponent++;
        }

        comm.AddRange([(byte)((exponent + 16382) >> 8), (byte)(exponent + 16382)]);
        var bits = (ulong)(significand * 2 * Math.Pow(2, 63));
        for (var i = 7; i >= 0; i--)
        {
            comm.Add((byte)(bits >> (i * 8)));
        }

        var ssnd = new List<byte> { 0, 0, 0, 0, 0, 0, 0, 0 };
        ssnd.AddRange(samples.Select(s => (byte)s));

        var body = new List<byte>("AIFF"u8.ToArray());
        Chunk(body, "COMM", comm.ToArray());
        Chunk(body, "SSND", ssnd.ToArray());

        var file = new List<byte>("FORM"u8.ToArray());
        Word(file, body.Count);
        file.AddRange(body);
        return file.ToArray();
    }

    private static void Word(List<byte> bytes, int value) =>
        bytes.AddRange([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);

    private static void Chunk(List<byte> output, string id, byte[] data)
    {
        output.AddRange(System.Text.Encoding.ASCII.GetBytes(id));
        Word(output, data.Length);
        output.AddRange(data);
        if ((data.Length & 1) == 1)
        {
            output.Add(0);
        }
    }
}

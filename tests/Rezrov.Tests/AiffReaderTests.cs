using System.Buffers.Binary;
using System.Text;
using Rezrov.Core.Audio;
using Rezrov.Core.Blorb;

namespace Rezrov.Tests;

/// <summary>
/// [blorb 3] Reading the sampled sounds of a resource file: the format
/// chunk, the samples at each width, the byte order the later form
/// names, and the files this cannot play.
/// </summary>
public class AiffReaderTests
{
    [Fact]
    public void EightBitSamplesAreSignedBytes()
    {
        var sound = AiffReader.Read(Aiff(channels: 1, bits: 8, frames: 4, rate: 11025, samples: [0, 127, 0x80, 0xC0]))!;

        // A signed byte over 128: silence, the top, the bottom, and
        // halfway down.
        Assert.Equal(1, sound.Channels);
        Assert.Equal(11025, sound.SampleRate);
        Assert.Equal(4, sound.Frames);
        Assert.Equal([0f, 127 / 128f, -1f, -0.5f], sound.Samples);
    }

    [Fact]
    public void SixteenBitSamplesAreBigEndianUnlessTheFormSaysOtherwise()
    {
        var big = AiffReader.Read(Aiff(channels: 1, bits: 16, frames: 2, rate: 22050, samples: [0x40, 0x00, 0xC0, 0x00]))!;
        Assert.Equal([0.5f, -0.5f], big.Samples);

        // [blorb 3] The later AIFC form names a compression, and sowt is
        // the same samples the other way round.
        var little = AiffReader.Read(Aiff(channels: 1, bits: 16, frames: 2, rate: 22050, samples: [0x00, 0x40, 0x00, 0xC0], compression: "sowt"))!;
        Assert.Equal([0.5f, -0.5f], little.Samples);

        var plain = AiffReader.Read(Aiff(channels: 1, bits: 16, frames: 2, rate: 22050, samples: [0x40, 0x00, 0xC0, 0x00], compression: "NONE"))!;
        Assert.Equal([0.5f, -0.5f], plain.Samples);
    }

    [Fact]
    public void ACompressedSoundIsRefused()
    {
        // Nothing here decompresses, so the frontend is told it cannot
        // play this one rather than playing noise.
        Assert.Null(AiffReader.Read(Aiff(channels: 1, bits: 16, frames: 2, rate: 22050, samples: [1, 2, 3, 4], compression: "QDMC")));
    }

    [Fact]
    public void StereoSamplesStayInterleavedAndBothChannelsAreHeard()
    {
        var sound = AiffReader.Read(Aiff(channels: 2, bits: 8, frames: 2, rate: 8000, samples: [0x40, 0xC0, 0x20, 0xE0]))!;

        Assert.Equal(2, sound.Channels);
        Assert.Equal(2, sound.Frames);
        Assert.Equal(0.5f, sound.Frame(0, 0));
        Assert.Equal(-0.5f, sound.Frame(0, 1));
        Assert.Equal(0.25f, sound.Frame(1, 0));

        // Past the end there is silence, and a mono sound is heard on
        // whatever channel asks for it.
        Assert.Equal(0f, sound.Frame(2, 0));
        var mono = AiffReader.Read(Aiff(channels: 1, bits: 8, frames: 1, rate: 8000, samples: [0x40]))!;
        Assert.Equal(0.5f, mono.Frame(0, 1));
    }

    [Fact]
    public void OtherChunksAreSteppedOverAndTheSampleOffsetIsHonored()
    {
        // The markers and instrument settings a tracker writes, an odd
        // length that pads, and dead space before the samples.
        var extra = new (string Id, byte[] Body)[] { ("MARK", [1, 2, 3]), ("INST", [4]) };
        var sound = AiffReader.Read(Aiff(channels: 1, bits: 8, frames: 2, rate: 8000, samples: [0x40, 0xC0], offset: 2, others: extra))!;

        Assert.Equal([0.5f, -0.5f], sound.Samples);
    }

    [Fact]
    public void ASoundWhoseSamplesRunOutEarlyPlaysAsFarAsItGoes()
    {
        // The format chunk claims eight frames and only three arrived.
        var file = Aiff(channels: 1, bits: 8, frames: 8, rate: 8000, samples: [0x40, 0x40, 0x40]);
        var sound = AiffReader.Read(file)!;

        Assert.Equal(3, sound.Frames);
        Assert.Equal(TimeSpan.FromSeconds(3 / 8000.0), sound.Duration);
    }

    [Fact]
    public void WhatIsNotAnAiffAtAllIsRefused()
    {
        Assert.Null(AiffReader.Read([]));
        Assert.Null(AiffReader.Read("not a form at all"u8));
        Assert.Null(AiffReader.Read([.. "FORM"u8, 0, 0, 0, 4, .. "ILBM"u8]));

        // A form with a format chunk and no samples, and one with
        // samples and no format chunk.
        Assert.Null(AiffReader.Read(Aiff(channels: 1, bits: 8, frames: 2, rate: 8000, samples: [])));
        Assert.Null(AiffReader.Read([.. "FORM"u8, 0, 0, 0, 20, .. "AIFF"u8, .. "SSND"u8, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0]));
    }

    [Fact]
    public void EverySoundInTheCorpusDecodes()
    {
        var files = Corpus.BlorbFiles();
        Assert.SkipUnless(files.Count > 0, "The entharion submodule is not populated.");

        var sounds = 0;
        foreach (var path in files)
        {
            var blorb = BlorbFile.Read(File.ReadAllBytes(path));
            foreach (var resource in blorb.Resources.Where(r => r.Usage == ResourceUsage.Sound))
            {
                var sound = AiffReader.Read(resource.Data.Span);
                Assert.NotNull(sound);

                // These were recorded at whatever rate the machine of
                // the day managed, from three thousand samples a second
                // upward, all well inside what a device will take.
                Assert.InRange(sound.SampleRate, 1000, 192000);
                Assert.InRange(sound.Channels, 1, 2);
                Assert.True(sound.Frames > 0);
                sounds++;
            }
        }

        Assert.True(sounds > 0);
    }

    /// <summary>
    /// An AIFF, or an AIFC when a compression is named, with a COMM
    /// chunk, any other chunks asked for, and an SSND chunk holding the
    /// sample bytes as given.
    /// </summary>
    private static byte[] Aiff(int channels, int bits, int frames, double rate, byte[] samples, string? compression = null, uint offset = 0, (string Id, byte[] Body)[]? others = null)
    {
        var body = new List<byte>();
        body.AddRange(Encoding.ASCII.GetBytes(compression is null ? "AIFF" : "AIFC"));

        var comm = new List<byte>();
        Short(comm, channels);
        Word(comm, frames);
        Short(comm, bits);
        comm.AddRange(Extended(rate));
        if (compression is not null)
        {
            comm.AddRange(Encoding.ASCII.GetBytes(compression));
            comm.AddRange([0, 0]);
        }

        Chunk(body, "COMM", comm.ToArray());

        foreach (var (id, bytes) in others ?? [])
        {
            Chunk(body, id, bytes);
        }

        if (samples.Length > 0 || offset > 0)
        {
            var ssnd = new List<byte>();
            Word(ssnd, (int)offset);
            Word(ssnd, 0);
            ssnd.AddRange(new byte[offset]);
            ssnd.AddRange(samples);
            Chunk(body, "SSND", ssnd.ToArray());
        }

        var file = new List<byte>();
        file.AddRange("FORM"u8.ToArray());
        Word(file, body.Count);
        file.AddRange(body);
        return file.ToArray();
    }

    private static void Short(List<byte> bytes, int value) =>
        bytes.AddRange([(byte)(value >> 8), (byte)value]);

    private static void Word(List<byte> bytes, int value) =>
        bytes.AddRange([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);

    private static void Chunk(List<byte> output, string id, byte[] data)
    {
        output.AddRange(Encoding.ASCII.GetBytes(id));
        Word(output, data.Length);
        output.AddRange(data);
        if ((data.Length & 1) == 1)
        {
            output.Add(0);
        }
    }

    /// <summary>
    /// A sample rate as the 80-bit extended number AIFF stores it.
    /// </summary>
    private static byte[] Extended(double value)
    {
        var exponent = 0;
        var significand = value;
        while (significand >= 1)
        {
            significand /= 2;
            exponent++;
        }

        var bytes = new byte[10];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)(exponent + 16383 - 1));
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(2), (ulong)(significand * 2 * Math.Pow(2, 63)));
        return bytes;
    }
}

using Rezrov.Core.Audio;

namespace Rezrov.Tests;

/// <summary>
/// The mixer on its own: what comes out for a sound, how repeats,
/// volume, pausing, and fades change it, when a play is reported as
/// ended, and what a sound recorded at another rate sounds like.
/// </summary>
/// <remarks>
/// Time passes only as samples are asked for, so every test here is
/// exact: no device, no clock, and no waiting.
/// </remarks>
public class AudioMixerTests
{
    /// <summary>
    /// A sound of the given samples, at a rate a test states in samples
    /// a second so that the arithmetic stays small.
    /// </summary>
    private static AiffSound Sound(double rate, params float[] samples) => new(samples, 1, rate);

    private static short[] Fill(AudioMixer mixer, int frames)
    {
        var buffer = new short[frames * mixer.Channels];
        mixer.Fill(buffer);
        return buffer;
    }

    [Fact]
    public void NothingPlayingIsSilence()
    {
        var mixer = new AudioMixer(8, 1);

        Assert.Equal(new short[4], Fill(mixer, 4));
        Assert.Equal(0, mixer.VoiceCount);
        Assert.False(mixer.IsSounding);
    }

    [Fact]
    public void ASoundAtTheDeviceRateComesOutSampleForSample()
    {
        var mixer = new AudioMixer(8, 1);
        var ended = 0;
        var voice = mixer.Play(Sound(8, 1f, 0.5f, 0f, -1f), cycleEnded: () => ended++);

        Assert.True(mixer.IsSounding);
        Assert.Equal([32767, 16384, 0, -32767], Fill(mixer, 4));

        // One play, so the end of the first cycle is the end of the
        // sound: reported once, and the voice is gone.
        Assert.Equal(1, ended);
        Assert.True(voice.HasEnded);
        Assert.Equal(new short[4], Fill(mixer, 4));
        Assert.Equal(0, mixer.VoiceCount);
    }

    [Fact]
    public void VolumeScalesTheSamplesAndTwoLoudSoundsClip()
    {
        var mixer = new AudioMixer(8, 1);
        mixer.Play(Sound(8, 1f, 0.5f, 0f, -1f), volume: 0.5f);

        Assert.Equal([16384, 8192, 0, -16384], Fill(mixer, 4));

        // Two voices at once are summed, and a sum past the end of the
        // range is as loud as the device goes.
        mixer.Play(Sound(8, 1f, 1f), volume: 1f);
        mixer.Play(Sound(8, 1f, 1f), volume: 1f);
        Assert.Equal([32767, 32767], Fill(mixer, 2));
    }

    [Fact]
    public void ASoundRepeatsAndReportsEachPlay()
    {
        var mixer = new AudioMixer(8, 1);
        var ended = 0;
        var voice = mixer.Play(Sound(8, 1f, -1f), repeats: 3, cycleEnded: () => ended++);

        Assert.Equal([32767, -32767, 32767, -32767], Fill(mixer, 4));
        Assert.Equal(2, ended);
        Assert.False(voice.HasEnded);

        Assert.Equal([32767, -32767], Fill(mixer, 2));
        Assert.Equal(3, ended);
        Assert.True(voice.HasEnded);
    }

    [Fact]
    public void ASoundRepeatingForeverNeverEnds()
    {
        var mixer = new AudioMixer(8, 1);
        var ended = 0;
        var voice = mixer.Play(Sound(8, 1f, -1f), repeats: -1, cycleEnded: () => ended++);

        Fill(mixer, 100);

        Assert.Equal(50, ended);
        Assert.False(voice.HasEnded);
        Assert.Equal(1, mixer.VoiceCount);
    }

    [Fact]
    public void AStoppedSoundGoesQuietAndReportsNothing()
    {
        var mixer = new AudioMixer(8, 1);
        var ended = 0;
        var voice = mixer.Play(Sound(8, 1f, 1f, 1f, 1f), cycleEnded: () => ended++);

        Assert.Equal([32767, 32767], Fill(mixer, 2));
        voice.Stop();

        // A sound stopped by hand did not end by itself, so the end is
        // never reported.
        Assert.Equal(new short[2], Fill(mixer, 2));
        Assert.Equal(0, ended);
        Assert.Equal(0, mixer.VoiceCount);
    }

    [Fact]
    public void APausedSoundIsHeldWhereItIs()
    {
        var mixer = new AudioMixer(8, 1);
        var voice = mixer.Play(Sound(8, 1f, 0.5f, -1f), paused: true);

        // Paused from the start: silence, and nothing moves.
        Assert.True(voice.Paused);
        Assert.False(mixer.IsSounding);
        Assert.Equal(new short[2], Fill(mixer, 2));

        voice.Resume();
        Assert.Equal([32767, 16384], Fill(mixer, 2));

        voice.Pause();
        Assert.Equal(new short[2], Fill(mixer, 2));

        // And it goes on from where it was held.
        voice.Resume();
        Assert.Equal([-32767], Fill(mixer, 1));
    }

    [Fact]
    public void AVolumeSlidesToItsNewLevelAndSaysWhenItArrives()
    {
        var mixer = new AudioMixer(8, 1);
        var reached = 0;
        var voice = mixer.Play(Sound(8, 1f, 1f), repeats: -1);

        // Half a second at eight samples a second is four steps down
        // from full volume to silence.
        voice.SetVolume(0, TimeSpan.FromSeconds(0.5), () => reached++);
        var samples = Fill(mixer, 4);

        // Each frame is quieter than the one before, the first at the
        // volume the slide began from, and the fourth arrives.
        Assert.True(samples[0] > samples[1] && samples[1] > samples[2] && samples[2] > samples[3]);
        Assert.Equal(32767, samples[0]);
        Assert.Equal(0f, voice.Volume);
        Assert.Equal(1, reached);

        // Silence from there on, and the arrival is reported once only.
        Assert.Equal(new short[4], Fill(mixer, 4));
        Assert.Equal(1, reached);
    }

    [Fact]
    public void AVolumeChangeWithNoTimeHappensAtOnce()
    {
        var mixer = new AudioMixer(8, 1);
        var reached = 0;
        var voice = mixer.Play(Sound(8, 1f, 1f), repeats: -1);

        voice.SetVolume(0.5f, TimeSpan.Zero, () => reached++);

        Assert.Equal(0.5f, voice.Volume);
        Assert.Equal(1, reached);
        Assert.Equal([16384, 16384], Fill(mixer, 2));
    }

    [Fact]
    public void AVolumeSlideThatAnotherInterruptsNeverArrives()
    {
        var mixer = new AudioMixer(8, 1);
        var first = 0;
        var second = 0;
        var voice = mixer.Play(Sound(8, 1f, 1f), repeats: -1);

        voice.SetVolume(0, TimeSpan.FromSeconds(1), () => first++);
        Fill(mixer, 2);
        voice.SetVolume(1, TimeSpan.FromSeconds(0.5), () => second++);
        Fill(mixer, 8);

        Assert.Equal(0, first);
        Assert.Equal(1, second);
        Assert.Equal(1f, voice.Volume);
    }

    [Fact]
    public void ASoundRecordedSlowerThanTheDeviceIsStretchedOverMoreSamples()
    {
        var mixer = new AudioMixer(8, 1);
        var ended = 0;

        // Four samples recorded at half the device's rate last eight of
        // the device's, with the samples between drawn on a straight
        // line from one to the next.
        mixer.Play(Sound(4, 0f, 1f, 0f, -1f), cycleEnded: () => ended++);
        var samples = Fill(mixer, 8);

        Assert.Equal([0, 16384, 32767, 16384, 0, -16384, -32767, -16384], samples);
        Assert.Equal(1, ended);
    }

    [Fact]
    public void AMonoSoundIsHeardOnBothChannels()
    {
        var mixer = new AudioMixer(8, 2);
        mixer.Play(Sound(8, 1f, -1f));

        // Two samples a frame, the same in each.
        Assert.Equal([32767, 32767, -32767, -32767], Fill(mixer, 2));
    }

    [Fact]
    public void StoppingEverythingSilencesEveryVoice()
    {
        var mixer = new AudioMixer(8, 1);
        var ended = 0;
        mixer.Play(Sound(8, 1f, 1f), repeats: -1, cycleEnded: () => ended++);
        mixer.Play(Sound(8, 1f, 1f), repeats: -1, cycleEnded: () => ended++);

        mixer.StopAll();

        Assert.Equal(0, mixer.VoiceCount);
        Assert.Equal(new short[4], Fill(mixer, 4));
        Assert.Equal(0, ended);
    }

    [Fact]
    public void ABleepIsAToneThatStartsAndEndsAtSilence()
    {
        // [zm 9.2] The bleeps are made rather than read from anywhere.
        var tone = AudioTone.Sine(440, TimeSpan.FromMilliseconds(100), 8000);

        Assert.Equal(800, tone.Frames);
        Assert.Equal(1, tone.Channels);
        Assert.Equal(0f, tone.Samples[0]);
        Assert.True(Math.Abs(tone.Samples[^1]) < 0.05f);
        Assert.True(tone.Samples.Max() > 0.9f);
        Assert.True(tone.Samples.Min() < -0.9f);
    }
}

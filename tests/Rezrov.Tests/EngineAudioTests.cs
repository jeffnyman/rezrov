using Rezrov.Core.Audio;
using Rezrov.Core.Blorb;
using Rezrov.Glulx.Glk;
using Rezrov.Tui;
using Rezrov.ZMachine.Sound;

namespace Rezrov.Tests;

/// <summary>
/// The terminal's two sound frontends over one audio engine: the
/// Z-machine's sound effects and Glk's sound channels, each playing
/// the same kind of resource through the same mixer.
/// </summary>
public class EngineAudioTests
{
    // The loudest an 8-bit sample goes each way, which is one step
    // short of the top of the range going up and exactly the bottom
    // going down.
    private static readonly byte[] Loud = TestAiff.Mono(8, 127, -128, 127, -128);

    // [blorb 16] A sound in a resource file is the whole form, which is
    // what the test builder writes when the type is FORM and the bytes
    // begin with the form's own kind.
    private static readonly byte[] Resource = Loud[8..];

    private static (AudioEngine Engine, FakeAudioDevice Device) Engine()
    {
        var device = new FakeAudioDevice();
        return (new AudioEngine(device), device);
    }

    [Fact]
    public void WithNoAudioOutputNeitherFrontendCanPlay()
    {
        var zmachine = new EngineSound();
        var glk = new EngineGlkSound();

        // [zm 9.1.2] and [glk #sound_testing] Both say so, and the
        // bleeps still work, or are silently skipped.
        Assert.False(zmachine.CanPlaySounds);
        Assert.False(glk.CanPlaySounds);
        zmachine.Bleep(1);

        var sound = new SoundResource(3, "AIFF", Loud);
        zmachine.Play(sound, 8, 1, () => Assert.Fail("Nothing can have played."));
        zmachine.StopPlaying(sound);
        zmachine.Finish(sound);
    }

    [Fact]
    public void AZMachineSoundPlaysAtTheVolumeTheGameAsked()
    {
        var (engine, device) = Engine();
        var frontend = new EngineSound(engine);
        var sound = new SoundResource(3, "AIFF", Loud);
        var ended = 0;

        Assert.True(frontend.CanPlaySounds);
        frontend.Prepare(sound);
        frontend.Play(sound, 8, 1, () => ended++);

        // [zm 9.3] Volume 8 is the sound as recorded, so the loudest
        // samples come out at the top and bottom of the range.
        Assert.Equal([32511, -32767, 32511, -32767], device.Pull(4));
        Assert.Equal(1, ended);

        // Half the volume, half the swing.
        frontend.Play(sound, 4, 1, () => ended++);
        var half = device.Pull(1);
        Assert.InRange(half[0], 16000, 16600);

        // [zm op:sound_effect] Stopped by hand: quiet, and the game is
        // never told it ended.
        frontend.StopPlaying(sound);
        Assert.Equal(new short[4], device.Pull(4));
        Assert.Equal(1, ended);

        engine.Dispose();
        Assert.True(device.IsDisposed);
    }

    [Fact]
    public void AZMachineSoundRepeatsAndIsForgottenWhenFinished()
    {
        var (engine, device) = Engine();
        var frontend = new EngineSound(engine);
        var sound = new SoundResource(3, "AIFF", Loud);
        var cycles = 0;

        // [zm 9.4.3] Twice over, and [zm 9.4.4] each cycle reported, so
        // the model can call the game's routine after the last.
        frontend.Play(sound, 8, 2, () => cycles++);
        device.Pull(4);
        Assert.Equal(1, cycles);
        device.Pull(4);
        Assert.Equal(2, cycles);
        Assert.Equal(new short[4], device.Pull(4));

        // [zm 9.4.5] Finishing with it drops what was decoded, and the
        // next play reads it again.
        frontend.Finish(sound);
        frontend.Play(sound, 8, 1, () => cycles++);
        Assert.Equal([32511, -32767], device.Pull(2));
    }

    [Fact]
    public void AGlkChannelPlaysWithItsVolumeAndPause()
    {
        var (engine, device) = Engine();
        var frontend = new EngineGlkSound(engine);
        var glk = new GlkLibrary(new RecordingGlkDisplay(), sound: frontend)
        {
            Resources = BlorbFile.Read(TestBlorb.Build([], sounds: [(7, "FORM", Resource)])),
        };

        // [glk #sound_channels] Half volume, and the samples come out
        // halfway up the range.
        var channel = glk.CreateSoundChannel(0, GlkSoundChannel.FullVolume / 2)!;
        Assert.True(glk.PlaySound(channel, 7));
        var samples = device.Pull(2);
        Assert.InRange(samples[0], 16000, 16600);
        Assert.InRange(samples[1], -16600, -16000);

        // [glk op:schannel_pause] Held where it is, then let go.
        glk.PauseSound(channel);
        Assert.Equal(new short[2], device.Pull(2));
        glk.UnpauseSound(channel);
        Assert.NotEqual(new short[1], device.Pull(1));

        // [glk op:schannel_play] A sound the resource file does not have
        // cannot start.
        Assert.False(glk.PlaySound(channel, 99));
    }

    [Fact]
    public void AGlkChannelReportsTheEndOnlyAfterTheLastRepetition()
    {
        var (engine, device) = Engine();
        var frontend = new EngineGlkSound(engine);
        var glk = new GlkLibrary(new RecordingGlkDisplay(), sound: frontend)
        {
            Resources = BlorbFile.Read(TestBlorb.Build([], sounds: [(7, "FORM", Resource)])),
        };

        var channel = glk.CreateSoundChannel(0)!;
        glk.PlaySound(channel, 7, 2, 31);

        // [glk op:schannel_play_ext] Nothing after the first repetition.
        device.Pull(4);
        Assert.Equal(GlkEvent.None, glk.SelectPoll());

        // And after the last, the event with the sound and the value
        // the game passed.
        device.Pull(4);
        Assert.Equal(new GlkEvent(EventType.SoundNotify, null, 7, 31), glk.SelectPoll());

        // A sound repeating forever is never reported, however long it
        // plays.
        glk.PlaySound(channel, 7, uint.MaxValue, 32);
        device.Pull(40);
        Assert.Equal(GlkEvent.None, glk.SelectPoll());
        Assert.Equal(7u, channel.Playing);
    }

    [Fact]
    public void AGlkVolumeSlideOnASilentChannelFinishesAtOnce()
    {
        var (engine, _) = Engine();
        var frontend = new EngineGlkSound(engine);
        var glk = new GlkLibrary(new RecordingGlkDisplay(), sound: frontend)
        {
            Resources = BlorbFile.Read(TestBlorb.Build([], sounds: [(7, "FORM", Resource)])),
        };

        // [glk op:schannel_set_volume_ext deviates] With nothing playing
        // there are no samples to slide across, so a change that asked
        // to be told when it finished is told at once.
        var channel = glk.CreateSoundChannel(0)!;
        glk.SetSoundVolume(channel, 0, 500, 44);

        Assert.Equal(new GlkEvent(EventType.VolumeNotify, null, 0, 44), glk.SelectPoll());
        Assert.Equal(0u, channel.Volume);
    }
}

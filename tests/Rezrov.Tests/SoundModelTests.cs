using Rezrov.ZMachine;
using Rezrov.ZMachine.Sound;

namespace Rezrov.Tests;

/// <summary>
/// The sound model on its own: what plays, what interrupts what, when
/// the routine is called, and what a Version 3 game gets from the
/// looping table.
/// </summary>
public class SoundModelTests
{
    [Fact]
    public void BleepsGoStraightToTheFrontend()
    {
        var (model, sound) = Make();

        model.Bleep(1);
        model.Bleep(2);

        // [zm 9.2]
        Assert.Equal(["Bleep 1", "Bleep 2"], sound.Calls);
    }

    [Fact]
    public void PlayingASoundStopsTheOneOfItsKindAndLeavesTheOther()
    {
        var (model, sound) = Make();
        model.InputHappened();

        Assert.True(model.Play(3, 8, 1, 0));
        Assert.True(model.Play(10, 5, 2, 0));
        model.InputHappened();
        Assert.True(model.Play(4, 8, 1, 0));

        // [zm 9.4.2] and [blorb 14.3] Sample 4 replaces sample 3; the
        // music, 10, plays on.
        Assert.Equal(["Play 3 v8 r1", "Play 10 v5 r2", "Stop 3", "Play 4 v8 r1"], sound.Calls);
        Assert.Equal(4, model.PlayingSample);
        Assert.Equal(10, model.PlayingMusic);
        Assert.False(model.Play(99, 8, 1, 0));
    }

    [Fact]
    public void TheRoutineIsCalledOnlyWhenTheSoundEndsByItself()
    {
        var (model, sound) = Make();
        model.InputHappened();

        model.Play(3, 8, 2, 0x1234);
        sound.EndCycle(3);
        Assert.True(model.HasPendingEvents);
        Assert.Empty(model.Update());
        Assert.Equal(3, model.PlayingSample);

        // [zm 9.4.4] After the second and last play, the routine.
        sound.EndCycle(3);
        Assert.Equal([(ushort)0x1234], model.Update());
        Assert.Null(model.PlayingSample);

        // Stopped by hand, or by another sound: no routine.
        model.Play(3, 8, 1, 0x1234);
        model.Stop(3);
        Assert.Empty(model.Update());

        model.Play(3, 8, 1, 0x1234);
        model.InputHappened();
        model.Play(4, 8, 1, 0);
        sound.EndCycle(3);
        Assert.Empty(model.Update());
    }

    [Fact]
    public void ASoundRepeatingForeverNeverEndsByItself()
    {
        var (model, sound) = Make();

        model.Play(3, 8, -1, 0x1234);
        for (var i = 0; i < 5; i++)
        {
            sound.EndCycle(3);
        }

        // [zm 9.4.3]
        Assert.Empty(model.Update());
        Assert.Equal(3, model.PlayingSample);
    }

    [Fact]
    public void ANewSampleWaitsForOneStartedSinceTheLastInput()
    {
        // The remarks on section 9: The Lurking Horror fires several
        // samples in one turn expecting each to get its time.
        var (model, sound) = Make();
        model.InputHappened();

        model.Play(3, 8, -1, 0);
        model.Play(4, 8, 1, 0);

        Assert.Equal(["Play 3 v8 r-1"], sound.Calls);
        Assert.Equal(3, model.PlayingSample);

        sound.EndCycle(3);
        model.Update();

        Assert.Equal(["Play 3 v8 r-1", "Stop 3", "Play 4 v8 r1"], sound.Calls);
        Assert.Equal(4, model.PlayingSample);

        // Once input has happened, a new sample interrupts at once.
        model.InputHappened();
        model.Play(3, 8, 1, 0);
        Assert.Equal(3, model.PlayingSample);
    }

    [Fact]
    public void MusicIsNeverHeldBack()
    {
        // [blorb 14.3]
        var (model, sound) = Make();

        model.Play(10, 8, 1, 0);
        model.Play(11, 8, 1, 0);

        Assert.Equal(["Play 10 v8 r1", "Stop 10", "Play 11 v8 r1"], sound.Calls);
    }

    [Fact]
    public void StopFinishAndTheirAllForms()
    {
        var (model, sound) = Make();
        model.InputHappened();
        model.Play(3, 8, 1, 0);
        model.Play(10, 8, 1, 0);

        // [zm op:sound_effect] Stopping one, finishing one, then all.
        model.Stop(10);
        model.Finish(3);
        Assert.Null(model.PlayingSample);
        Assert.Null(model.PlayingMusic);

        model.Play(3, 8, 1, 0);
        model.StopAll();
        Assert.Null(model.PlayingSample);

        model.FinishAll();
        Assert.Contains("Finish 4", sound.Calls);
        Assert.Contains("Finish 10", sound.Calls);
    }

    [Fact]
    public void Version3TakesRepeatsFromTheLoopingTable()
    {
        // [blorb 11.4] The opcode cannot say, so the resource file does.
        var (model, sound) = Make(ZMachineVersion.V3);
        model.InputHappened();

        model.Play(3, 8, 7, 0);
        model.InputHappened();
        model.Play(4, 8, 7, 0);

        Assert.Equal(["Play 3 v8 r1", "Stop 3", "Play 4 v8 r-1"], sound.Calls);
    }

    [Fact]
    public void PrepareAndVolumeArePassedAlong()
    {
        var (model, sound) = Make();

        Assert.True(model.Prepare(3));
        Assert.False(model.Prepare(99));
        model.Play(3, 20, 1, 0);

        // [zm 9.4.1] and [zm 9.3] volume clamped to 8.
        Assert.Equal(["Prepare 3", "Play 3 v8 r1"], sound.Calls);
    }

    [Fact]
    public void WithoutAudioNothingPlays()
    {
        var (model, sound) = Make(sound: new RecordingSound { CanPlaySounds = false });

        Assert.True(model.Play(3, 8, 1, 0));
        Assert.Null(model.PlayingSample);
        Assert.Empty(sound.Calls);
        Assert.False(model.CanPlaySounds);
    }

    private static (SoundModel Model, RecordingSound Sound) Make(ZMachineVersion version = ZMachineVersion.V5, RecordingSound? sound = null)
    {
        sound ??= new RecordingSound();
        var model = new SoundModel(sound, version);
        model.AddResource(new SoundResource(3, "AIFF", new byte[] { 1 }, PlaysOnce: true));
        model.AddResource(new SoundResource(4, "OGGV", new byte[] { 2 }, PlaysOnce: false));
        model.AddResource(new SoundResource(10, "MOD ", new byte[] { 3 }));
        model.AddResource(new SoundResource(11, "SONG", new byte[] { 4 }));
        return (model, sound);
    }
}

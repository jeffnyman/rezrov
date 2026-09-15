using Rezrov.Core.Blorb;
using Rezrov.Glulx.Glk;
using Rezrov.Glulx.Instructions;
using static Rezrov.Tests.GlulxAssembler;

namespace Rezrov.Tests;

/// <summary>
/// [glk #sound] Sound channels: what the gestalt says with and without
/// audio, creating and destroying channels, playing, stopping, pausing,
/// volume, and the events the ends of sounds and volume changes
/// become.
/// </summary>
public class GlkSoundTests
{
    private const uint SchannelIterate = 0x00F0;
    private const uint SchannelGetRock = 0x00F1;
    private const uint SchannelCreate = 0x00F2;
    private const uint SchannelDestroy = 0x00F3;
    private const uint SchannelPlayMulti = 0x00F7;
    private const uint SchannelPlayExt = 0x00F9;
    private const uint SchannelSetVolumeExt = 0x00FD;
    private const uint SelectPoll = 0x00C1;

    private static BlorbFile Resources() => BlorbFile.Read(TestBlorb.Build(
        [],
        sounds: [(3, "OGGV", [1, 2, 3]), (5, "MOD ", [4])]));

    private static (GlkLibrary Glk, RecordingGlkDisplay Display, RecordingGlkSound Sound) Library(bool audio = true)
    {
        var display = new RecordingGlkDisplay();
        var sound = new RecordingGlkSound { CanPlaySounds = audio };
        var glk = new GlkLibrary(display, sound: sound) { Resources = Resources() };
        return (glk, display, sound);
    }

    private static uint Gestalt(GlkLibrary glk, GestaltSelector selector) => glk.Gestalt((uint)selector, 0, null);

    [Fact]
    public void WithoutAudioTheGestaltSaysNoAndNoChannelCanBeMade()
    {
        var (glk, _, _) = Library(audio: false);

        // [glk #sound_testing] Every sound selector answers no, and
        // [glk #sound_channels] creating a channel returns null.
        Assert.Equal(0u, Gestalt(glk, GestaltSelector.Sound));
        Assert.Equal(0u, Gestalt(glk, GestaltSelector.Sound2));
        Assert.Equal(0u, Gestalt(glk, GestaltSelector.SoundMusic));
        Assert.Equal(0u, Gestalt(glk, GestaltSelector.SoundVolume));
        Assert.Equal(0u, Gestalt(glk, GestaltSelector.SoundNotify));
        Assert.Null(glk.CreateSoundChannel(1));
        Assert.Equal(0, glk.SoundChannels.Count);
    }

    [Fact]
    public void WithAudioTheGestaltSaysYesToTheWholeSuite()
    {
        var (glk, _, _) = Library();

        // [glk #sound_testing] gestalt_Sound2 covers the rest, and the
        // rest are guaranteed to agree with it.
        Assert.Equal(1u, Gestalt(glk, GestaltSelector.Sound));
        Assert.Equal(1u, Gestalt(glk, GestaltSelector.Sound2));
        Assert.Equal(1u, Gestalt(glk, GestaltSelector.SoundMusic));
        Assert.Equal(1u, Gestalt(glk, GestaltSelector.SoundVolume));
        Assert.Equal(1u, Gestalt(glk, GestaltSelector.SoundNotify));
    }

    [Fact]
    public void AChannelStartsAtFullVolumeUnlessMadeAtAnother()
    {
        var (glk, _, sound) = Library();

        var first = glk.CreateSoundChannel(7)!;
        var second = glk.CreateSoundChannel(9, 0x8000)!;

        // [glk #sound_channels] Full volume is 0x10000; create_ext sets
        // another. [glk #opaque_iteration] The channels iterate in
        // creation order, and [glk #opaque_rocks] keep their rocks.
        Assert.Equal((1u, 7u, GlkSoundChannel.FullVolume), (first.Id, first.Rock, first.Volume));
        Assert.Equal((2u, 9u, 0x8000u), (second.Id, second.Rock, second.Volume));
        Assert.False(first.Paused);
        Assert.Null(first.Playing);
        Assert.Same(first, glk.SoundChannels.Next(null));
        Assert.Same(second, glk.SoundChannels.Next(first));
        Assert.Null(glk.SoundChannels.Next(second));

        // [glk op:schannel_destroy] The frontend hears, and the channel
        // is gone.
        glk.DestroySoundChannel(first);
        Assert.Equal(["Close 1"], sound.Calls);
        Assert.Null(glk.SoundChannels.Find(1));
        Assert.Same(second, glk.SoundChannels.Next(null));
    }

    [Fact]
    public void PlayingASoundStartsItOnTheChannelAndReplacesTheLast()
    {
        var (glk, _, sound) = Library();
        var channel = glk.CreateSoundChannel(1)!;

        // [glk op:schannel_play] Once, on the channel at its volume.
        Assert.True(glk.PlaySound(channel, 3));
        Assert.Equal(3u, channel.Playing);
        Assert.Equal(["Play 1 3 r1 v10000"], sound.Calls);

        // [glk #sound_playing] The same sound again stops the first.
        Assert.True(glk.PlaySound(channel, 3, uint.MaxValue));
        Assert.Equal(["Play 1 3 r1 v10000", "Stop 1", "Play 1 3 forever v10000"], sound.Calls);

        // A sound that does not exist stops the last and fails.
        sound.Calls.Clear();
        Assert.False(glk.PlaySound(channel, 99));
        Assert.Null(channel.Playing);
        Assert.Equal(["Stop 1"], sound.Calls);

        // Zero repeats plays nothing, but is not a failure.
        sound.Calls.Clear();
        Assert.True(glk.PlaySound(channel, 3, 0));
        Assert.Empty(sound.Calls);
        Assert.Null(channel.Playing);

        // A format the frontend cannot play fails, as the spec allows.
        sound.Unplayable.Add("MOD ");
        Assert.False(glk.PlaySound(channel, 5));
        Assert.Null(channel.Playing);
    }

    [Fact]
    public void TheEndOfASoundIsAnEventWhenAskedFor()
    {
        var (glk, display, sound) = Library();
        var channel = glk.CreateSoundChannel(1)!;

        glk.PlaySound(channel, 3, 2, 55);
        Assert.Equal(GlkEvent.None, glk.SelectPoll());

        // The frontend reports the end from its own thread: the library
        // wakes the display and holds the event for the next select.
        sound.EndSound(1);
        Assert.True(glk.HasPendingSoundEvents);
        Assert.Equal(1, display.Wakes);

        // [glk op:schannel_play_ext] No window, the resource number,
        // and the notify value, and the channel is idle again.
        Assert.Equal(new GlkEvent(EventType.SoundNotify, null, 3, 55), glk.Select());
        Assert.Null(channel.Playing);
        Assert.Empty(display.Timeouts);

        // With notify zero the end is silent, but the channel is still
        // idle afterward.
        glk.PlaySound(channel, 3);
        sound.EndSound(1);
        Assert.Equal(GlkEvent.None, glk.SelectPoll());
        Assert.Null(channel.Playing);
    }

    [Fact]
    public void AStoppedReplacedOrDestroyedSoundGivesNoEvent()
    {
        var (glk, _, sound) = Library();
        var channel = glk.CreateSoundChannel(1)!;

        // [glk op:schannel_stop] Stopped: the frontend's late report of
        // its end is ignored.
        glk.PlaySound(channel, 3, 1, 55);
        glk.StopSound(channel);
        sound.EndSound(1);
        Assert.Equal(GlkEvent.None, glk.SelectPoll());

        // [glk #sound_playing] Replaced: the first sound's end is
        // ignored, the second's is an event.
        glk.PlaySound(channel, 3, 1, 56);
        glk.PlaySound(channel, 3, 1, 57);
        sound.Started[^2].Ended();
        Assert.Equal(GlkEvent.None, glk.SelectPoll());
        Assert.Equal(3u, channel.Playing);
        sound.EndSound(1);
        Assert.Equal(new GlkEvent(EventType.SoundNotify, null, 3, 57), glk.SelectPoll());

        // [glk op:schannel_destroy] Destroyed while playing: no event.
        glk.PlaySound(channel, 3, 1, 58);
        glk.DestroySoundChannel(channel);
        sound.EndSound(1);
        Assert.Equal(GlkEvent.None, glk.SelectPoll());

        // Stopping a channel with nothing playing is not passed on.
        var other = glk.CreateSoundChannel(2)!;
        sound.Calls.Clear();
        glk.StopSound(other);
        Assert.Empty(sound.Calls);
    }

    [Fact]
    public void PlayMultiStartsEachSoundAndNotifiesEach()
    {
        var (glk, _, sound) = Library();
        var first = glk.CreateSoundChannel(1)!;
        var second = glk.CreateSoundChannel(2)!;

        // [glk op:schannel_play_multi] Once each, and the count of those
        // that started: sound 99 does not exist.
        Assert.Equal(1u, glk.PlaySounds([first, second], [3, 99], 7));
        Assert.Equal(2u, glk.PlaySounds([first, second], [3, 3], 7));
        Assert.Equal("Play 2 3 r1 v10000", sound.Calls[^1]);

        // A separate event for each, with the same notify value.
        sound.EndSound(1);
        sound.EndSound(2);
        Assert.Equal(new GlkEvent(EventType.SoundNotify, null, 3, 7), glk.SelectPoll());
        Assert.Equal(new GlkEvent(EventType.SoundNotify, null, 3, 7), glk.SelectPoll());
        Assert.Equal(GlkEvent.None, glk.SelectPoll());
    }

    [Fact]
    public void PauseAndUnpauseReachTheFrontendOnce()
    {
        var (glk, _, sound) = Library();
        var channel = glk.CreateSoundChannel(1)!;

        // [glk op:schannel_pause] Pausing a channel that is paused does
        // nothing, and a sound started on a paused channel is told so.
        glk.PauseSound(channel);
        glk.PauseSound(channel);
        Assert.True(channel.Paused);
        glk.PlaySound(channel, 3);
        Assert.Equal(["Pause 1", "Play 1 3 r1 v10000 paused"], sound.Calls);

        // [glk op:schannel_unpause] Likewise unpausing.
        glk.UnpauseSound(channel);
        glk.UnpauseSound(channel);
        Assert.False(channel.Paused);
        Assert.Equal(["Pause 1", "Play 1 3 r1 v10000 paused", "Unpause 1"], sound.Calls);
    }

    [Fact]
    public void VolumeChangesAtOnceOrOverTime()
    {
        var (glk, _, sound) = Library();
        var channel = glk.CreateSoundChannel(1)!;

        // [glk op:schannel_set_volume] At once, with no event.
        glk.SetSoundVolume(channel, 0x8000);
        Assert.Equal(0x8000u, channel.Volume);
        Assert.Equal(["Volume 1 8000 over 0"], sound.Calls);
        Assert.Equal(GlkEvent.None, glk.SelectPoll());

        // [glk op:schannel_set_volume_ext] At once with a notify value:
        // the change has completed, so the event comes at once.
        glk.SetSoundVolume(channel, 0x4000, 0, 9);
        Assert.Equal(new GlkEvent(EventType.VolumeNotify, null, 0, 9), glk.SelectPoll());

        // Over time: the frontend fades, and the event comes when it
        // says the fade is done.
        glk.SetSoundVolume(channel, 0, 500, 11);
        Assert.Equal("Volume 1 0 over 500", sound.Calls[^1]);
        Assert.Equal(0u, channel.Volume);
        Assert.Equal(GlkEvent.None, glk.SelectPoll());
        sound.EndFade(1);
        Assert.Equal(new GlkEvent(EventType.VolumeNotify, null, 0, 11), glk.SelectPoll());

        // [glk #sound_playing] A change interrupted by another never
        // completes: only the second's event comes.
        glk.SetSoundVolume(channel, 0x10000, 500, 12);
        glk.SetSoundVolume(channel, 0x2000, 500, 13);
        sound.Fades[^2].Changed();
        Assert.Equal(GlkEvent.None, glk.SelectPoll());
        sound.EndFade(1);
        Assert.Equal(new GlkEvent(EventType.VolumeNotify, null, 0, 13), glk.SelectPoll());

        // A fade nobody asked to hear about gives the frontend nothing
        // to call.
        glk.SetSoundVolume(channel, 0x3000, 100);
        Assert.Equal(3, sound.Fades.Count);
    }

    [Fact]
    public void LoadHintsPassOnForSoundsThatExist()
    {
        var (glk, _, sound) = Library();

        // [glk op:sound_load_hint] A hint about a sound the resource
        // file lacks is nothing to pass on.
        glk.SoundLoadHint(3, true);
        glk.SoundLoadHint(99, true);
        glk.SoundLoadHint(3, false);
        Assert.Equal(["Load 3", "Unload 3"], sound.Calls);
    }

    [Fact]
    public void TheSoundFunctionsWorkThroughTheDispatchLayer()
    {
        var display = new RecordingGlkDisplay();
        var sound = new RecordingGlkSound();
        var glk = new GlkLibrary(display, sound: sound) { Resources = Resources() };

        // Create a channel with rock 5, play sound 3 on it with notify
        // 42, read the rock back, iterate from nothing, change the
        // volume at once with notify 9, poll for the event, play
        // through play_multi with one-entry arrays, and destroy.
        var code = new GlulxAssembler().Function("main");
        Glk(code, SchannelCreate, Ram(0), C(5));
        Glk(code, SchannelPlayExt, Ram(4), Ram(0), C(3), C(1), C(42));
        Glk(code, SchannelGetRock, Ram(8), Ram(0));
        Glk(code, SchannelIterate, Ram(12), C(0), Ref(16));
        Glk(code, SchannelSetVolumeExt, Discard, Ram(0), C(0x8000), C(0), C(9));
        Glk(code, SelectPoll, Discard, Ref(20));
        code.Op(Opcode.Copy, Ram(0), Ram(40));
        code.Op(Opcode.Copy, C(3), Ram(44));
        Glk(code, SchannelPlayMulti, Ram(48), Ref(40), C(1), Ref(44), C(1), C(6));
        Glk(code, SchannelDestroy, Discard, Ram(0));
        var machine = GlulxRun.Run(code.Return(C(0)), glk: glk);

        Assert.Equal(1u, machine.Ram(0));
        Assert.Equal(1u, machine.Ram(4));
        Assert.Equal(5u, machine.Ram(8));
        Assert.Equal((1u, 5u), (machine.Ram(12), machine.Ram(16)));
        Assert.Equal(((uint)EventType.VolumeNotify, 0u, 0u, 9u), (machine.Ram(20), machine.Ram(24), machine.Ram(28), machine.Ram(32)));
        Assert.Equal(1u, machine.Ram(48));
        Assert.Equal(["Play 1 3 r1 v10000", "Volume 1 8000 over 0", "Stop 1", "Play 1 3 r1 v8000", "Close 1"], sound.Calls);
        Assert.Empty(glk.Warnings);
        Assert.Equal(0, glk.SoundChannels.Count);
    }

    /// <summary>
    /// [glulx op:glk] Pushes the arguments last first and calls the
    /// function, storing its result.
    /// </summary>
    private static GlulxAssembler Glk(GlulxAssembler code, uint selector, Arg result, params Arg[] args)
    {
        for (var i = args.Length - 1; i >= 0; i--)
        {
            code.Op(Opcode.Copy, args[i], Sp);
        }

        return code.Op(Opcode.Glk, C(selector), C(args.Length), result);
    }

    private static Arg Ref(uint offset) => C(GlulxRun.RamStart + offset);
}

using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Sound;
using static Rezrov.Tests.Assembler;

namespace Rezrov.Tests;

/// <summary>
/// The sound_effect opcode in the interpreter: every operand form, the
/// end-of-sound routine, and the header bits.
/// </summary>
public partial class InterpreterTests
{
    [Fact]
    public void BleepsTakeOneOperandOrNone()
    {
        var sound = new RecordingSound();
        var run = Execute(
            new Assembler()
                .Variable(Op.SoundEffect, true, Small(1))
                .Variable(Op.SoundEffect, true, Small(2))
                .Variable(Op.SoundEffect, true)
                .Variable(Op.SoundEffect, true, Small(1), Small(2))
                .Quit(),
            sound: sound);

        // [zm op:sound_effect] No operands means bleep 1, and a bleep
        // with more operands is a mistake that still bleeps.
        Assert.Equal(["Bleep 1", "Bleep 2", "Bleep 1", "Bleep 1"], sound.Calls);
        Assert.Contains("sound_effect", Assert.Single(run.Interpreter.RuntimeErrors));
    }

    [Fact]
    public void SoundsArePreparedStartedStoppedAndFinished()
    {
        var sound = new RecordingSound();
        var run = Execute(
            new Assembler()
                .Variable(Op.SoundEffect, true, Small(3), Small(1))
                .Variable(Op.SoundEffect, true, Small(3), Small(2), Large(0x0208))
                .Variable(Op.SoundEffect, true, Small(3), Small(3))
                .Variable(Op.SoundEffect, true, Small(3), Small(4))
                .Variable(Op.SoundEffect, true, Small(3), Small(2), Large(0xFFFF))
                .Variable(Op.SoundEffect, true, Small(0), Small(3))
                .Quit(),
            sound: sound,
            setup: story => story.Bytes[0x300] = 0);

        // [zm op:sound_effect] Effects 1 to 4; the volume word's low
        // byte is the volume and its high byte the repeats, with 255
        // meaning loudest and forever; and sound 0 with effect 3 stops
        // everything.
        Assert.Equal(["Prepare 3", "Play 3 v8 r2", "Stop 3", "Finish 3", "Play 3 v8 r-1", "Stop 3"], sound.Calls);
        Assert.Empty(run.Interpreter.RuntimeErrors);
    }

    [Fact]
    public void TheRoutineIsCalledWhenTheSoundEnds()
    {
        // [zm 9.4.4] A sound started with a routine; when the frontend
        // reports its last cycle over, the interpreter calls the routine
        // before the next instruction.
        var sound = new RecordingSound();
        var run = Execute(
            new Assembler()
                .Variable(Op.SoundEffect, true, Small(3), Small(2), Large(0x0108), Large(RoutineA / 4))
                .Long(Op.Store, Small(G0), Small(1))
                .Long(Op.Store, Small(G1), Small(1))
                .Quit(),
            sound,
            story => story.Routine(RoutineA, 0, new Assembler()
                .Long(Op.Store, Small(G2), Var(G0))
                .Short0(Op.Rtrue)
                .ToArray()),
            afterEachStep: (interpreter, count) =>
            {
                if (count == 2)
                {
                    sound.EndCycle(3);
                }
            });

        // G2 was set from G0 by the routine, which ran after the second
        // instruction (G0 = 1) and before the third.
        Assert.Equal(1, run.Global(G2));
        Assert.Equal(1, run.Global(G1));
        Assert.Null(run.Interpreter.Sound.PlayingSample);
    }

    [Fact]
    public void AMissingSoundOrEffectIsReported()
    {
        var run = Execute(
            new Assembler()
                .Variable(Op.SoundEffect, true, Small(9), Small(2), Large(0x0108))
                .Variable(Op.SoundEffect, true, Small(3), Small(7))
                .Quit(),
            sound: new RecordingSound());

        Assert.Equal(2, run.Interpreter.RuntimeErrors.Count);
        Assert.Contains("no sound 9", run.Interpreter.RuntimeErrors[0]);
        Assert.Contains("effect 7", run.Interpreter.RuntimeErrors[1]);
    }

    [Fact]
    public void AVersion3GameMayLeaveTheVolumeOutWithoutAWarning()
    {
        // The Lurking Horror calls sound_effect with two operands, and
        // [blorb 11.4] its resource file decides the looping.
        var sound = new RecordingSound();
        var run = Execute(
            new Assembler()
                .Variable(Op.SoundEffect, true, Small(3), Small(2))
                .Variable(Op.SoundEffect, true, Small(4), Small(2), Large(0x0108))
                .Quit(),
            sound: sound,
            version: ZMachineVersion.V3);

        Assert.Equal("Play 3 v8 r1", sound.Calls[0]);
        Assert.Contains("repeats", Assert.Single(run.Interpreter.RuntimeErrors));
    }

    [Fact]
    public void ZeroRepeatsInVersion5PlaysOnceWithAWarning()
    {
        var sound = new RecordingSound();
        var run = Execute(
            new Assembler()
                .Variable(Op.SoundEffect, true, Small(3), Small(2), Small(8))
                .Quit(),
            sound: sound);

        // [zm op:sound_effect] "Setting repeats to zero in V5 is illegal;
        // treat it as once and maybe warn." The quit stops it after.
        Assert.Equal(["Play 3 v8 r1", "Stop 3"], sound.Calls);
        Assert.Contains("repeats", Assert.Single(run.Interpreter.RuntimeErrors));
    }

    [Fact]
    public void WithoutAudioTheHeaderSaysSoAndSoundsAreIgnored()
    {
        var story = new Story(ZMachineVersion.V5);
        story.PutWord(0x10, 0x0080);

        var silent = new Interpreter(new ZMemory((byte[])story.Bytes.Clone()), new RecordingScreen(), new ScriptedInput());
        Assert.False(silent.Header.Flags2.HasFlag(Flags2.WantsSoundEffects));

        // [zm 9.1.2] With a frontend that can play, the bit stays.
        var loud = new Interpreter(new ZMemory((byte[])story.Bytes.Clone()), new RecordingScreen(), new ScriptedInput(), sound: new RecordingSound());
        Assert.True(loud.Header.Flags2.HasFlag(Flags2.WantsSoundEffects));
    }

    [Fact]
    public void ResourcesFromTheWrongGameAreRefused()
    {
        var run = Execute(new Assembler().Quit(), sound: new RecordingSound());
        var blorb = BlorbFileTests.BuildForTest(release: 99, serial: "000000", checksum: 0);

        // [blorb 6] The identifier must match the story.
        Assert.Throws<InvalidDataException>(() => run.Interpreter.UseResources(blorb));
    }

    private static Run Execute(
        Assembler code,
        ISound sound,
        Action<Story>? setup = null,
        Action<Interpreter, int>? afterEachStep = null,
        ZMachineVersion version = ZMachineVersion.V5)
    {
        var story = new Story(version);
        story.Put(Code, code.ToArray());
        setup?.Invoke(story);

        var memory = new ZMemory(story.Bytes);
        var writer = new StringWriter();
        var interpreter = new Interpreter(memory, new TextWriterScreen(writer), new ScriptedInput(), sound: sound);
        interpreter.Sound.AddResource(new SoundResource(3, "AIFF", new byte[] { 1 }));
        interpreter.Sound.AddResource(new SoundResource(4, "MOD ", new byte[] { 2 }));

        var count = 0;
        while (!interpreter.HasQuit && count < 10000)
        {
            interpreter.Step();
            count++;
            afterEachStep?.Invoke(interpreter, count);
        }

        Assert.True(interpreter.HasQuit, "The program did not quit within 10000 instructions.");
        return new Run(story, writer.ToString(), interpreter);
    }
}

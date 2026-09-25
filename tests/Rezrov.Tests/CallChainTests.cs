using Rezrov.Core.Acceptance;
using Rezrov.Debugging;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Screen;

namespace Rezrov.Tests;

/// <summary>
/// Naming the routines a running game is inside.
/// </summary>
public class CallChainTests
{
    private const string SubmoduleAbsent = "The entharion submodule is not populated.";

    [Fact]
    public void TheRunningRoutineComesFirstAndTheOutermostLast()
    {
        var (_, _, machine) = Nested();

        machine.Step();
        machine.Step();

        var stack = CallChain.Of(machine.State);

        Assert.Equal([3, 2, 1], stack.Select(e => e.Depth));
        Assert.Equal([true, true, false], stack.Select(e => e.Exact));

        // The two called frames name their routines outright. The one
        // the game started in has none to name, and no listing was
        // offered to guess with.
        Assert.Equal(0x1200, stack[0].Routine);
        Assert.Equal(0x1100, stack[1].Routine);
        Assert.Null(stack[2].Routine);
    }

    [Fact]
    public void EachFrameIsWhereTheOneAboveItWillReturnTo()
    {
        var (_, _, machine) = Nested();

        machine.Step();
        machine.Step();

        var stack = CallChain.Of(machine.State);
        var frames = machine.State.Frames;

        Assert.Equal(machine.State.ProgramCounter, stack[0].Address);
        Assert.Equal(frames[2].ReturnAddress, stack[1].Address);
        Assert.Equal(frames[1].ReturnAddress, stack[2].Address);
    }

    [Fact]
    public void AFrameThatCannotNameItsRoutineIsNamedFromTheListing()
    {
        // The outermost frame of a Zork I run is the one the game
        // started in, so the machine has nothing to say about it and
        // the disassembly answers instead.
        var root = Corpus.FindRepositoryRoot();
        Assert.SkipWhen(root is null, SubmoduleAbsent);

        var path = Path.Combine(root, "acceptance", "zork1-r88-s840726.accept");
        Assert.SkipUnless(File.Exists(path), SubmoduleAbsent);

        var script = AcceptanceScript.Load(path);
        Assert.SkipUnless(File.Exists(script.GamePath), SubmoduleAbsent);

        var memory = new ZMemory(File.ReadAllBytes(script.GamePath));
        var header = new StoryHeader(memory);
        var listing = Disassembly.Of(memory, header);

        var machine = new Interpreter(
            memory,
            new TextWriterScreen(new StringWriter()),
            new ScriptedInput([.. script.Commands]),
            new RandomGenerator(script.Seed));

        // Far enough in to be several routines deep rather than at the
        // start, and shallow enough that the walkthrough is still
        // running.
        while (machine.State.FrameNumber < 4 && machine.InstructionsExecuted < 1_000_000)
        {
            machine.Step();
        }

        Assert.True(machine.State.FrameNumber >= 4, "the game never got four routines deep");

        var stack = CallChain.Of(machine.State, listing);

        Assert.All(stack, entry => Assert.NotNull(entry.Routine));
        Assert.False(stack[^1].Exact);
        Assert.All(stack.Take(stack.Count - 1), entry => Assert.True(entry.Exact));

        // Whatever named a routine, the address the frame is at has to
        // be inside it.
        Assert.All(stack, entry =>
        {
            var routine = listing.RoutineAt(entry.Address);
            Assert.NotNull(routine);
            Assert.Equal(routine.Address, entry.Routine);
        });
    }

    /// <summary>
    /// A machine two calls deep: the start calls the routine at $1100,
    /// which calls the one at $1200.
    /// </summary>
    private static (ZMemory Memory, StoryHeader Header, Interpreter Machine) Nested()
    {
        var bytes = new byte[8192];

        bytes[0x00] = (byte)ZMachineVersion.V5;
        Put(bytes, 0x04, 0x0400);   // [zm 11.1] high memory base
        Put(bytes, 0x06, 0x1000);   // [zm 11.1] initial program counter
        Put(bytes, 0x0E, 0x0400);   // [zm 11.1] static memory base

        new Assembler()
            .Variable(Op.Call, true, Assembler.Large(0x1100 / 4)).Store(0)
            .Quit()
            .ToArray()
            .CopyTo(bytes, 0x1000);

        bytes[0x1100] = 0;
        new Assembler()
            .Variable(Op.Call, true, Assembler.Large(0x1200 / 4)).Store(0)
            .Short0(Op.Rtrue)
            .ToArray()
            .CopyTo(bytes, 0x1101);

        bytes[0x1200] = 0;
        bytes[0x1201] = 0xB0;       // [zm op:rtrue]

        var memory = new ZMemory(bytes);
        var header = new StoryHeader(memory);

        return (memory, header, new Interpreter(
            memory,
            new TextWriterScreen(new StringWriter()),
            new ScriptedInput()));
    }

    private static void Put(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 8);
        bytes[offset + 1] = (byte)value;
    }
}

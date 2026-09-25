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
        var (_, _, machine) = DebugGame.Of();

        machine.Step();
        machine.Step();

        var stack = CallChain.Of(machine.State);

        Assert.Equal([3, 2, 1], stack.Select(e => e.Depth));
        Assert.Equal([true, true, false], stack.Select(e => e.Exact));

        // The two called frames name their routines outright. The one
        // the game started in has none to name, and no listing was
        // offered to guess with.
        Assert.Equal(DebugGame.Second, stack[0].Routine);
        Assert.Equal(DebugGame.First, stack[1].Routine);
        Assert.Null(stack[2].Routine);
    }

    [Fact]
    public void EachFrameIsWhereTheOneAboveItWillReturnTo()
    {
        var (_, _, machine) = DebugGame.Of();

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
}

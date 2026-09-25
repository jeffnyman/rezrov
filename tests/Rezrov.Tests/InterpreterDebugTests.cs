using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using static Rezrov.Tests.Assembler;

namespace Rezrov.Tests;

/// <summary>
/// What the interpreter shows of itself to something driving the game
/// rather than playing it: the call chain, the addresses a run stops
/// at, and why a run came back.
/// </summary>
public partial class InterpreterTests
{
    [Fact]
    public void AFrameKnowsWhichRoutineItIsRunning()
    {
        var machine = Machine(
            new Assembler()
                .Variable(Op.Call, true, Large(RoutineA / 4)).Store(0)
                .Quit(),
            story => story.Routine(RoutineA, 0, new Assembler().Short0(Op.Rtrue).ToArray()));

        // One instruction is the call, so the machine is then inside
        // the routine it called.
        machine.Step();

        Assert.Equal(RoutineA, machine.State.CurrentFrame.RoutineAddress);
    }

    [Fact]
    public void TheFrameAGameStartsInKnowsNoRoutine()
    {
        // [zm 5.5] Outside Version 6 a game begins at an address, not
        // inside a routine, so there is nothing truthful to say.
        var machine = Machine(new Assembler().Quit());

        Assert.Null(machine.State.CurrentFrame.RoutineAddress);
    }

    [Fact]
    public void AFrameRebuiltFromASavedGameKnowsNoRoutine()
    {
        // [quetzal 4] A saved frame is a return address, its locals,
        // and its stack. The routine it was running is not in there.
        var machine = Machine(
            new Assembler()
                .Variable(Op.Call, true, Large(RoutineA / 4)).Store(0)
                .Quit(),
            story => story.Routine(RoutineA, 0, new Assembler().Short0(Op.Rtrue).ToArray()));

        machine.Step();
        Assert.Equal(RoutineA, machine.State.CurrentFrame.RoutineAddress);

        machine.State.Restore(machine.State.Snapshot());

        Assert.Null(machine.State.CurrentFrame.RoutineAddress);
    }

    [Fact]
    public void TheWholeCallChainCanBeReadFromOutside()
    {
        // The start, then A, then B, so three frames with the newest
        // last and the one the machine is in at the end of the list.
        var machine = Machine(
            new Assembler()
                .Variable(Op.Call, true, Large(RoutineA / 4)).Store(0)
                .Quit(),
            story =>
            {
                story.Routine(RoutineA, 0, new Assembler()
                    .Variable(Op.Call, true, Large(RoutineB / 4)).Store(0)
                    .Short0(Op.Rtrue)
                    .ToArray());
                story.Routine(RoutineB, 0, new Assembler().Short0(Op.Rtrue).ToArray());
            });

        machine.Step();
        machine.Step();

        Assert.Equal([null, RoutineA, RoutineB], machine.State.Frames.Select(f => f.RoutineAddress));
        Assert.Equal(machine.State.CurrentFrame, machine.State.Frames[^1]);
        Assert.Equal(3, machine.State.FrameNumber);
    }

    [Fact]
    public void ARunStopsBeforeAnInstructionItWasAskedToStopAt()
    {
        var machine = Machine(new Assembler()
            .Short0(Op.Nop)
            .Short0(Op.Nop)
            .Short0(Op.Nop)
            .Quit());

        machine.Breakpoints.Add(Code + 2);

        Assert.Equal(StopReason.Breakpoint, machine.Continue());
        Assert.Equal(Code + 2, machine.State.ProgramCounter);
        Assert.Equal(2, machine.InstructionsExecuted);
    }

    [Fact]
    public void ARunAlwaysGetsPastTheBreakpointItIsSittingOn()
    {
        // Otherwise carrying on from a breakpoint would mean stepping
        // off it by hand first, every time.
        var machine = Machine(new Assembler()
            .Short0(Op.Nop)
            .Short0(Op.Nop)
            .Quit());

        machine.Breakpoints.Add(Code);
        machine.Breakpoints.Add(Code + 1);

        Assert.Equal(StopReason.Breakpoint, machine.Continue());
        Assert.Equal(Code + 1, machine.State.ProgramCounter);

        Assert.Equal(StopReason.Quit, machine.Continue());
    }

    [Fact]
    public void ARunStopsWhenTheCallChainComesBackToTheDepthAskedFor()
    {
        var machine = Machine(
            new Assembler()
                .Variable(Op.Call, true, Large(RoutineA / 4)).Store(0)
                .Quit(),
            story => story.Routine(RoutineA, 0, new Assembler()
                .Short0(Op.Nop)
                .Short0(Op.Nop)
                .Short0(Op.Rtrue)
                .ToArray()));

        machine.Step();
        Assert.Equal(2, machine.State.FrameNumber);

        // Running to a return: come back as soon as the chain is one
        // deep again, which the routine's rtrue does.
        Assert.Equal(StopReason.Unwound, machine.Continue(unwind: 1));
        Assert.Equal(1, machine.State.FrameNumber);
        Assert.False(machine.HasQuit);
    }

    [Fact]
    public void SteppingOverACallIsRunningUntilItComesBack()
    {
        // The two together are how a debugger steps over a call, and
        // nothing else is needed to say it.
        var machine = Machine(
            new Assembler()
                .Variable(Op.Call, true, Large(RoutineA / 4)).Store(0)
                .Long(Op.Store, Small(G0), Small(7))
                .Quit(),
            story => story.Routine(RoutineA, 0, new Assembler()
                .Short0(Op.Nop)
                .Short0(Op.Rtrue)
                .ToArray()));

        var depth = machine.State.FrameNumber;
        machine.Step();

        Assert.True(machine.State.FrameNumber > depth);
        Assert.Equal(StopReason.Unwound, machine.Continue(unwind: depth));

        // Back in the caller with the call finished and nothing after
        // it carried out yet.
        Assert.Equal(depth, machine.State.FrameNumber);
        Assert.Equal(0, machine.State.ReadVariable(G0));
    }

    [Fact]
    public void ARunComesBackWhenTheInstructionsRunOut()
    {
        var machine = Machine(new Assembler()
            .Short0(Op.Nop)
            .Short0(Op.Nop)
            .Short0(Op.Nop)
            .Quit());

        Assert.Equal(StopReason.Limit, machine.Continue(limit: 2));
        Assert.Equal(2, machine.InstructionsExecuted);
        Assert.False(machine.HasQuit);
    }

    [Fact]
    public void ARunComesBackWhenTheGameQuits()
    {
        var machine = Machine(new Assembler().Short0(Op.Nop).Quit());

        Assert.Equal(StopReason.Quit, machine.Continue());
        Assert.True(machine.HasQuit);

        // And a game that has already quit is not started again.
        Assert.Equal(StopReason.Quit, machine.Continue());
    }

    [Fact]
    public void TheValueStackCanBeReadFrameByFrame()
    {
        // [zm 6.3.1] A routine sees only what it pushed itself, and
        // where its own values begin is what the frame records.
        var machine = Machine(
            new Assembler()
                .Variable(Op.Push, true, Large(0x1111))
                .Variable(Op.Call, true, Large(RoutineA / 4)).Store(0)
                .Quit(),
            story => story.Routine(RoutineA, 0, new Assembler()
                .Variable(Op.Push, true, Large(0x2222))
                .Short0(Op.Rtrue)
                .ToArray()));

        machine.Step();
        machine.Step();
        machine.Step();

        Assert.Equal([0x1111, 0x2222], machine.State.Stack);
        Assert.Equal(1, machine.State.CurrentFrame.StackBase);
        Assert.Equal(1, machine.State.StackDepth);
    }
}

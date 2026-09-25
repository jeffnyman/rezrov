using Rezrov.Debugging;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;

namespace Rezrov.Tests;

/// <summary>
/// The debugger, driven the way a player drives it: a typed line in,
/// and what to print back.
/// </summary>
public class DebugSessionTests
{
    [Fact]
    public void ACommandThatIsNotOneSaysSo()
    {
        var (session, _) = Session();

        Assert.Contains("there is no wibble command", session.Obey("wibble"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyLineSaysNothing()
    {
        var (session, _) = Session();

        Assert.Equal(string.Empty, session.Obey("   "));
    }

    [Fact]
    public void BreakingOnARoutineStopsWhereItsInstructionsBegin()
    {
        // [zm 5.2] The address the listing shows for a routine is its
        // locals, which the program counter never holds, so typing it
        // has to mean the first instruction.
        var (session, machine) = Session();

        var said = session.Obey($"break {DebugGame.First:X4}");

        Assert.Contains($"{DebugGame.First + 1:X4}", said, StringComparison.Ordinal);
        Assert.Contains($"routine {DebugGame.First:X4}", said, StringComparison.Ordinal);
        Assert.Contains(DebugGame.First + 1, machine.Breakpoints);
    }

    [Fact]
    public void ARunStopsAtABreakpointAndSaysWhereItIs()
    {
        var (session, machine) = Session();

        session.Obey($"break {DebugGame.First:X4}");
        var said = session.Obey("continue");

        Assert.Contains("stopped", said, StringComparison.Ordinal);
        Assert.Contains($"{DebugGame.First + 1:X4}", said, StringComparison.Ordinal);
        Assert.Equal(DebugGame.First + 1, machine.State.ProgramCounter);
    }

    [Fact]
    public void BreakingOnReadsStopsWhereTheGameTakesACommand()
    {
        var (session, machine) = Session();

        var said = session.Obey("break reads");

        Assert.Contains("1 of the 1 places", said, StringComparison.Ordinal);
        Assert.Contains(DebugGame.Second + 1, machine.Breakpoints);
    }

    [Fact]
    public void ABreakpointWhereNoInstructionBeginsIsStillSetAndSaidSo()
    {
        // The scan is a very good guess and not a fact, so a request it
        // does not understand is carried out and remarked on rather
        // than refused.
        var (session, machine) = Session();

        var said = session.Obey($"break {DebugGame.First + 2:X4}");

        Assert.Contains("no instruction begins there", said, StringComparison.Ordinal);
        Assert.Contains(DebugGame.First + 2, machine.Breakpoints);
    }

    [Fact]
    public void BreakpointsCanBeListedAndCleared()
    {
        var (session, machine) = Session();

        session.Obey($"break {DebugGame.First:X4}");
        session.Obey($"break {DebugGame.Second:X4}");

        var listed = session.Obey("breaks");
        Assert.Contains($"{DebugGame.First + 1:X4}", listed, StringComparison.Ordinal);
        Assert.Contains($"{DebugGame.Second + 1:X4}", listed, StringComparison.Ordinal);

        Assert.Contains("no longer stopping", session.Obey($"delete {DebugGame.First + 1:X4}"), StringComparison.Ordinal);
        Assert.Contains("1 breakpoint gone", session.Obey("delete"), StringComparison.Ordinal);

        Assert.Empty(machine.Breakpoints);
        Assert.Contains("nothing is being stopped at", session.Obey("breaks"), StringComparison.Ordinal);
    }

    [Fact]
    public void SteppingGoesIntoACallAndNextGoesOverIt()
    {
        var (session, machine) = Session();

        session.Obey("step");
        Assert.Equal(2, machine.State.FrameNumber);

        // Back out, and this time step over the call rather than into
        // it: the chain is no deeper and the caller has moved on.
        var (over, second) = Session();
        over.Obey("next");

        Assert.Equal(1, second.State.FrameNumber);
        Assert.True(second.InstructionsExecuted > 1);
    }

    [Fact]
    public void FinishRunsUntilTheRoutineReturns()
    {
        var (session, machine) = Session();

        session.Obey("step");
        Assert.Equal(2, machine.State.FrameNumber);

        session.Obey("finish");

        Assert.Equal(1, machine.State.FrameNumber);
    }

    [Fact]
    public void FinishSaysSoInTheRoutineTheGameStartedIn()
    {
        // [zm 5.4] The outermost routine has nothing to return to.
        var (session, _) = Session();

        Assert.Contains("does not return", session.Obey("finish"), StringComparison.Ordinal);
    }

    [Fact]
    public void WhereShowsTheRoutineRunningFirst()
    {
        var (session, _) = Session();

        session.Obey("step");
        session.Obey("step");

        var lines = session.Obey("where").Split(Environment.NewLine);

        Assert.Equal(3, lines.Length);
        Assert.Contains($"routine {DebugGame.Second:X4}", lines[0], StringComparison.Ordinal);
        Assert.Contains($"routine {DebugGame.First:X4}", lines[1], StringComparison.Ordinal);
        Assert.Contains("from the listing", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void ListMarksTheInstructionTheGameIsOn()
    {
        var (session, machine) = Session();

        session.Obey("step");

        var marked = session.Obey("list")
            .Split(Environment.NewLine)
            .Single(line => line.StartsWith("=>", StringComparison.Ordinal));

        Assert.Contains($"{machine.State.ProgramCounter:X4}", marked, StringComparison.Ordinal);
    }

    [Fact]
    public void ListReadsStraightFromAnAddressNoRoutineCovers()
    {
        var (session, _) = Session();

        var said = session.Obey($"list {DebugGame.Start:X4}");

        Assert.Contains("no routine covers", said, StringComparison.Ordinal);
        Assert.Contains($"{DebugGame.Start:X4}", said, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalsAndTheStackSayWhenThereIsNothing()
    {
        var (session, _) = Session();

        Assert.Contains("no locals", session.Obey("locals"), StringComparison.Ordinal);
        Assert.Contains("pushed nothing", session.Obey("stack"), StringComparison.Ordinal);
    }

    [Fact]
    public void AGlobalIsReadByItsOwnNumberRatherThanItsVariableNumber()
    {
        // [zm 6.2] Global 0 is variable $10, and it is global 0 that
        // the listing prints and a player types.
        var (session, machine) = Session();

        machine.State.WriteVariable(0x10, 0x1234);
        machine.State.WriteVariable(0x11, 0x5678);

        Assert.Contains("G00 1234", session.Obey("globals 00"), StringComparison.Ordinal);
        Assert.Contains("G01 5678", session.Obey("globals 01"), StringComparison.Ordinal);

        var all = session.Obey("globals");
        Assert.Contains("G00 1234", all, StringComparison.Ordinal);
        Assert.Contains("G01 5678", all, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadShowsTheBytesAtAnAddress()
    {
        var (session, _) = Session();

        var said = session.Obey("read 0000");

        // The first byte of a story file is its version.
        Assert.Contains("05", said, StringComparison.Ordinal);
        Assert.Contains("byte 05", said, StringComparison.Ordinal);
    }

    [Fact]
    public void ACommandThatNeedsAnAddressSaysWhatItWants()
    {
        var (session, _) = Session();

        Assert.Contains("break wants", session.Obey("break"), StringComparison.Ordinal);
        Assert.Contains("read wants", session.Obey("read zzz"), StringComparison.Ordinal);
        Assert.Contains("list wants", session.Obey("list zzz"), StringComparison.Ordinal);
    }

    [Fact]
    public void AddressesMayBeWrittenWithAMarkerOrWithout()
    {
        var (session, machine) = Session();

        session.Obey($"break ${DebugGame.First + 1:X4}");
        session.Obey($"break 0x{DebugGame.Second + 1:X4}");

        Assert.Equal([DebugGame.First + 1, DebugGame.Second + 1], machine.Breakpoints.Order());
    }

    [Fact]
    public void QuittingFinishesTheSession()
    {
        var (session, _) = Session();

        Assert.False(session.Finished);
        session.Obey("quit");
        Assert.True(session.Finished);
    }

    [Fact]
    public void EverythingSaysTheGameHasQuitOnceItHas()
    {
        var (session, machine) = Session();

        session.Obey("continue");
        Assert.True(machine.HasQuit);

        foreach (var command in new[] { "step", "next", "finish", "continue" })
        {
            Assert.Equal("the game has quit.", session.Obey(command));
        }
    }

    [Fact]
    public void HelpNamesEveryCommand()
    {
        var (session, _) = Session();

        var help = session.Obey("help");

        foreach (var command in new[]
        {
            "break", "delete", "breaks", "step", "next", "finish",
            "continue", "where", "list", "locals", "globals", "stack", "read", "quit",
        })
        {
            Assert.Contains(command, help, StringComparison.Ordinal);
        }
    }

    private static (DebugSession Session, Interpreter Machine) Session()
    {
        var (memory, header, machine) = DebugGame.Of();

        return (new DebugSession(machine, Disassembly.Of(memory, header)), machine);
    }
}

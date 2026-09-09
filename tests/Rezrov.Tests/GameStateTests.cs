using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;

namespace Rezrov.Tests;

public class GameStateTests
{
    private const int Globals = 0x100;
    private const int RoutineA = 0x800;
    private const int RoutineB = 0x900;

    /// <summary>
    /// A story with a globals table at $100, dynamic memory ending at
    /// $400, and two routines in static memory. Routine A has three
    /// locals, with header defaults of 10, 20, and 30 in the versions
    /// that have them; routine B has none. The code bytes are never
    /// executed here, since this tests the state and not the opcodes.
    /// </summary>
    private static (GameState State, ZMemory Memory) Fresh(ZMachineVersion version)
    {
        var bytes = new byte[4096];
        bytes[0x00] = (byte)version;
        PutWord(bytes, 0x04, 0x0400);
        PutWord(bytes, 0x06, 0x0500);
        PutWord(bytes, 0x0C, Globals);
        PutWord(bytes, 0x0E, 0x0400);

        // Version 6 starts by calling main, so point $06 at routine B as
        // a packed address. Both offsets in the header are zero, so
        // [zm 1.2.3] packed is byte address over 4.
        if (version == ZMachineVersion.V6)
        {
            PutWord(bytes, 0x06, RoutineB / 4);
        }

        bytes[RoutineA] = 3;
        if (version <= ZMachineVersion.V4)
        {
            PutWord(bytes, RoutineA + 1, 10);
            PutWord(bytes, RoutineA + 3, 20);
            PutWord(bytes, RoutineA + 5, 30);
        }

        bytes[RoutineB] = 0;

        var memory = new ZMemory(bytes);
        return (new GameState(memory, new StoryHeader(memory)), memory);
    }

    private static void PutWord(byte[] bytes, int address, int value)
    {
        bytes[address] = (byte)(value >> 8);
        bytes[address + 1] = (byte)value;
    }

    // [zm 1.2.3] Packed addresses for the test routines by version.
    private static ushort Packed(ZMachineVersion version, int address) => version switch
    {
        <= ZMachineVersion.V3 => (ushort)(address / 2),
        ZMachineVersion.V8 => (ushort)(address / 8),
        _ => (ushort)(address / 4),
    };

    [Fact]
    public void StartsAtTheInitialProgramCounterWithNoLocals()
    {
        // [zm 5.5]
        var (state, _) = Fresh(ZMachineVersion.V5);

        Assert.Equal(0x0500, state.ProgramCounter);
        Assert.Equal(1, state.FrameNumber);
        Assert.Empty(state.CurrentFrame.Locals);
        Assert.Equal(0, state.StackDepth);
    }

    [Fact]
    public void Version6StartsInsideTheMainRoutine()
    {
        // [zm 5.4] The main routine is called at startup, so execution
        // begins at its first instruction, and it is the bottom frame.
        var (state, _) = Fresh(ZMachineVersion.V6);

        Assert.Equal(RoutineB + 1, state.ProgramCounter);
        Assert.Equal(1, state.FrameNumber);
        Assert.Throws<InvalidOperationException>(() => state.Return(0));
    }

    [Fact]
    public void GlobalsLiveInTheTableInMemory()
    {
        var (state, memory) = Fresh(ZMachineVersion.V5);

        // [zm 6.2] Variable $10 is word 0 of the table and $FF is word 239.
        memory.WriteWord(Globals, 0x1234);
        memory.WriteWord(Globals + (239 * 2), 0x5678);

        Assert.Equal(0x1234, state.ReadVariable(0x10));
        Assert.Equal(0x5678, state.ReadVariable(0xFF));

        state.WriteVariable(0x11, 0xABCD);
        Assert.Equal(0xABCD, memory.ReadWord(Globals + 2));
    }

    [Fact]
    public void VariableZeroPushesAndPops()
    {
        var (state, _) = Fresh(ZMachineVersion.V5);

        // [zm 6.3] Writing $00 pushes, reading it pulls, last in first out.
        state.WriteVariable(0, 5);
        state.WriteVariable(0, 7);
        Assert.Equal(2, state.StackDepth);

        Assert.Equal(7, state.ReadVariable(0));
        Assert.Equal(5, state.ReadVariable(0));
        Assert.Equal(0, state.StackDepth);
    }

    [Fact]
    public void PullingFromAnEmptyStackIsAnError()
    {
        // [zm 6.3.1] It is illegal to pull unless values were pushed.
        var (state, _) = Fresh(ZMachineVersion.V5);

        Assert.Throws<InvalidOperationException>(() => state.ReadVariable(0));
        Assert.Throws<InvalidOperationException>(() => state.Peek());
    }

    [Fact]
    public void ARoutineCannotReachTheCallersStack()
    {
        var (state, _) = Fresh(ZMachineVersion.V5);
        state.Push(42);

        // [zm 6.3.1] The stack is empty at the start of each routine, so
        // the 42 is physically below but out of reach.
        state.CallRoutine(Packed(ZMachineVersion.V5, RoutineB), [], null, 0x0500);

        Assert.Equal(0, state.StackDepth);
        Assert.Throws<InvalidOperationException>(() => state.Pop());
    }

    [Fact]
    public void ReturningDiscardsWhatTheRoutinePushed()
    {
        var (state, _) = Fresh(ZMachineVersion.V5);
        state.Push(1);

        state.CallRoutine(Packed(ZMachineVersion.V5, RoutineB), [], null, 0x0500);
        state.Push(2);
        state.Push(3);

        // [zm 6.3.2] Back in the caller, only its own value remains.
        state.Return(0);

        Assert.Equal(1, state.StackDepth);
        Assert.Equal(1, state.Pop());
    }

    [Fact]
    public void ReferencesToTheStackPointerWorkInPlace()
    {
        // [zm 6.3.4] The editor's note says to test this rather than
        // reason about it, so: with 5 underneath and 7 on top, an in-place
        // read leaves the depth alone, an in-place write replaces the top,
        // and 5 is still there afterward.
        var (state, _) = Fresh(ZMachineVersion.V5);
        state.Push(5);
        state.Push(7);

        Assert.Equal(7, state.ReadVariableInPlace(0));
        Assert.Equal(2, state.StackDepth);

        state.WriteVariableInPlace(0, 8);
        Assert.Equal(2, state.StackDepth);
        Assert.Equal(8, state.Pop());
        Assert.Equal(5, state.Pop());

        // On an empty stack there is no top to read or write.
        Assert.Throws<InvalidOperationException>(() => state.ReadVariableInPlace(0));
        Assert.Throws<InvalidOperationException>(() => state.WriteVariableInPlace(0, 1));
    }

    [Fact]
    public void InPlaceAccessToOtherVariablesIsOrdinary()
    {
        var (state, _) = Fresh(ZMachineVersion.V5);

        state.WriteVariableInPlace(0x10, 99);
        Assert.Equal(99, state.ReadVariableInPlace(0x10));
        Assert.Equal(99, state.ReadVariable(0x10));
    }

    [Theory]
    [InlineData(ZMachineVersion.V3, 10, 20, 30)]
    [InlineData(ZMachineVersion.V5, 0, 0, 0)]
    public void LocalsStartFromTheHeaderOrZero(ZMachineVersion version, int first, int second, int third)
    {
        // [zm 6.4.4] Header values through Version 4, zero from Version 5.
        var (state, _) = Fresh(version);

        state.CallRoutine(Packed(version, RoutineA), [], null, 0);

        Assert.Equal(3, state.CurrentFrame.Locals.Length);
        Assert.Equal(first, state.ReadVariable(1));
        Assert.Equal(second, state.ReadVariable(2));
        Assert.Equal(third, state.ReadVariable(3));
        Assert.Equal(0, state.CurrentFrame.ArgumentCount);
    }

    [Fact]
    public void ArgumentsOverwriteLocalsAndTheRestKeepTheirDefaults()
    {
        var (state, _) = Fresh(ZMachineVersion.V3);

        // [zm 6.4.4] Argument 1 into local 1 and so on, after the defaults
        // are set, so the supplied argument wins. [zm 6.4.4.1] Fewer
        // arguments than locals is fine, and the third keeps its default.
        state.CallRoutine(Packed(ZMachineVersion.V3, RoutineA), [100, 200], null, 0);

        Assert.Equal(100, state.ReadVariable(1));
        Assert.Equal(200, state.ReadVariable(2));
        Assert.Equal(30, state.ReadVariable(3));
        Assert.Equal(2, state.CurrentFrame.ArgumentCount);
    }

    [Fact]
    public void ExtraArgumentsAreThrownAway()
    {
        var (state, _) = Fresh(ZMachineVersion.V5);

        // [zm 6.4.4.1] Five arguments to a routine with three locals.
        state.CallRoutine(Packed(ZMachineVersion.V5, RoutineA), [1, 2, 3, 4, 5], null, 0);

        Assert.Equal(3, state.CurrentFrame.Locals.Length);
        Assert.Equal(3, state.ReadVariable(3));
        Assert.Equal(5, state.CurrentFrame.ArgumentCount);
    }

    [Fact]
    public void CheckArgCountKnowsWhatWasSupplied()
    {
        var (state, _) = Fresh(ZMachineVersion.V5);
        state.CallRoutine(Packed(ZMachineVersion.V5, RoutineA), [1, 0], null, 0);

        // [zm op:check_arg_count] routine(1, 0) supplied two, whatever
        // their values; the third was not supplied even though the local
        // exists.
        Assert.True(state.ArgumentWasSupplied(1));
        Assert.True(state.ArgumentWasSupplied(2));
        Assert.False(state.ArgumentWasSupplied(3));
        Assert.False(state.ArgumentWasSupplied(0));
    }

    [Fact]
    public void ReferringToALocalTheRoutineDoesNotHaveIsAnError()
    {
        var (state, _) = Fresh(ZMachineVersion.V5);
        state.CallRoutine(Packed(ZMachineVersion.V5, RoutineA), [], null, 0);

        // [zm 4.2.2] Three locals, so 4 does not exist. And at the start
        // there are none at all.
        Assert.Throws<InvalidOperationException>(() => state.ReadVariable(4));
        Assert.Throws<InvalidOperationException>(() => state.WriteVariable(4, 1));

        state.Return(0);
        Assert.Throws<InvalidOperationException>(() => state.ReadVariable(1));
    }

    [Fact]
    public void CallingPackedAddressZeroReturnsFalseAtOnce()
    {
        var (state, _) = Fresh(ZMachineVersion.V5);
        state.WriteVariable(0x10, 0xFFFF);

        // [zm 6.4.3] Nothing happens, false is stored, and execution is
        // already back at the return address with no new frame.
        state.CallRoutine(0, [1, 2], 0x10, 0x0777);

        Assert.Equal(0, state.ReadVariable(0x10));
        Assert.Equal(0x0777, state.ProgramCounter);
        Assert.Equal(1, state.FrameNumber);
    }

    [Fact]
    public void ReturnStoresTheResultAndPreservesTheCaller()
    {
        var (state, _) = Fresh(ZMachineVersion.V5);
        state.WriteVariable(0x10, 1);
        state.Push(9);

        state.CallRoutine(Packed(ZMachineVersion.V5, RoutineA), [5], 0x10, 0x0600);
        state.WriteVariable(1, 77);

        // [zm 6.4.2] and [zm 6.4.5] The result lands in the caller's
        // variable, the caller's stack is untouched, and the program
        // counter is back where the call said.
        state.Return(0x4242);

        Assert.Equal(0x4242, state.ReadVariable(0x10));
        Assert.Equal(0x0600, state.ProgramCounter);
        Assert.Equal(1, state.StackDepth);
        Assert.Equal(9, state.Pop());
    }

    [Fact]
    public void AResultStoredToTheStackGoesOnTheCallersStack()
    {
        var (state, _) = Fresh(ZMachineVersion.V5);

        state.CallRoutine(Packed(ZMachineVersion.V5, RoutineB), [], 0, 0);
        state.Push(1);
        state.Push(2);
        state.Return(0x1111);

        // [zm 6.3.2] The routine's own 1 and 2 are gone, and only the
        // result is on the caller's stack.
        Assert.Equal(1, state.StackDepth);
        Assert.Equal(0x1111, state.Pop());
    }

    [Fact]
    public void ADiscardedResultGoesNowhere()
    {
        var (state, _) = Fresh(ZMachineVersion.V5);
        state.Push(3);

        // [zm 6.4.1] call_vn throws the result away: no store variable.
        state.CallRoutine(Packed(ZMachineVersion.V5, RoutineB), [], null, 0);
        state.Return(0x9999);

        Assert.Equal(1, state.StackDepth);
        Assert.Equal(3, state.Pop());
    }

    [Fact]
    public void ReturningFromTheStartingRoutineIsIllegal()
    {
        // [zm 5.5]
        var (state, _) = Fresh(ZMachineVersion.V5);

        Assert.Throws<InvalidOperationException>(() => state.Return(0));
    }

    [Fact]
    public void CatchAndThrowUnwindTheCallChain()
    {
        var (state, _) = Fresh(ZMachineVersion.V5);
        var packed = Packed(ZMachineVersion.V5, RoutineB);

        // Three routines deep, and the middle one catches its frame.
        state.CallRoutine(packed, [], 0x10, 0x0601);
        state.CallRoutine(packed, [], 0x11, 0x0602);
        var caught = state.FrameNumber;
        state.Push(5);
        state.CallRoutine(packed, [], 0x12, 0x0603);
        state.CallRoutine(packed, [], 0x13, 0x0604);

        Assert.Equal(5, state.FrameNumber);

        // [zm 6.5] and [zm op:throw] Back to the frame that caught, then
        // return from it: its own stack is discarded, its result goes to
        // its caller's variable, and execution resumes after its call.
        state.ThrowTo(caught, 0x2222);

        Assert.Equal(2, state.FrameNumber);
        Assert.Equal(0x2222, state.ReadVariable(0x11));
        Assert.Equal(0x0602, state.ProgramCounter);
        Assert.Equal(0, state.StackDepth);
    }

    [Fact]
    public void ThrowingToAFrameThatDoesNotExistIsAnError()
    {
        var (state, _) = Fresh(ZMachineVersion.V5);

        Assert.Throws<InvalidOperationException>(() => state.ThrowTo(0, 0));
        Assert.Throws<InvalidOperationException>(() => state.ThrowTo(2, 0));
    }

    [Fact]
    public void GamesMayWriteDynamicMemoryButNotStatic()
    {
        var (state, memory) = Fresh(ZMachineVersion.V5);

        // [zm 1.1.1] and [zm 1.1.2] Dynamic memory ends at $400 here.
        state.WriteByte(0x03FF, 0xAA);
        state.WriteWord(0x0200, 0xBBCC);
        Assert.Equal(0xAA, memory.ReadByte(0x03FF));
        Assert.Equal(0xBBCC, state.ReadWord(0x0200));

        Assert.Throws<InvalidOperationException>(() => state.WriteByte(0x0400, 1));
        Assert.Throws<InvalidOperationException>(() => state.WriteWord(0x03FF, 1));

        // Reading static memory is fine.
        Assert.Equal(3, state.ReadByte(RoutineA));
    }

    [Fact]
    public void RestartRestoresMemoryButKeepsFlags2()
    {
        var (state, memory) = Fresh(ZMachineVersion.V5);

        state.WriteVariable(0x10, 0x1234);
        state.Push(1);
        state.CallRoutine(Packed(ZMachineVersion.V5, RoutineA), [], null, 0);
        memory.WriteWord(0x10, 0x0001);

        // [zm 6.1.3] Everything back to the story file and the stack
        // emptied, except that Flags 2 survives.
        state.Restart();

        Assert.Equal(0, state.ReadVariable(0x10));
        Assert.Equal(1, state.FrameNumber);
        Assert.Equal(0, state.StackDepth);
        Assert.Equal(0x0500, state.ProgramCounter);
        Assert.Equal(0x0001, memory.ReadWord(0x10));
    }

    [Fact]
    public void AUserStackCountsSpareSlotsDown()
    {
        var (_, memory) = Fresh(ZMachineVersion.V6);

        // [zm 6.6] A table whose first word is 3: three spare slots.
        const int table = 0x300;
        memory.WriteWord(table, 3);

        Assert.True(UserStackTable.TryPush(memory, table, 0xAAAA));
        Assert.True(UserStackTable.TryPush(memory, table, 0xBBBB));
        Assert.True(UserStackTable.TryPush(memory, table, 0xCCCC));
        Assert.Equal(0, memory.ReadWord(table));

        // Values fill from the far end of the table toward the count.
        Assert.Equal(0xAAAA, memory.ReadWord(table + 6));
        Assert.Equal(0xCCCC, memory.ReadWord(table + 2));

        // [zm op:push_stack] Full, so nothing happens and no branch.
        Assert.False(UserStackTable.TryPush(memory, table, 0xDDDD));
        Assert.Equal(0, memory.ReadWord(table));

        // [zm op:pull] Last in, first out.
        Assert.Equal(0xCCCC, UserStackTable.Pull(memory, table));
        Assert.Equal(1, memory.ReadWord(table));

        // [zm op:pop_stack] Two more thrown away, back to empty.
        UserStackTable.Pop(memory, table, 2);
        Assert.Equal(3, memory.ReadWord(table));
    }
}

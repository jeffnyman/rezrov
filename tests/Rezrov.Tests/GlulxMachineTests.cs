using Rezrov.Glulx;
using Rezrov.Glulx.Execution;
using Rezrov.Glulx.Instructions;
using static Rezrov.Tests.GlulxAssembler;

namespace Rezrov.Tests;

/// <summary>
/// The machine's control flow and state: calls and returns, the call
/// frame, branches, catch and throw, restart, and the gestalt answers.
/// </summary>
public class GlulxMachineTests
{
    private static GlulxAssembler Program(params (byte Size, byte Count)[] locals) =>
        new GlulxAssembler().Function("main", FunctionType.LocalArguments, locals);

    [Fact]
    public void AddsAndReturnsFromTheStartFunction()
    {
        var machine = GlulxRun.Run(Program().Op(Opcode.Add, C(1), C(2), Ram(0)).Return(C(0)));

        // [glulx op:return] Returning from the top-level function ends
        // execution, and the stack is empty again.
        Assert.Equal(3u, machine.Ram(0));
        Assert.True(machine.HasQuit);
        Assert.Equal(2, machine.InstructionsExecuted);
        Assert.Equal(0u, machine.Stack.StackPointer);
    }

    [Fact]
    public void QuitStopsBeforeTheNextInstruction()
    {
        var machine = GlulxRun.Run(Program().Op(Opcode.Copy, C(1), Ram(0)).Quit().Op(Opcode.Copy, C(2), Ram(0)));

        Assert.Equal(1u, machine.Ram(0));
        Assert.True(machine.HasQuit);
    }

    [Fact]
    public void TheCallFrameIsLaidOutAsTheSpecificationSays()
    {
        // [glulx #callframe] The example: three 8-bit locals then six
        // 16-bit ones give a format of eight bytes and sixteen bytes of
        // locals with a padding byte after the third.
        var machine = GlulxRun.Machine(Program((1, 3), (2, 6)).Return(C(0)));
        var stack = machine.Stack;

        Assert.Equal(0u, stack.FramePointer);
        Assert.Equal(32u, stack.ReadWord(0));
        Assert.Equal(16u, stack.ReadWord(4));
        Assert.Equal(32u, stack.FrameLength);
        Assert.Equal(16u, stack.LocalsPosition);
        Assert.Equal(new byte[] { 1, 3, 2, 6, 0, 0, 0, 0 }, stack.Contents[8..16].ToArray());
        Assert.Equal(32u, stack.StackPointer);
        Assert.Equal(0u, stack.Count);
    }

    [Fact]
    public void ArgumentsAreWrittenIntoLocalsInOrder()
    {
        // [glulx #function] Type C1: written into the locals, extras
        // dropped, unfilled locals zero.
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.CallFIII, At("two"), C(10), C(20), C(30), Ram(0))
            .Op(Opcode.CallFII, At("three"), C(10), C(20), Ram(4))
            .Return(C(0))
            .Function("two", FunctionType.LocalArguments, (4, 2))
            .Op(Opcode.Sub, Loc(0), Loc(4), Sp)
            .Return(Sp)
            .Function("three", FunctionType.LocalArguments, (4, 3))
            .Op(Opcode.Add, Loc(8), C(100), Sp)
            .Return(Sp));

        Assert.Equal(0xFFFFFFF6u, machine.Ram(0));
        Assert.Equal(100u, machine.Ram(4));
    }

    [Fact]
    public void ArgumentsToNarrowLocalsAreTruncated()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.CallFII, At("f"), C(0x1234), C(0x12345), Ram(0))
            .Return(C(0))
            .Function("f", FunctionType.LocalArguments, (1, 1), (2, 1))
            .Op(Opcode.CopyB, Loc(0), Sp)
            .Op(Opcode.CopyS, Loc(2), Sp)
            .Op(Opcode.Add, Sp, Sp, Sp)
            .Return(Sp));

        // [glulx #function] 1234 into a byte is 34, 12345 into a short
        // is 2345, and [glulx #callframe] the short sits at offset 2.
        Assert.Equal(0x34u + 0x2345u, machine.Ram(0));
    }

    [Fact]
    public void StackArgumentFunctionsFindTheCountAndArgumentsOnTheStack()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.CallFII, At("f"), C(7), C(9), Discard)
            .Return(C(0))
            .Function("f", FunctionType.StackArguments)
            .Op(Opcode.StkCount, Ram(0))
            .Op(Opcode.Copy, Sp, Ram(4))
            .Op(Opcode.Copy, Sp, Ram(8))
            .Op(Opcode.Copy, Sp, Ram(12))
            .Return(C(0)));

        // [glulx #function] Type C0: the count on top, then the first
        // argument, then the second. [glulx op:stkcount] The count of
        // values is the argument count plus one.
        Assert.Equal(3u, machine.Ram(0));
        Assert.Equal(2u, machine.Ram(4));
        Assert.Equal(7u, machine.Ram(8));
        Assert.Equal(9u, machine.Ram(12));
    }

    [Fact]
    public void CallTakesItsArgumentsFromTheStackFirstOnTop()
    {
        // [glulx op:call] Pushed in backward order: the last argument
        // first, the first argument topmost.
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.Copy, C(9), Sp)
            .Op(Opcode.Copy, C(7), Sp)
            .Op(Opcode.Call, At("f"), C(2), Ram(0))
            .Op(Opcode.StkCount, Ram(4))
            .Return(C(0))
            .Function("f", FunctionType.LocalArguments, (4, 2))
            .Op(Opcode.Sub, Loc(0), Loc(4), Sp)
            .Return(Sp));

        Assert.Equal(0xFFFFFFFEu, machine.Ram(0));
        Assert.Equal(0u, machine.Ram(4));
    }

    [Fact]
    public void AReturnValueGoesToEveryKindOfDestination()
    {
        var machine = GlulxRun.Run(Program((4, 1))
            .Op(Opcode.CallF, At("five"), Loc(0))
            .Op(Opcode.Copy, Loc(0), Ram(0))
            .Op(Opcode.CallF, At("five"), Sp)
            .Op(Opcode.Copy, Sp, Ram(4))
            .Op(Opcode.CallF, At("five"), Mem(GlulxRun.RamStart + 8))
            .Op(Opcode.CallF, At("five"), Ram(12))
            .Op(Opcode.CallF, At("five"), Discard)
            .Op(Opcode.StkCount, Ram(16))
            .Return(C(0))
            .Function("five")
            .Return(C(5)));

        // [glulx #callstub] Local, stack, memory, memory by RAM offset,
        // and no store at all.
        Assert.Equal(5u, machine.Ram(0));
        Assert.Equal(5u, machine.Ram(4));
        Assert.Equal(5u, machine.Ram(8));
        Assert.Equal(5u, machine.Ram(12));
        Assert.Equal(0u, machine.Ram(16));
    }

    [Fact]
    public void CallingSomethingThatIsNotAFunctionIsFatal()
    {
        var e = Assert.Throws<GlulxException>(() => GlulxRun.Run(Program().Op(Opcode.CallF, C(GlulxRun.RamStart), Discard)));
        Assert.Contains("Not a function", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RunawayRecursionOverflowsTheStack()
    {
        // [glulx #stack] The stack has the size the header asked for.
        var e = Assert.Throws<GlulxException>(() => GlulxRun.Run(Program()
            .Op(Opcode.CallF, At("f"), Discard)
            .Function("f")
            .Op(Opcode.CallF, At("f"), Discard)));
        Assert.Contains("Stack overflow", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PoppingBelowTheFrameIsFatal()
    {
        // [glulx #callframe] It is illegal to pop back beyond the frame.
        var e = Assert.Throws<GlulxException>(() => GlulxRun.Run(Program().Op(Opcode.Copy, Sp, Ram(0))));
        Assert.Contains("Stack underflow", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(4u)]
    [InlineData(2u)]
    [InlineData(0xFFFFFFFCu)]
    public void ALocalOutsideTheLocalsSegmentIsFatal(uint offset)
    {
        // [glulx #instruction] Whether read as negative or as very
        // large, FFFFFFFC is outside the segment, and so is any field
        // that would end past it.
        var e = Assert.Throws<GlulxException>(() => GlulxRun.Run(Program((4, 1)).Op(Opcode.Copy, C(1), Loc(offset))));
        Assert.Contains("outside", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TailCallReplacesTheFrameAndReturnsToTheOriginalCaller()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.CallFI, At("f"), C(5), Ram(0))
            .Op(Opcode.Copy, C(3), Sp)
            .Op(Opcode.TailCall, At("g"), C(1))
            .Function("f", FunctionType.LocalArguments, (4, 1))
            .Op(Opcode.Copy, Loc(0), Sp)
            .Op(Opcode.TailCall, At("g"), C(1))
            .Function("g", FunctionType.LocalArguments, (4, 1))
            .Op(Opcode.Mul, Loc(0), C(2), Sp)
            .Return(Sp));

        // [glulx op:tailcall] g's return goes to f's caller, and a
        // tailcall from the top level makes g the top level, so its
        // return ends execution.
        Assert.Equal(10u, machine.Ram(0));
        Assert.True(machine.HasQuit);
        Assert.Equal(0u, machine.Stack.StackPointer);
    }

    [Fact]
    public void BranchOffsetsZeroAndOneReturn()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.Copy, C(9), Ram(4))
            .Op(Opcode.CallF, At("one"), Ram(0))
            .Op(Opcode.CallF, At("zero"), Ram(4))
            .Return(C(0))
            .Function("one")
            .Op(Opcode.Jz, C(0), C(1))
            .Return(C(7))
            .Function("zero")
            .Op(Opcode.Jump, C(0))
            .Return(C(7)));

        // [glulx #opcodes_branch] Offset 1 is return 1, offset 0 is
        // return 0, and neither reaches the instruction after.
        Assert.Equal(1u, machine.Ram(0));
        Assert.Equal(0u, machine.Ram(4));
    }

    [Fact]
    public void BranchesGoByOffsetForwardAndBack()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.Jeq, C(1), C(1), To("skip"))
            .Op(Opcode.Copy, C(1), Ram(0))
            .Label("skip")
            .Op(Opcode.Copy, C(2), Ram(4))
            .Op(Opcode.Copy, C(0), Ram(8))
            .Label("loop")
            .Op(Opcode.Add, Ram(8), C(1), Ram(8))
            .Op(Opcode.Jlt, Ram(8), C(5), To("loop"))
            .Return(C(0)));

        Assert.Equal(0u, machine.Ram(0));
        Assert.Equal(2u, machine.Ram(4));
        Assert.Equal(5u, machine.Ram(8));
    }

    [Theory]
    [InlineData(Opcode.Jlt, -1, 1, true)]
    [InlineData(Opcode.Jltu, -1, 1, false)]
    [InlineData(Opcode.Jgt, -1, 1, false)]
    [InlineData(Opcode.Jgtu, -1, 1, true)]
    [InlineData(Opcode.Jge, 5, 5, true)]
    [InlineData(Opcode.Jle, 5, 5, true)]
    [InlineData(Opcode.Jgeu, 0, 0, true)]
    [InlineData(Opcode.Jleu, 1, 0, false)]
    [InlineData(Opcode.Jne, 1, 2, true)]
    [InlineData(Opcode.Jeq, 1, 2, false)]
    [InlineData(Opcode.Jnz, 0, 0, false)]
    public void ComparisonsAreSignedOrUnsignedAsNamed(Opcode opcode, int left, int right, bool taken)
    {
        var code = Program();
        if (opcode == Opcode.Jnz)
        {
            code.Op(opcode, C(left), To("taken"));
        }
        else
        {
            code.Op(opcode, C(left), C(right), To("taken"));
        }

        var machine = GlulxRun.Run(code
            .Op(Opcode.Copy, C(1), Ram(0))
            .Return(C(0))
            .Label("taken")
            .Op(Opcode.Copy, C(2), Ram(0))
            .Return(C(0)));

        // [glulx #opcodes_branch] The u variants compare as unsigned.
        Assert.Equal(taken ? 2u : 1u, machine.Ram(0));
    }

    [Fact]
    public void JumpAbsGoesToAnAddress()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.JumpAbs, At("there"))
            .Op(Opcode.Copy, C(1), Ram(0))
            .Label("there")
            .Return(C(0)));

        Assert.Equal(0u, machine.Ram(0));
    }

    [Fact]
    public void CatchHandsOutATokenAndThrowComesBackToIt()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.Catch, Ram(0), To("handler"))
            .Op(Opcode.Copy, Ram(0), Ram(4))
            .Return(C(0))
            .Label("handler")
            .Op(Opcode.Copy, C(7), Ram(8))
            .Op(Opcode.Throw, C(42), Ram(0)));

        // [glulx #continuations] The catch branches at catch time; the
        // throw stores its value in the catch's destination and carries
        // on after the catch, with the branch ignored.
        Assert.Equal(42u, machine.Ram(0));
        Assert.Equal(42u, machine.Ram(4));
        Assert.Equal(7u, machine.Ram(8));
    }

    [Fact]
    public void TheCatchTokenIsTheStackPointerAboveTheStub()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.Catch, Ram(0), To("end"))
            .Label("end")
            .Return(C(0)));

        // [glulx op:throw] A frame of 12 bytes, then a 16-byte stub.
        Assert.Equal(28u, machine.Ram(0));
    }

    [Fact]
    public void ACatchBranchOfOneReturns()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.CallF, At("f"), Ram(0))
            .Return(C(0))
            .Function("f")
            .Op(Opcode.Catch, Discard, C(1))
            .Return(C(7)));

        // [glulx op:catch] Offset 1 returns 1 at once, invalidating the
        // token.
        Assert.Equal(1u, machine.Ram(0));
    }

    [Fact]
    public void ThrowWithABadTokenIsFatal()
    {
        var e = Assert.Throws<GlulxException>(() => GlulxRun.Run(Program().Op(Opcode.Throw, C(1), C(0x1000))));
        Assert.Contains("Invalid catch token", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RestartResetsMemoryExceptTheProtectedRange()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.Jnz, Ram(0), To("done"))
            .Op(Opcode.Copy, C(1), Ram(0))
            .Op(Opcode.Copy, C(5), Ram(4))
            .Op(Opcode.SetMemSize, C(0xB00), Discard)
            .Op(Opcode.Protect, C(GlulxRun.RamStart), C(4))
            .Op(Opcode.Restart)
            .Label("done")
            .Op(Opcode.GetMemSize, Ram(8))
            .Return(C(0)));

        // [glulx op:restart] Memory, its size, and the stack go back to
        // the start, [glulx op:protect] except the protected range, and
        // the range itself survives.
        Assert.Equal(1u, machine.Ram(0));
        Assert.Equal(0u, machine.Ram(4));
        Assert.Equal(0xA00u, machine.Ram(8));
        Assert.Equal(GlulxRun.RamStart, machine.ProtectedStart);
        Assert.Equal(4u, machine.ProtectedLength);
        Assert.Equal(9, machine.InstructionsExecuted);
    }

    [Fact]
    public void MemoryCanGrowAndShrinkWithinTheRules()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.GetMemSize, Ram(0))
            .Op(Opcode.SetMemSize, C(0xB00), Ram(4))
            .Op(Opcode.GetMemSize, Ram(8))
            .Op(Opcode.Copy, Mem(0xAFC), Ram(12))
            .Op(Opcode.Copy, C(0x77), Mem(0xAFC))
            .Op(Opcode.SetMemSize, C(0xA00), Discard)
            .Return(C(0)));

        // [glulx op:setmemsize] New space is zeroes, success stores
        // zero, and a shrink loses what was above.
        Assert.Equal(0xA00u, machine.Ram(0));
        Assert.Equal(0u, machine.Ram(4));
        Assert.Equal(0xB00u, machine.Ram(8));
        Assert.Equal(0u, machine.Ram(12));
        Assert.Equal(0xA00u, machine.Memory.Length);

        Assert.Throws<GlulxException>(() => GlulxRun.Run(Program().Op(Opcode.SetMemSize, C(0x900), Discard)));
        Assert.Throws<GlulxException>(() => GlulxRun.Run(Program().Op(Opcode.SetMemSize, C(0xA80), Discard)));
    }

    [Theory]
    [InlineData(0u, 0u, 0x00030103u)]
    [InlineData(2u, 0u, 1u)]
    [InlineData(3u, 0u, 0u)]
    [InlineData(4u, 0u, 1u)]
    [InlineData(4u, 1u, 1u)]
    [InlineData(4u, 2u, 1u)]
    [InlineData(5u, 0u, 1u)]
    [InlineData(6u, 0u, 1u)]
    [InlineData(7u, 0u, 0u)]
    [InlineData(11u, 0u, 0u)]
    [InlineData(0x1000u, 0u, 0u)]
    public void GestaltAnswersForWhatIsBuilt(uint selector, uint argument, uint expected)
    {
        var machine = GlulxRun.Run(Program().Op(Opcode.Gestalt, C(selector), C(argument), Ram(0)).Return(C(0)));

        // [glulx #opcodes_misc] Version, memory resizing, all three I/O
        // systems, Unicode, and mzero and mcopy are there; undo, the
        // heap, and floats are not yet; unknown is zero.
        Assert.Equal(expected, machine.Ram(0));
    }

    [Fact]
    public void GestaltReportsTheInterpreterVersion()
    {
        var machine = GlulxRun.Run(Program().Op(Opcode.Gestalt, C(1), C(0), Ram(0)).Return(C(0)));

        // [glulx #opcodes_misc] Packed like the GlulxVersion, from the
        // program's own version.
        Assert.Equal(GlulxMachine.InterpreterVersion, machine.Ram(0));
        Assert.NotEqual(0u, GlulxMachine.InterpreterVersion);
    }

    [Fact]
    public void VerifyChecksTheFile()
    {
        var code = Program().Op(Opcode.Verify, Ram(0)).Return(C(0));

        Assert.Equal(0u, GlulxRun.Run(code).Ram(0));

        var damaged = GlulxRun.File(code);
        damaged[0x600] ^= 1;
        var machine = new GlulxMachine(new GlulxMemory(damaged));
        machine.Run();

        // [glulx op:verify] Zero for a good file, 1 for a problem.
        Assert.Equal(1u, machine.Ram(0));
    }

    [Fact]
    public void DebugTrapHalts()
    {
        var e = Assert.Throws<GlulxException>(() => GlulxRun.Run(Program().Op(Opcode.DebugTrap, C(0x1234))));
        Assert.Contains("debugtrap 00001234", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOpcodeNotBuiltYetSaysSo()
    {
        var e = Assert.Throws<NotSupportedException>(() => GlulxRun.Run(Program().Op(Opcode.MAlloc, C(16), Discard)));
        Assert.Contains("malloc", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SeededRandomNumbersRepeat()
    {
        var code = Program()
            .Op(Opcode.Random, C(1000), Ram(0))
            .Op(Opcode.Random, C(1000), Ram(4))
            .Op(Opcode.SetRandom, C(77))
            .Op(Opcode.Random, C(100), Ram(8))
            .Op(Opcode.SetRandom, C(0))
            .Op(Opcode.Random, C(0), Ram(12))
            .Return(C(0));

        var first = GlulxRun.Run(code, seed: 5);
        var second = GlulxRun.Run(code, seed: 5);
        var other = GlulxRun.Run(code, seed: 6);

        // [glulx op:setrandom] A nonzero seed gives the same sequence
        // every time, in the session's seed and in the game's own.
        Assert.Equal((first.Ram(0), first.Ram(4)), (second.Ram(0), second.Ram(4)));
        Assert.NotEqual((first.Ram(0), first.Ram(4)), (other.Ram(0), other.Ram(4)));
        Assert.Equal(new GlulxRandom(77).InRange(100), first.Ram(8));
        Assert.True(first.Ram(0) < 1000 && first.Ram(4) < 1000);
    }

    [Fact]
    public void RandomRangesFollowTheSignOfTheArgument()
    {
        var random = new GlulxRandom(1);
        var sawNegative = false;

        for (var i = 0; i < 1000; i++)
        {
            // [glulx op:random] 0 to L1-1 for positive, L1+1 to 0 for
            // negative, and anything for zero.
            Assert.InRange(random.InRange(10), 0u, 9u);
            Assert.InRange((int)random.InRange(unchecked((uint)-10)), -9, 0);
            sawNegative |= (int)random.InRange(0) < 0;
        }

        Assert.True(sawNegative);
    }
}

using Rezrov.Glulx;
using Rezrov.Glulx.Execution;
using Rezrov.Glulx.Glk;
using Rezrov.Glulx.Instructions;
using Rezrov.Glulx.Saves;
using static Rezrov.Tests.GlulxAssembler;
using FileMode = Rezrov.Glulx.Glk.FileMode;

namespace Rezrov.Tests;

/// <summary>
/// [glulx #game-state] The opcodes that save and restore the machine:
/// undo in temporary storage, saved games through a Glk stream, and the
/// protected range and the gestalt answers that go with them.
/// </summary>
public class GlulxSaveTests
{
    private const uint StreamOpenMemory = 0x0043;
    private const uint StreamSetPosition = 0x0045;

    // A buffer in RAM for a saved game, above the words the tests
    // leave their results in.
    private const uint Buffer = 0x100;
    private const uint BufferLength = 0x200;

    private static GlulxAssembler Program() => new GlulxAssembler().Function("main");

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

    /// <summary>
    /// A program under the Glk I/O system with a read-write memory
    /// stream over the buffer, its id in RAM word 0.
    /// </summary>
    private static GlulxAssembler WithStream(FileMode mode = FileMode.ReadWrite)
    {
        var code = Program().Op(Opcode.SetIOSys, C(2), C(0));
        return Glk(code, StreamOpenMemory, Ram(0), C(GlulxRun.RamStart + Buffer), C(BufferLength), C((uint)mode), C(0));
    }

    [Fact]
    public void UndoGoesBackToTheSavedStateWithMinusOne()
    {
        // The words written between the save and the restore are
        // protected, or the restore would put them back to zero and
        // say nothing.
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.Protect, C(GlulxRun.RamStart + 8), C(12))
            .Op(Opcode.Copy, C(1), Ram(4))
            .Op(Opcode.SaveUndo, Ram(0))
            .Op(Opcode.Jne, Ram(0), C(0), To("back"))
            .Op(Opcode.Copy, C(2), Ram(4))
            .Op(Opcode.HasUndo, Ram(8))
            .Op(Opcode.RestoreUndo, Ram(12))
            .Op(Opcode.Copy, C(7), Ram(16))
            .Return(C(0))
            .Label("back")
            .Op(Opcode.HasUndo, Ram(20))
            .Return(C(0)));

        // [glulx op:saveundo] 0 the first time and -1 on coming back,
        // in the same place; [glulx op:hasundo] 0 while a state is
        // there and 1 once it has been used; [glulx op:restoreundo]
        // never stores on success, and memory is as it was.
        Assert.Equal(0xFFFFFFFFu, machine.Ram(0));
        Assert.Equal(1u, machine.Ram(4));
        Assert.Equal(0u, machine.Ram(8));
        Assert.Equal(0u, machine.Ram(12));
        Assert.Equal(0u, machine.Ram(16));
        Assert.Equal(1u, machine.Ram(20));
        Assert.Equal(0, machine.Undo.Count);
    }

    [Fact]
    public void UndoWithNothingSavedFails()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.HasUndo, Ram(0))
            .Op(Opcode.DiscardUndo)
            .Op(Opcode.RestoreUndo, Ram(4))
            .Return(C(0)));

        // [glulx op:hasundo] 1 for none, [glulx op:discardundo] nothing
        // to discard does nothing, [glulx op:restoreundo] 1 for failure.
        Assert.Equal(1u, machine.Ram(0));
        Assert.Equal(1u, machine.Ram(4));
    }

    [Fact]
    public void DiscardDropsTheMostRecentState()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.Protect, C(GlulxRun.RamStart + 8), C(8))
            .Op(Opcode.Copy, C(1), Ram(4))
            .Op(Opcode.SaveUndo, Ram(0))
            .Op(Opcode.Jne, Ram(0), C(0), To("back"))
            .Op(Opcode.Copy, C(2), Ram(4))
            .Op(Opcode.SaveUndo, Discard)
            .Op(Opcode.DiscardUndo)
            .Op(Opcode.HasUndo, Ram(8))
            .Op(Opcode.Copy, C(3), Ram(4))
            .Op(Opcode.RestoreUndo, Discard)
            .Op(Opcode.Copy, C(7), Ram(12))
            .Return(C(0))
            .Label("back")
            .Return(C(0)));

        // [glulx op:discardundo] The second state goes, one is still
        // there, and the restore finds the first, coming back through
        // its stub.
        Assert.Equal(0xFFFFFFFFu, machine.Ram(0));
        Assert.Equal(1u, machine.Ram(4));
        Assert.Equal(0u, machine.Ram(8));
        Assert.Equal(0u, machine.Ram(12));
        Assert.Equal(0, machine.Undo.Count);
    }

    [Fact]
    public void TheResultCanGoOnTheStack()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.SaveUndo, Sp)
            .Op(Opcode.Copy, Sp, Ram(0))
            .Op(Opcode.Jne, Ram(0), C(0), To("back"))
            .Op(Opcode.RestoreUndo, Discard)
            .Label("back")
            .Return(C(0)));

        // [glulx #callstub] A stack destination pushes the result, the
        // first time and again after the restore.
        Assert.Equal(0xFFFFFFFFu, machine.Ram(0));
    }

    [Fact]
    public void UndoRestoresLocalsAndTheSizeOfMemory()
    {
        var machine = GlulxRun.Run(new GlulxAssembler().Function("main", FunctionType.LocalArguments, (4, 2))
            .Op(Opcode.Copy, C(5), Loc(0))
            .Op(Opcode.SetMemSize, C(0xB00), Discard)
            .Op(Opcode.Copy, C(0x77), Mem(0xAFC))
            .Op(Opcode.SaveUndo, Loc(4))
            .Op(Opcode.Jne, Loc(4), C(0), To("back"))
            .Op(Opcode.Copy, C(6), Loc(0))
            .Op(Opcode.SetMemSize, C(0xA00), Discard)
            .Op(Opcode.RestoreUndo, Discard)
            .Return(C(0))
            .Label("back")
            .Op(Opcode.Copy, Loc(0), Ram(0))
            .Op(Opcode.GetMemSize, Ram(4))
            .Op(Opcode.Copy, Mem(0xAFC), Ram(8))
            .Op(Opcode.Copy, Loc(4), Ram(12))
            .Return(C(0)));

        // [glulx #saveformat] The stack, locals with it, and the size
        // of memory are part of the state, so a shrink is undone and
        // what was above comes back.
        Assert.Equal(5u, machine.Ram(0));
        Assert.Equal(0xB00u, machine.Ram(4));
        Assert.Equal(0x77u, machine.Ram(8));
        Assert.Equal(0xFFFFFFFFu, machine.Ram(12));
    }

    [Fact]
    public void UndoGoesBackIntoACalledFunction()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.CallFI, At("inner"), C(3), Ram(0))
            .Op(Opcode.Jeq, Ram(0), C(20), To("done"))
            .Op(Opcode.RestoreUndo, Discard)
            .Label("done")
            .Return(C(0))
            .Function("inner", FunctionType.LocalArguments, (4, 1))
            .Op(Opcode.SaveUndo, Ram(4))
            .Op(Opcode.Jne, Ram(4), C(0), To("restored"))
            .Return(C(10))
            .Label("restored")
            .Op(Opcode.Add, Loc(0), C(17), Ram(8))
            .Return(Ram(8)));

        // [glulx #saveformat] The whole stack comes back: the frame of
        // the function that saved, with its argument still in its
        // local, and the stub below it that says where its return
        // value goes.
        Assert.Equal(20u, machine.Ram(0));
        Assert.Equal(0xFFFFFFFFu, machine.Ram(4));
        Assert.Equal(20u, machine.Ram(8));
    }

    [Fact]
    public void TheProtectedRangeSurvivesAnUndo()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.Copy, C(1), Ram(4))
            .Op(Opcode.SaveUndo, Ram(0))
            .Op(Opcode.Jne, Ram(0), C(0), To("back"))
            .Op(Opcode.Copy, C(2), Ram(4))
            .Op(Opcode.Copy, C(3), Ram(8))
            .Op(Opcode.Protect, C(GlulxRun.RamStart + 4), C(4))
            .Op(Opcode.RestoreUndo, Discard)
            .Label("back")
            .Return(C(0)));

        // [glulx op:protect] The range is silently unaffected by a
        // restoreundo; the word beside it is restored.
        Assert.Equal(2u, machine.Ram(4));
        Assert.Equal(0u, machine.Ram(8));
    }

    [Fact]
    public void TemporaryStorageIsBoundedAndTheOldestGoes()
    {
        var machine = GlulxRun.Run(new GlulxAssembler().Function("main", FunctionType.LocalArguments, (4, 1))
            .Label("loop")
            .Op(Opcode.Copy, Loc(0), Ram(4))
            .Op(Opcode.SaveUndo, Discard)
            .Op(Opcode.Add, Loc(0), C(1), Loc(0))
            .Op(Opcode.Jlt, Loc(0), C(GlulxUndoHistory.MaxStates + 5), To("loop"))
            .Return(C(0)));

        // [glulx #game-state] A game may not assume how many levels
        // there are; here the last MaxStates are kept, so going back
        // once finds the state saved on the last pass.
        Assert.Equal(GlulxUndoHistory.MaxStates, machine.Undo.Count);
        Assert.True(machine.Undo.TryPop(out var state));
        Assert.Equal((uint)(GlulxUndoHistory.MaxStates + 4), state.Ram[4..8].Aggregate(0u, (sum, b) => (sum << 8) | b));
    }

    [Fact]
    public void GestaltAnswersUndoAndExtUndo()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.Gestalt, C(3), C(0), Ram(0))
            .Op(Opcode.Gestalt, C(12), C(0), Ram(4))
            .Return(C(0)));

        // [glulx #opcodes_misc] Undo (3) and ExtUndo (12) both 1.
        Assert.Equal(1u, machine.Ram(0));
        Assert.Equal(1u, machine.Ram(4));
    }

    [Fact]
    public void ASavedGameRestoresThroughAGlkStream()
    {
        var code = WithStream()
            .Op(Opcode.Copy, C(1), Ram(4))
            .Op(Opcode.Save, Ram(0), Ram(8))
            .Op(Opcode.Jne, Ram(8), C(-1), To("saved"))
            .Op(Opcode.Copy, C(99), Ram(12))
            .Return(C(0))
            .Label("saved")
            .Op(Opcode.Copy, C(2), Ram(4));
        Glk(code, StreamSetPosition, Discard, Ram(0), C(0), C((uint)SeekMode.Start));
        code.Op(Opcode.Restore, Ram(0), Ram(16))
            .Op(Opcode.Copy, C(7), Ram(20))
            .Return(C(0));

        var machine = GlulxRun.Run(code, glk: new GlkLibrary(new RecordingGlkDisplay()));

        // [glulx op:save] 0 when saved, then [glulx op:restore] the
        // restore never stores and execution continues after the save
        // with -1 in its result and memory as it was then.
        Assert.Equal(0xFFFFFFFFu, machine.Ram(8));
        Assert.Equal(1u, machine.Ram(4));
        Assert.Equal(99u, machine.Ram(12));
        Assert.Equal(0u, machine.Ram(16));
        Assert.Equal(0u, machine.Ram(20));
        Assert.Null(machine.LastRestoreError);
        Assert.Empty(machine.Glk.Warnings);
    }

    [Fact]
    public void TheStreamHoldsAQuetzalFileEndingInTheStub()
    {
        var machine = GlulxRun.Run(WithStream()
            .Op(Opcode.Save, Ram(0), Ram(8))
            .Return(C(0)), glk: new GlkLibrary(new RecordingGlkDisplay()));

        Assert.Equal(0u, machine.Ram(8));

        // [glulx #saveformat] What was written is a saved game of this
        // game, whose stack ends with the stub the save pushed: a
        // memory destination, RAM word 8, and the frame pointer 0 of
        // the main function.
        var stream = (GlkMemoryStream)machine.Glk.Streams.Find(machine.Ram(0))!;
        var file = machine.Memory.Slice(GlulxRun.RamStart + Buffer, stream.Position).ToArray();
        var state = GlulxQuetzal.Read(new MemoryStream(file), machine.Memory);

        Assert.Equal(0xA00u, state.MemorySize);
        Assert.Equal(0x600, state.Ram.Length);
        Assert.Equal((uint)DestinationType.Memory, ReadWord(state.Stack, state.Stack.Length - 16));
        Assert.Equal(GlulxRun.RamStart + 8, ReadWord(state.Stack, state.Stack.Length - 12));
        Assert.Equal(0u, ReadWord(state.Stack, state.Stack.Length - 4));
    }

    [Fact]
    public void SaveFailsWithoutAStreamToWriteTo()
    {
        var machine = GlulxRun.Run(WithStream(FileMode.Read)
            .Op(Opcode.Save, C(0), Ram(8))
            .Op(Opcode.Save, C(99), Ram(12))
            .Op(Opcode.Save, Ram(0), Ram(16))
            .Return(C(0)), glk: new GlkLibrary(new RecordingGlkDisplay()));

        // [glulx op:save] 1 for no stream, an unknown one, or one that
        // cannot be written.
        Assert.Equal(1u, machine.Ram(8));
        Assert.Equal(1u, machine.Ram(12));
        Assert.Equal(1u, machine.Ram(16));
    }

    [Fact]
    public void RestoreFailsOnWhatIsNotASavedGame()
    {
        var machine = GlulxRun.Run(WithStream()
            .Op(Opcode.Restore, C(0), Ram(8))
            .Op(Opcode.Restore, Ram(0), Ram(12))
            .Return(C(0)), glk: new GlkLibrary(new RecordingGlkDisplay()));

        // [glulx op:restore] 1 for no stream and 1 for a stream with
        // nothing in it, and the reason is kept.
        Assert.Equal(1u, machine.Ram(8));
        Assert.Equal(1u, machine.Ram(12));
        Assert.Equal("This is not a Quetzal saved game.", machine.LastRestoreError);
    }

    [Fact]
    public void SaveAndRestoreAreIllegalOutsideTheGlkSystem()
    {
        // [glulx op:save] Under the null and filter systems there is
        // nowhere to write the state.
        Assert.Throws<GlulxException>(() => GlulxRun.Run(Program().Op(Opcode.Save, C(1), Discard)));
        Assert.Throws<GlulxException>(() => GlulxRun.Run(Program().Op(Opcode.Restore, C(1), Discard)));
    }

    [Fact]
    public void AStateFromAnotherStackDoesNotFit()
    {
        var machine = GlulxRun.Machine(Program().Return(C(0)));
        var state = new GlulxSavedState(0xA00, new byte[0x600], new byte[0x404]);

        Assert.Throws<GlulxException>(() => machine.Apply(state, 0));
        Assert.Throws<GlulxException>(() => machine.Stack.Load(new byte[12]));
        Assert.Throws<GlulxException>(() => machine.Stack.Load(new byte[18]));
    }

    [Fact]
    public void AProtectedRangeAboveTheOldEndOfMemoryKeepsZeroes()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.SetMemSize, C(0xB00), Discard)
            .Op(Opcode.Copy, C(0x01020304), Mem(0xA80))
            .Op(Opcode.SaveUndo, Ram(0))
            .Op(Opcode.Jne, Ram(0), C(0), To("back"))
            .Op(Opcode.SetMemSize, C(0xA00), Discard)
            .Op(Opcode.Protect, C(0xA81), C(2))
            .Op(Opcode.RestoreUndo, Discard)
            .Return(C(0))
            .Label("back")
            .Op(Opcode.Copy, Mem(0xA80), Ram(4))
            .Op(Opcode.GetMemSize, Ram(8))
            .Return(C(0)));

        // [glulx op:protect] The range is unaffected by the restore, and
        // what it holds is what memory held once it was the saved size
        // again: zeroes, since it had been shrunk below the range, as
        // glulxercise's undomemsize test expects.
        Assert.Equal(0x01000004u, machine.Ram(4));
        Assert.Equal(0xB00u, machine.Ram(8));
    }

    private static uint ReadWord(byte[] bytes, int at) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at));
}

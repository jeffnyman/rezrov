using Rezrov.Glulx;
using Rezrov.Glulx.Instructions;
using Rezrov.Glulx.Saves;
using static Rezrov.Tests.GlulxAssembler;

namespace Rezrov.Tests;

/// <summary>
/// [glulx #opcodes_malloc] The allocation heap: activation at the end
/// of memory, reuse and merging of freed blocks, how memory grows,
/// what the heap forbids, and the heap through restart, undo, and a
/// saved state.
/// </summary>
public class GlulxHeapTests
{
    // Memory ends at $A00 in the test layout, so that is where the heap
    // begins.
    private const uint EndMem = 0xA00;

    private static GlulxAssembler Program() => new GlulxAssembler().Function("main");

    [Fact]
    public void AllocationActivatesTheHeapAndFreeingTheLastBlockEndsIt()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.Gestalt, C(8), C(0), Ram(0))
            .Op(Opcode.MAlloc, C(16), Ram(4))
            .Op(Opcode.Gestalt, C(8), C(0), Ram(8))
            .Op(Opcode.GetMemSize, Ram(12))
            .Op(Opcode.MAlloc, C(16), Ram(16))
            .Op(Opcode.MFree, Ram(4))
            .Op(Opcode.MFree, Ram(16))
            .Op(Opcode.Gestalt, C(8), C(0), Ram(20))
            .Op(Opcode.GetMemSize, Ram(24))
            .Op(Opcode.Gestalt, C(7), C(0), Ram(28))
            .Return(C(0)));

        // [glulx #opcodes_malloc] The first block is at the old end of
        // memory, which becomes the heap start the MAllocHeap gestalt
        // answers, memory grows by 256, blocks do not overlap, and
        // freeing the last block shrinks memory back and ends the heap.
        Assert.Equal(0u, machine.Ram(0));
        Assert.Equal(EndMem, machine.Ram(4));
        Assert.Equal(EndMem, machine.Ram(8));
        Assert.Equal(EndMem + 0x100, machine.Ram(12));
        Assert.Equal(EndMem + 16, machine.Ram(16));
        Assert.Equal(0u, machine.Ram(20));
        Assert.Equal(EndMem, machine.Ram(24));
        Assert.Equal(1u, machine.Ram(28));
        Assert.False(machine.Heap.IsActive);
        Assert.Equal(EndMem, machine.Memory.Length);
    }

    [Fact]
    public void FreedSpaceIsReusedAndNeighborsMerge()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.MAlloc, C(16), Ram(0))
            .Op(Opcode.MAlloc, C(16), Ram(4))
            .Op(Opcode.MAlloc, C(16), Ram(8))
            .Op(Opcode.MFree, Ram(0))
            .Op(Opcode.MFree, Ram(4))
            .Op(Opcode.MAlloc, C(32), Ram(12))
            .Op(Opcode.MAlloc, C(16), Ram(16))
            .Op(Opcode.MFree, Ram(12))
            .Op(Opcode.MAlloc, C(8), Ram(20))
            .Return(C(0)));

        // Two freed neighbors make room for a block their size; the
        // next block goes after the one still in use; and a small block
        // takes the first free space that fits.
        Assert.Equal(EndMem, machine.Ram(12));
        Assert.Equal(EndMem + 48, machine.Ram(16));
        Assert.Equal(EndMem, machine.Ram(20));
        Assert.Equal(3, machine.Heap.Count);
        Assert.Equal(EndMem + 0x100, machine.Memory.Length);
    }

    [Fact]
    public void MemoryGrowsByTheLargestOfTheHeapTheRequestAnd256()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.MAlloc, C(1000), Ram(0))
            .Op(Opcode.GetMemSize, Ram(4))
            .Op(Opcode.MAlloc, C(100), Ram(8))
            .Op(Opcode.GetMemSize, Ram(12))
            .Return(C(0)));

        // A thousand bytes need 1024 more memory; then a hundred do not
        // fit in the 24 left, so memory doubles, and the leftover joins
        // the new space so the block starts right after the first.
        Assert.Equal(EndMem, machine.Ram(0));
        Assert.Equal(EndMem + 1024, machine.Ram(4));
        Assert.Equal(EndMem + 1000, machine.Ram(8));
        Assert.Equal(EndMem + 2048, machine.Ram(12));
    }

    [Fact]
    public void MemoryIsTheHeapsToResizeWhileItIsActive()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.MAlloc, C(16), Ram(0))
            .Op(Opcode.MFree, Ram(0))
            .Op(Opcode.SetMemSize, C(0xB00), Ram(4))
            .Op(Opcode.GetMemSize, Ram(8))
            .Return(C(0)));

        // [glulx op:setmemsize] Legal again once the heap is inactive.
        Assert.Equal(0u, machine.Ram(4));
        Assert.Equal(0xB00u, machine.Ram(8));

        Assert.Throws<GlulxException>(() => GlulxRun.Run(Program()
            .Op(Opcode.MAlloc, C(16), Discard)
            .Op(Opcode.SetMemSize, C(0xB00), Discard)
            .Return(C(0))));
    }

    [Fact]
    public void ZeroLengthsAndBadFreesAreFatal()
    {
        // [glulx op:malloc] The length must be positive; [glulx
        // op:mfree] the address must be an extant block.
        Assert.Throws<GlulxException>(() => GlulxRun.Run(Program().Op(Opcode.MAlloc, C(0), Discard).Return(C(0))));
        Assert.Throws<GlulxException>(() => GlulxRun.Run(Program().Op(Opcode.MFree, C(EndMem)).Return(C(0))));
        Assert.Throws<GlulxException>(() => GlulxRun.Run(Program()
            .Op(Opcode.MAlloc, C(16), Ram(0))
            .Op(Opcode.MAlloc, C(16), Ram(4))
            .Op(Opcode.MFree, Ram(0))
            .Op(Opcode.MFree, Ram(0))
            .Return(C(0))));
        Assert.Throws<GlulxException>(() => GlulxRun.Run(Program()
            .Op(Opcode.MAlloc, C(16), Ram(0))
            .Op(Opcode.MAlloc, C(16), Ram(4))
            .Op(Opcode.MFree, C(EndMem + 8))
            .Return(C(0))));
    }

    [Fact]
    public void AnAllocationTooLargeForMemoryFails()
    {
        // [glulx op:malloc] Zero for a failed allocation, and nothing
        // else changes.
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.MAlloc, C(0x7FFFFFF0), Ram(0))
            .Op(Opcode.GetMemSize, Ram(4))
            .Op(Opcode.Gestalt, C(8), C(0), Ram(8))
            .Return(C(0)));

        Assert.Equal(0u, machine.Ram(0));
        Assert.Equal(EndMem, machine.Ram(4));
        Assert.Equal(0u, machine.Ram(8));
    }

    [Fact]
    public void RestartClearsTheHeap()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.Jnz, Ram(0), To("done"))
            .Op(Opcode.Copy, C(1), Ram(0))
            .Op(Opcode.Protect, C(GlulxRun.RamStart), C(4))
            .Op(Opcode.MAlloc, C(16), Discard)
            .Op(Opcode.Restart)
            .Label("done")
            .Op(Opcode.GetMemSize, Ram(4))
            .Op(Opcode.Gestalt, C(8), C(0), Ram(8))
            .Return(C(0)));

        // [glulx op:restart] Memory is back to ENDMEM with no heap.
        Assert.Equal(EndMem, machine.Ram(4));
        Assert.Equal(0u, machine.Ram(8));
        Assert.False(machine.Heap.IsActive);
    }

    [Fact]
    public void UndoRestoresTheHeap()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.MAlloc, C(16), Ram(0))
            .Op(Opcode.SaveUndo, Ram(4))
            .Op(Opcode.Jne, Ram(4), C(0), To("back"))
            .Op(Opcode.MFree, Ram(0))
            .Op(Opcode.RestoreUndo, Discard)
            .Return(C(0))
            .Label("back")
            .Op(Opcode.GetMemSize, Ram(8))
            .Op(Opcode.Gestalt, C(8), C(0), Ram(12))
            .Op(Opcode.MAlloc, C(16), Ram(16))
            .Return(C(0)));

        // [glulx #opcodes_malloc] The heap state is part of the saved
        // game state: after the undo the block freed since is extant
        // again, memory is its old size, and a new block goes after it.
        Assert.Equal(EndMem + 0x100, machine.Ram(8));
        Assert.Equal(EndMem, machine.Ram(12));
        Assert.Equal(EndMem + 16, machine.Ram(16));
        Assert.Equal(2, machine.Heap.Count);
    }

    [Fact]
    public void ASavedHeapMustFitTheRestoredMemory()
    {
        var machine = GlulxRun.Machine(Program().Return(C(0)));
        var stack = new byte[16];
        GlulxSavedState State(GlulxHeapSummary heap) => new(0xB00, new byte[0x700], stack, heap);

        // [glulx #saveformat] Blocks out of order, outside memory, or
        // before the heap start, and a start below ENDMEM, are all
        // refused; a good summary is applied.
        Assert.Throws<GlulxException>(() => machine.Apply(State(new GlulxHeapSummary(EndMem, [(EndMem + 16, 16), (EndMem, 16)])), 0));
        Assert.Throws<GlulxException>(() => machine.Apply(State(new GlulxHeapSummary(EndMem, [(EndMem + 0xF8, 16)])), 0));
        Assert.Throws<GlulxException>(() => machine.Apply(State(new GlulxHeapSummary(EndMem, [(EndMem - 16, 16)])), 0));
        Assert.Throws<GlulxException>(() => machine.Apply(State(new GlulxHeapSummary(0x900, [(0x900, 16)])), 0));
        Assert.False(machine.Heap.IsActive);

        machine.Apply(State(new GlulxHeapSummary(EndMem, [(EndMem, 16), (EndMem + 32, 16)])), 0);
        Assert.True(machine.Heap.IsActive);
        Assert.Equal(2, machine.Heap.Count);
        Assert.Equal(EndMem + 16, machine.Heap.Allocate(16));
        Assert.Equal(EndMem + 48, machine.Heap.Allocate(16));
        Assert.Equal(EndMem, machine.Heap.Summary()!.Start);
        Assert.Equal([(EndMem, 16u), (EndMem + 16, 16u), (EndMem + 32, 16u), (EndMem + 48, 16u)], machine.Heap.Summary()!.Blocks);
    }
}

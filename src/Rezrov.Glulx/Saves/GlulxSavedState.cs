namespace Rezrov.Glulx.Saves;

/// <summary>
/// The state of a Glulx game at one moment: the size of memory, the
/// contents of RAM, the whole stack with a call stub on top saying
/// where execution continues, and the heap if it is active.
/// </summary>
/// <remarks>
/// [glulx #saveformat] This is what a saved game holds and what undo
/// keeps: memory from RAMSTART to its current end, and the stack from
/// the bottom to the stack pointer with every value big-endian, which
/// is how <see cref="Execution.GlulxStackSpace"/> keeps it anyway. The
/// stub on top is pushed by the opcode that saves and popped by the one
/// that restores, or by the saving opcode itself when it continues, and
/// popping it stores the operation's result where the opcode's store
/// operand said and puts the program counter and frame pointer back.
/// [glulx #opcodes_malloc] The heap's state is part of the saved game
/// too: its start and the extant blocks.
///
/// [glulx #saveformat] Nothing else is part of the state: not the Glk
/// objects, the protected range, the random number generator, the I/O
/// system, or the decoding table address, and a restore leaves all of
/// them as they are.
/// </remarks>
/// <param name="MemorySize">
/// [glulx op:setmemsize] The size of memory when the state was taken,
/// which a restore sets memory back to.
/// </param>
/// <param name="Ram">Memory from RAMSTART to that size.</param>
/// <param name="Stack">The stack, big-endian, with the stub on top.</param>
/// <param name="Heap">The heap, or null while it was not active.</param>
public sealed record GlulxSavedState(uint MemorySize, byte[] Ram, byte[] Stack, GlulxHeapSummary? Heap = null)
{
    /// <summary>
    /// [glulx #callstub] The smallest stack there can be: one call stub.
    /// </summary>
    public const int MinimumStackLength = 16;
}

/// <summary>
/// [glulx #saveformat] The heap as a saved game records it: where it
/// starts and the address and length of every extant block.
/// </summary>
/// <param name="Start">
/// [glulx #opcodes_malloc] The end of memory when the heap became
/// active.
/// </param>
/// <param name="Blocks">The extant blocks, in address order.</param>
public sealed record GlulxHeapSummary(uint Start, IReadOnlyList<(uint Address, uint Length)> Blocks);

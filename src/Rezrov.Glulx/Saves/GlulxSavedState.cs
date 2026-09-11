namespace Rezrov.Glulx.Saves;

/// <summary>
/// The state of a Glulx game at one moment: the size of memory, the
/// contents of RAM, and the whole stack, with a call stub on top saying
/// where execution continues.
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
public sealed record GlulxSavedState(uint MemorySize, byte[] Ram, byte[] Stack)
{
    /// <summary>
    /// [glulx #callstub] The smallest stack there can be: one call stub.
    /// </summary>
    public const int MinimumStackLength = 16;
}

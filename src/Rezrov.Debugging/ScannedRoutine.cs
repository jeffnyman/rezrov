using Rezrov.ZMachine.Instructions;

namespace Rezrov.Debugging;

/// <summary>
/// A routine the scan found: where it begins, the locals it declares,
/// and every instruction of its body in order.
/// </summary>
/// <remarks>
/// [zm 5.1] Routines are packed into high memory and nothing in the
/// file lists them, so this is the scan's answer rather than the story
/// file's. It is a good answer, measured against what games actually
/// execute, but it is still an answer and not a fact.
/// </remarks>
/// <param name="Address">Where the routine begins.</param>
/// <param name="InitialLocals">
/// [zm 5.2.1] The initial value of each local. Zero for every local
/// from Version 5, where the file no longer carries them.
/// </param>
/// <param name="CodeAddress">Where the first instruction begins.</param>
/// <param name="Body">The instructions, first to last.</param>
public sealed record ScannedRoutine(
    int Address,
    IReadOnlyList<ushort> InitialLocals,
    int CodeAddress,
    IReadOnlyList<Instruction> Body)
{
    /// <summary>How many locals the routine declares.</summary>
    public int LocalCount => InitialLocals.Count;

    /// <summary>The first byte after the routine.</summary>
    public int EndAddress => Body.Count > 0 ? Body[^1].NextAddress : CodeAddress;

    /// <summary>The routine's size in bytes, header included.</summary>
    public int Length => EndAddress - Address;

    /// <summary>
    /// Whether this address falls anywhere within the routine.
    /// </summary>
    public bool Covers(int address) => address >= Address && address < EndAddress;
}

namespace Rezrov.ZMachine.Instructions;

/// <summary>
/// The few bytes at the start of a routine, before its first instruction.
/// </summary>
/// <remarks>
/// [zm 5.2] A routine begins with a byte giving how many local variables
/// it has, 0 to 15. [zm 5.2.1] In Versions 1 to 4 that many words of
/// initial values follow; from Version 5 the locals start at zero and
/// there is nothing more. [zm 5.3] Execution begins at the byte after
/// that, and there is no end marker: a routine ends when it returns.
/// </remarks>
/// <param name="Address">Where the routine begins.</param>
/// <param name="InitialLocals">
/// The initial value of each local, one per local the routine has.
/// </param>
/// <param name="CodeAddress">Where the first instruction begins.</param>
public readonly record struct RoutineHeader(int Address, IReadOnlyList<ushort> InitialLocals, int CodeAddress)
{
    /// <summary>[zm 5.2] The most locals a routine can have.</summary>
    public const int MaxLocals = 15;

    /// <summary>How many local variables the routine has.</summary>
    public int LocalCount => InitialLocals.Count;

    /// <summary>
    /// Reads the header of the routine at <paramref name="address"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The local count is more than 15, which usually means the address
    /// is not a routine at all.
    /// </exception>
    public static RoutineHeader Read(ZMemory memory, ZMachineVersion version, int address)
    {
        ArgumentNullException.ThrowIfNull(memory);

        int count = memory.ReadByte(address);
        if (count > MaxLocals)
        {
            throw new InvalidDataException(
                $"The routine at {address:X4} claims {count} locals, and a routine can have at most {MaxLocals}.");
        }

        var initial = new ushort[count];
        var at = address + 1;

        if (version <= ZMachineVersion.V4)
        {
            for (var i = 0; i < count; i++)
            {
                initial[i] = memory.ReadWord(at);
                at += 2;
            }
        }

        return new RoutineHeader(address, initial, at);
    }
}

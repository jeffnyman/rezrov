namespace Rezrov.ZMachine.Execution;

/// <summary>
/// The Version 6 "user stack": a table of words in dynamic memory that a
/// game pushes to and pulls from with its own opcodes.
/// </summary>
/// <remarks>
/// [zm 6.6] The first word of the table holds the number of spare slots,
/// so it begins at the stack's capacity and counts down as values are
/// pushed. The editor's note is worth taking to heart: it is not a count
/// of items and not a pointer, and a table of 10 words begins holding 9.
/// Values fill the table from its far end toward the count. [zm 6.6]
/// Nothing checks for underflow; pulling more than was pushed walks the
/// count past its starting value and writes over whatever follows.
/// </remarks>
public static class UserStackTable
{
    /// <summary>
    /// Pushes a value, returning false if the stack is full.
    /// </summary>
    /// <remarks>
    /// [zm op:push_stack] Overflow is not an error: nothing happens and
    /// the opcode does not branch.
    /// </remarks>
    public static bool TryPush(ZMemory memory, int table, ushort value)
    {
        ArgumentNullException.ThrowIfNull(memory);

        var spare = memory.ReadWord(table);
        if (spare == 0)
        {
            return false;
        }

        memory.WriteWord(table + (spare * 2), value);
        memory.WriteWord(table, (ushort)(spare - 1));
        return true;
    }

    /// <summary>
    /// Pulls the top value.
    /// </summary>
    /// <remarks>
    /// [zm op:pull] In Version 6 the stack may be a user one.
    /// </remarks>
    public static ushort Pull(ZMemory memory, int table)
    {
        ArgumentNullException.ThrowIfNull(memory);

        var spare = (ushort)(memory.ReadWord(table) + 1);
        memory.WriteWord(table, spare);
        return memory.ReadWord(table + (spare * 2));
    }

    /// <summary>
    /// Throws away a number of values from the top.
    /// </summary>
    /// <remarks>[zm op:pop_stack]</remarks>
    public static void Pop(ZMemory memory, int table, int items)
    {
        ArgumentNullException.ThrowIfNull(memory);

        memory.WriteWord(table, (ushort)(memory.ReadWord(table) + items));
    }
}

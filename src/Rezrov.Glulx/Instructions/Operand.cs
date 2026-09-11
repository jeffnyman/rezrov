namespace Rezrov.Glulx.Instructions;

/// <summary>
/// Where an operand's value comes from or goes to, once the addressing
/// mode's data size no longer matters.
/// </summary>
/// <remarks>
/// [glulx #instruction] The modes come in families of three that differ
/// only in how many bytes encode the constant or address, and executing
/// an instruction cares about the family alone. Zero is its own kind
/// for a store operand, where it means discard.
/// </remarks>
public enum OperandKind
{
    /// <summary>A constant, already sign-extended to 32 bits.</summary>
    Constant,

    /// <summary>A field in main memory at the operand's address.</summary>
    Memory,

    /// <summary>
    /// The top of the stack: popped for a load, pushed for a store.
    /// </summary>
    Stack,

    /// <summary>
    /// A field in the current call frame's locals at the operand's
    /// offset.
    /// </summary>
    Local,

    /// <summary>
    /// A field in main memory at RAMSTART plus the operand's offset.
    /// </summary>
    Ram,

    /// <summary>A store operand whose value is thrown away.</summary>
    Discard,
}

/// <summary>
/// One operand as it appears in the instruction: an addressing mode,
/// the constant or address the mode's data bytes held, and whether the
/// opcode reads it or writes it.
/// </summary>
/// <remarks>
/// [glulx #instruction] Nothing is looked up at decode time: an operand
/// that names a memory address or a local is still only the address,
/// and a stack operand has not been popped. Operands are evaluated
/// left to right when the instruction runs, which matters when several
/// of them use the stack.
/// </remarks>
/// <param name="Mode">How the operand was encoded.</param>
/// <param name="Value">
/// The constant, sign-extended, or the address or offset, not.
/// </param>
/// <param name="IsStore">
/// [glulx #dictionary-of-opcodes] Whether this is an S operand of the
/// opcode rather than an L operand.
/// </param>
public readonly record struct Operand(AddressingMode Mode, uint Value, bool IsStore)
{
    public OperandKind Kind => Mode switch
    {
        AddressingMode.Zero when IsStore => OperandKind.Discard,
        AddressingMode.Zero or AddressingMode.Constant8 or AddressingMode.Constant16 or AddressingMode.Constant32 => OperandKind.Constant,
        AddressingMode.Memory8 or AddressingMode.Memory16 or AddressingMode.Memory32 => OperandKind.Memory,
        AddressingMode.Stack => OperandKind.Stack,
        AddressingMode.Local8 or AddressingMode.Local16 or AddressingMode.Local32 => OperandKind.Local,
        _ => OperandKind.Ram,
    };

    /// <summary>
    /// The operand as a disassembler might print it: constants with a
    /// hash, addresses in brackets, and a store operand behind an arrow.
    /// </summary>
    public override string ToString()
    {
        var text = Kind switch
        {
            OperandKind.Constant => $"#{Value:X}",
            OperandKind.Memory => $"[{Value:X}]",
            OperandKind.Stack => "sp",
            OperandKind.Local => $"loc[{Value:X}]",
            OperandKind.Ram => $"ram[{Value:X}]",
            _ => "_",
        };

        return IsStore ? "->" + text : text;
    }
}

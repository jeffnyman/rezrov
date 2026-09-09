namespace Rezrov.ZMachine.Instructions;

/// <summary>
/// The operand count an instruction belongs to, which together with the
/// opcode number identifies the opcode.
/// </summary>
/// <remarks>
/// [zm 4.3] The standard writes these as 0OP, 1OP, 2OP, and VAR. They are
/// not literally how many operands an instruction has: a 2OP opcode
/// assembled in variable form can carry up to four, which is how je
/// compares one value against several.
/// </remarks>
public enum OperandCount
{
    ZeroOp,
    OneOp,
    TwoOp,
    Var,
}

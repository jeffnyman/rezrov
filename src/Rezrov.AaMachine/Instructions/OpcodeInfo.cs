namespace Rezrov.AaMachine.Instructions;

/// <summary>
/// [aam opcode] One row of the opcode table: which byte it is, what it
/// does, and what it reads after itself.
/// </summary>
/// <param name="Code">The opcode byte.</param>
/// <param name="Opcode">Which operation this is.</param>
/// <param name="Name">The name the specification gives it.</param>
/// <param name="Operands">
/// The operands in the order they are written. An operand the
/// specification writes as 0 is left out, since it is implied by the
/// opcode and takes no bytes.
/// </param>
public sealed record OpcodeInfo(byte Code, Opcode Opcode, string Name, OperandKind[] Operands)
{
    /// <summary>How many operands are written out.</summary>
    public int OperandCount => Operands.Length;
}

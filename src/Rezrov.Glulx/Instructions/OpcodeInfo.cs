namespace Rezrov.Glulx.Instructions;

/// <summary>
/// What the decoder needs to know about an opcode to read the rest of
/// its instruction: how many operands it has and which of them are
/// stores.
/// </summary>
/// <remarks>
/// [glulx #instruction] An instruction carries no operand count of its
/// own. The opcode number says how many addressing modes follow and,
/// through the operands' modes, how many data bytes, so nothing after
/// the opcode can be read until the opcode is known. That is what
/// <see cref="Operands"/> is for: the letters of the dictionary
/// heading, L for a load operand and S for a store, in order.
/// </remarks>
/// <param name="Opcode">Which opcode this is.</param>
/// <param name="Name">The name from the dictionary.</param>
/// <param name="Operands">
/// [glulx #dictionary-of-opcodes] The operand list as L and S letters,
/// "LLS" for "L1 L2 S1".
/// </param>
/// <param name="Branches">
/// [glulx #opcodes_branch] Whether the last operand is a branch offset,
/// with 0 and 1 meaning return.
/// </param>
/// <param name="OperandSize">
/// [glulx #instruction] The width in bytes of the fields the indirect
/// operands name: four for every opcode but copys and copyb.
/// </param>
public sealed record OpcodeInfo(
    Opcode Opcode,
    string Name,
    string Operands,
    bool Branches = false,
    int OperandSize = 4)
{
    /// <summary>The opcode's number, which is the enum value.</summary>
    public uint Number => (uint)Opcode;

    public int OperandCount => Operands.Length;

    /// <summary>
    /// Whether operand <paramref name="index"/> is an S operand.
    /// </summary>
    public bool IsStore(int index) => Operands[index] == 'S';
}

using System.Text;

namespace Rezrov.Glulx.Instructions;

/// <summary>
/// One decoded instruction: what it is and its operands as encoded.
/// </summary>
/// <remarks>
/// [glulx #instruction] The three parts of an instruction, read in
/// order: the opcode number, the addressing modes, and the operand
/// data. Nothing has been evaluated. Executing is a separate step that
/// reads the operands according to their modes.
/// </remarks>
/// <param name="Address">Where the instruction begins.</param>
/// <param name="Info">The opcode the number resolved to.</param>
/// <param name="Operands">The operands, first to last.</param>
/// <param name="NextAddress">
/// The first byte after the instruction, which is where execution
/// continues unless the instruction says otherwise.
/// </param>
public sealed record Instruction(
    uint Address,
    OpcodeInfo Info,
    IReadOnlyList<Operand> Operands,
    uint NextAddress)
{
    public Opcode Opcode => Info.Opcode;

    public string Name => Info.Name;

    /// <summary>The instruction's size in bytes.</summary>
    public uint Length => NextAddress - Address;

    /// <summary>
    /// The instruction in a form close to what a disassembler prints:
    /// the name, then the operands.
    /// </summary>
    public override string ToString()
    {
        var text = new StringBuilder(Name);

        foreach (var operand in Operands)
        {
            text.Append(' ').Append(operand);
        }

        return text.ToString();
    }
}

using System.Text;

namespace Rezrov.ZMachine.Instructions;

/// <summary>
/// One decoded instruction: what it is, its operands as encoded, and
/// whatever store, branch, or text trails them.
/// </summary>
/// <remarks>
/// [zm 4.1] The sections of an instruction, in order. Nothing here has
/// been evaluated: variable operands are still variable numbers, and the
/// text is an address rather than a string. Executing is a separate step
/// that reads these.
/// </remarks>
/// <param name="Address">Where the instruction begins.</param>
/// <param name="Form">[zm 4.3] The layout the first byte selected.</param>
/// <param name="OperandCount">
/// [zm 4.3] The operand count the form gave.
/// </param>
/// <param name="OpcodeNumber">The number within that operand count.</param>
/// <param name="Info">The opcode the count and number resolve to.</param>
/// <param name="Operands">[zm 4.5] The operands, first to last.</param>
/// <param name="StoreVariable">
/// [zm 4.6] The variable to store to, if any.
/// </param>
/// <param name="Branch">[zm 4.7] The branch data, if any.</param>
/// <param name="TextAddress">
/// [zm 4.8] Where the inline text begins, if any.
/// </param>
/// <param name="NextAddress">
/// The first byte after the instruction, which is where execution
/// continues unless the instruction says otherwise.
/// </param>
public sealed record Instruction(
    int Address,
    InstructionForm Form,
    OperandCount OperandCount,
    int OpcodeNumber,
    OpcodeInfo Info,
    IReadOnlyList<Operand> Operands,
    byte? StoreVariable,
    Branch? Branch,
    int? TextAddress,
    int NextAddress)
{
    public Opcode Opcode => Info.Opcode;

    public string Name => Info.Name;

    /// <summary>
    /// The instruction's size in bytes, inline text included.
    /// </summary>
    public int Length => NextAddress - Address;

    /// <summary>
    /// The instruction in a form close to what a disassembler prints:
    /// the name, the operands, then any store, branch, and text.
    /// </summary>
    public override string ToString()
    {
        var text = new StringBuilder(Name);

        foreach (var operand in Operands)
        {
            text.Append(' ').Append(operand);
        }

        if (StoreVariable is { } store)
        {
            text.Append(" -> ").Append(Operand.VariableName(store));
        }

        if (Branch is { } branch)
        {
            text.Append(' ').Append(branch);
        }

        if (TextAddress is { } address)
        {
            text.Append($" text@{address:X4}");
        }

        return text.ToString();
    }
}

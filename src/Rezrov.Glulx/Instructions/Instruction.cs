using System.Text;

namespace Rezrov.Glulx.Instructions;

/// <summary>
/// [glulx #instruction] One decoded instruction: where it is, what
/// opcode it is, its operands, and where the next instruction begins.
/// </summary>
/// <remarks>
/// Instructions in ROM never change, so the machine decodes each one
/// once and keeps it; that makes this the object the machine touches
/// most, and its operands are handed over as a span rather than
/// through an interface for that reason.
/// </remarks>
public sealed class Instruction
{
    private readonly Operand[] _operands;

    public Instruction(uint address, OpcodeInfo info, Operand[] operands, uint nextAddress)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(operands);

        Address = address;
        Info = info;
        _operands = operands;
        NextAddress = nextAddress;
    }

    /// <summary>The address of the opcode's first byte.</summary>
    public uint Address { get; }

    /// <summary>The opcode table's entry for the instruction.</summary>
    public OpcodeInfo Info { get; }

    /// <summary>
    /// The operands, in the order the instruction lists them.
    /// </summary>
    public IReadOnlyList<Operand> Operands => _operands;

    /// <summary>The address just past the last operand's data.</summary>
    public uint NextAddress { get; }

    public Opcode Opcode => Info.Opcode;

    public string Name => Info.Name;

    /// <summary>How many bytes the instruction takes.</summary>
    public uint Length => NextAddress - Address;

    /// <summary>The operands, for the machine's inner loop.</summary>
    internal ReadOnlySpan<Operand> OperandSpan => _operands;

    public override string ToString()
    {
        var text = new StringBuilder(Name);
        foreach (var operand in _operands)
        {
            text.Append(' ').Append(operand);
        }

        return text.ToString();
    }
}

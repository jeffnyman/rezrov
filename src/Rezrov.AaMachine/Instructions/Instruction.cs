using System.Text;

namespace Rezrov.AaMachine.Instructions;

/// <summary>
/// [aam opcode] One decoded instruction: where it is, what it does, its
/// operands, and where the next one begins.
/// </summary>
public sealed class Instruction
{
    private readonly Operand[] _operands;

    public Instruction(int address, OpcodeInfo info, Operand[] operands, int nextAddress)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(operands);

        Address = address;
        Info = info;
        _operands = operands;
        NextAddress = nextAddress;
    }

    /// <summary>The address of the opcode byte.</summary>
    public int Address { get; }

    /// <summary>The opcode table's row for this instruction.</summary>
    public OpcodeInfo Info { get; }

    /// <summary>The operands, in the order they are written.</summary>
    public IReadOnlyList<Operand> Operands => _operands;

    /// <summary>The address just past the last operand.</summary>
    public int NextAddress { get; }

    public Opcode Opcode => Info.Opcode;

    public string Name => Info.Name;

    /// <summary>How many bytes the instruction takes.</summary>
    public int Length => NextAddress - Address;

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

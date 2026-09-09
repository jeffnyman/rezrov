namespace Rezrov.ZMachine.Instructions;

/// <summary>
/// One operand as it appears in the instruction: a constant, or the
/// number of a variable whose value is to be read when the instruction
/// runs.
/// </summary>
/// <remarks>
/// [zm 4.2.3] A variable operand means "variable by value", and nothing
/// is looked up at decode time. [zm 4.5.2] Values are read first to last
/// when the instruction executes, which matters because reading the
/// stack changes it.
/// </remarks>
/// <param name="Type">How the operand was encoded.</param>
/// <param name="Value">
/// The constant, or for a variable operand the variable number.
/// </param>
public readonly record struct Operand(OperandType Type, ushort Value)
{
    /// <summary>
    /// The conventional name for a variable number.
    /// </summary>
    /// <remarks>
    /// [zm 4.2.2] $00 is the top of the stack, $01 to $0F are the current
    /// routine's locals, and $10 to $FF are the globals. Following the
    /// remarks on section 4, locals are shown numbered from 0 and globals
    /// from 0, the way the txd disassembler prints them.
    /// </remarks>
    public static string VariableName(int number) => number switch
    {
        0 => "sp",
        < 16 => $"L{number - 1:X2}",
        _ => $"G{number - 16:X2}",
    };

    public override string ToString() => Type switch
    {
        OperandType.LargeConstant => $"#{Value:X4}",
        OperandType.SmallConstant => $"#{Value:X2}",
        OperandType.Variable => VariableName(Value),
        _ => "omitted",
    };
}

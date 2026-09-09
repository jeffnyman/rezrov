namespace Rezrov.ZMachine.Instructions;

/// <summary>
/// The four kinds of operand, with the two-bit codes the instruction
/// encoding uses for them.
/// </summary>
/// <remarks>
/// [zm 4.2] A large constant is two bytes, a small constant one, and a
/// variable one byte holding a variable number. Omitted means no operand
/// at all, and [zm 4.4.3] once an operand is omitted so are all the ones
/// after it.
/// </remarks>
public enum OperandType : byte
{
    LargeConstant = 0,
    SmallConstant = 1,
    Variable = 2,
    Omitted = 3,
}

namespace Rezrov.ZMachine.Instructions;

/// <summary>
/// The four ways an instruction can be laid out.
/// </summary>
/// <remarks>
/// [zm 4.3] The form is read from the first byte: top two bits $$11 is
/// variable form, $$10 is short form, the byte $BE in Version 5 or later
/// is extended form, and anything else is long form. The form decides
/// where the operand count and the operand types come from.
/// </remarks>
public enum InstructionForm
{
    LongForm,
    ShortForm,
    ExtendedForm,
    VariableForm,
}

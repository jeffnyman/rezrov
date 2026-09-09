namespace Rezrov.ZMachine.Instructions;

/// <summary>
/// What the decoder needs to know about an opcode to read the rest of
/// its instruction: whether a store byte, branch data, or inline text
/// follows the operands.
/// </summary>
/// <remarks>
/// [zm 14] These are the St and Br columns of the opcode table, plus the
/// two opcodes [zm 4.8] marks as carrying text. The editor's note on
/// [zm 4.1] explains why they matter to decoding and not only to
/// execution: there is no length field, so an instruction's size is only
/// known once its opcode says what trails the operands.
/// </remarks>
/// <param name="Opcode">Which opcode this is.</param>
/// <param name="Name">The Inform name from the table.</param>
/// <param name="Store">[zm 4.6] A store variable byte follows.</param>
/// <param name="Branch">[zm 4.7] Branch data follows.</param>
/// <param name="Text">[zm 4.8] An encoded string follows.</param>
public sealed record OpcodeInfo(
    Opcode Opcode,
    string Name,
    bool Store = false,
    bool Branch = false,
    bool Text = false);

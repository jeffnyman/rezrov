namespace Rezrov.ZMachine.Instructions;

/// <summary>
/// What a branch instruction does with the result of its test.
/// </summary>
/// <remarks>
/// [zm 4.7] The branch data says which outcome causes the branch and
/// where it goes. Two offsets are special: [zm 4.7.1] 0 returns false
/// from the current routine and 1 returns true, so the arithmetic in
/// [zm 4.7.2] subtracts 2 to keep those values free. The editor's note
/// spells out the useful consequence: an offset of 2 goes nowhere at
/// all.
/// </remarks>
/// <param name="OnTrue">
/// True if the branch is taken when the condition holds, false if it is
/// taken when the condition fails.
/// </param>
/// <param name="Offset">
/// The offset as stored: 0 to 63 from the one-byte form, or a signed
/// 14-bit value from the two-byte form.
/// </param>
public readonly record struct Branch(bool OnTrue, short Offset)
{
    /// <summary>
    /// [zm 4.7.1] The branch returns false rather than jumping.
    /// </summary>
    public bool ReturnsFalse => Offset == 0;

    /// <summary>
    /// [zm 4.7.1] The branch returns true rather than jumping.
    /// </summary>
    public bool ReturnsTrue => Offset == 1;

    /// <summary>
    /// The address the branch jumps to, given the address of the byte
    /// after the branch data.
    /// </summary>
    /// <remarks>
    /// [zm 4.7.2] Address after branch data, plus the offset, minus 2.
    /// Not meaningful when <see cref="ReturnsFalse"/> or
    /// <see cref="ReturnsTrue"/>.
    /// </remarks>
    public int Target(int addressAfterBranchData) => addressAfterBranchData + Offset - 2;

    public override string ToString()
    {
        var sense = OnTrue ? "?" : "?~";

        return Offset switch
        {
            0 => sense + "rfalse",
            1 => sense + "rtrue",
            _ => sense + Offset,
        };
    }
}

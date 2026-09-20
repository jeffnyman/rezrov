using System.Globalization;

namespace Rezrov.AaMachine.Instructions;

/// <summary>
/// [aam opcode] Where an operand's number comes from.
/// </summary>
public enum OperandSource
{
    /// <summary>The number is written in the instruction.</summary>
    Immediate,

    /// <summary>It is in one of the sixty-four registers.</summary>
    Register,

    /// <summary>It is in a slot of the current environment frame.</summary>
    EnvSlot,
}

/// <summary>
/// [aam opcode] One decoded operand.
/// </summary>
/// <param name="Kind">How it was written.</param>
/// <param name="Source">
/// Where its number comes from. Only raw numbers, live values and
/// destinations have a choice; everything else is written out.
/// </param>
/// <param name="Number">
/// The number itself: a constant, or a register or slot number, or an
/// address in the CODE or WRIT chunk.
/// </param>
/// <param name="Unify">
/// For a destination, whether the result is unified with what is
/// already there rather than stored over it. Unification is how the
/// machine matches a value against a pattern, so it is the difference
/// between an assignment and a question.
/// </param>
public readonly record struct Operand(
    OperandKind Kind,
    OperandSource Source,
    int Number,
    bool Unify = false)
{
    /// <summary>
    /// The operand as a disassembler would print it: R0a for a
    /// register, E0a for an environment slot, and a destination marked
    /// with a greater-than sign when it stores and an equals sign when
    /// it unifies.
    /// </summary>
    public override string ToString() => Kind switch
    {
        OperandKind.Dest => (Unify ? "=" : ">") + Place(Source, Number),
        OperandKind.Code => $"@{Number:x4}",
        OperandKind.Text => $"${Number:x4}",

        // A raw number and a live value are written the same way, so
        // this is the one place the two part company: a live value is
        // a tagged word and is worth seeing in hexadecimal, while a
        // raw number is counting something.
        _ when Source != OperandSource.Immediate => Place(Source, Number),
        OperandKind.VByte or OperandKind.VWord or OperandKind.Value => $"#{Number:x4}",
        _ => Number.ToString(CultureInfo.InvariantCulture),
    };

    private static string Place(OperandSource source, int number) =>
        source == OperandSource.EnvSlot ? $"E{number:x2}" : $"R{number:x2}";
}

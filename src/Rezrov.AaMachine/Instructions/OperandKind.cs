namespace Rezrov.AaMachine.Instructions;

/// <summary>
/// [aam opcode] The ways an operand can be written, which is what
/// decides how many bytes of it follow the opcode.
/// </summary>
/// <remarks>
/// Nothing in an instruction says how long it is. The opcode says
/// which of these its operands are, and each of these says how to read
/// its own first byte to find out how many more it has, so an
/// instruction cannot be skipped without being decoded.
///
/// The V in the specification's VBYTE and VWORD stands for a literal
/// value, one of the tagged sixteen-bit words the machine works in,
/// rather than for a plain number.
/// </remarks>
public enum OperandKind
{
    /// <summary>A small constant number, one byte.</summary>
    Byte,

    /// <summary>
    /// A small constant literal, one byte: zero, or an object from 1
    /// to $ff.
    /// </summary>
    VByte,

    /// <summary>A constant number, two bytes.</summary>
    Word,

    /// <summary>
    /// A constant literal from $0000 to $7fff, two bytes.
    /// </summary>
    VWord,

    /// <summary>
    /// An unsigned sixteen-bit number: written out, or taken from a
    /// register, or taken from a slot of the current environment.
    /// </summary>
    Raw,

    /// <summary>
    /// A live value, written the same three ways as a raw number but
    /// read as a tagged reference rather than as a number.
    /// </summary>
    Value,

    /// <summary>
    /// Where a result goes: a register or an environment slot, either
    /// stored into or unified with.
    /// </summary>
    Dest,

    /// <summary>
    /// A plain index, into the variables or the style classes or
    /// whatever else the opcode is counting.
    /// </summary>
    Index,

    /// <summary>A byte address in the CODE chunk.</summary>
    Code,

    /// <summary>
    /// A shifted byte address in the WRIT chunk. The specification
    /// calls this one STRING.
    /// </summary>
    Text,
}

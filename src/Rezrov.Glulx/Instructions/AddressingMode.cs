namespace Rezrov.Glulx.Instructions;

/// <summary>
/// The sixteen addressing modes an operand can be encoded in, with the
/// four-bit codes the instruction uses for them.
/// </summary>
/// <remarks>
/// [glulx #instruction] Each mode fixes how many bytes of operand data
/// follow the mode nibbles and what those bytes mean. The suffix on a
/// name here is the size of that data: Constant16 is a two-byte
/// constant, Memory16 a two-byte address in main memory. Two codes, 4
/// and 12, are unused and the decoder refuses them.
///
/// The constant modes sign-extend their data to 32 bits and the other
/// modes do not, because negative constants are common and negative
/// addresses are not. [glulx #instruction] For a store operand the
/// same codes apply, except that the constant modes make no sense,
/// <see cref="Zero"/> means the value is thrown away, and
/// <see cref="Stack"/> pushes rather than pops.
/// </remarks>
public enum AddressingMode : byte
{
    /// <summary>The constant zero, with no data bytes.</summary>
    Zero = 0,

    /// <summary>A constant from -80 to 7F, one byte.</summary>
    Constant8 = 1,

    /// <summary>A constant from -8000 to 7FFF, two bytes.</summary>
    Constant16 = 2,

    /// <summary>A constant of any value, four bytes.</summary>
    Constant32 = 3,

    /// <summary>The contents of a main memory address 00 to FF.</summary>
    Memory8 = 5,

    /// <summary>
    /// The contents of a main memory address 0000 to FFFF.
    /// </summary>
    Memory16 = 6,

    /// <summary>The contents of any main memory address.</summary>
    Memory32 = 7,

    /// <summary>
    /// A value popped off the stack, or for a store operand pushed onto
    /// it, with no data bytes.
    /// </summary>
    Stack = 8,

    /// <summary>A call frame local at offset 00 to FF.</summary>
    Local8 = 9,

    /// <summary>A call frame local at offset 0000 to FFFF.</summary>
    Local16 = 10,

    /// <summary>A call frame local at any offset.</summary>
    Local32 = 11,

    /// <summary>The contents of RAM at RAMSTART plus 00 to FF.</summary>
    Ram8 = 13,

    /// <summary>
    /// The contents of RAM at RAMSTART plus 0000 to FFFF.
    /// </summary>
    Ram16 = 14,

    /// <summary>The contents of RAM at RAMSTART plus any offset.</summary>
    Ram32 = 15,
}

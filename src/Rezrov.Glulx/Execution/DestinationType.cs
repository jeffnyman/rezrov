namespace Rezrov.Glulx.Execution;

/// <summary>
/// [glulx #callstub] Where a call stub says its result goes when the
/// operation it records completes: a function return, a caught throw,
/// a restore, or the resumption of a string.
/// </summary>
public enum DestinationType : uint
{
    /// <summary>Do not store. The result is discarded.</summary>
    None = 0,

    /// <summary>Store in main memory at the address.</summary>
    Memory = 1,

    /// <summary>
    /// Store in a local of the call frame, at the offset from the start
    /// of its locals.
    /// </summary>
    Local = 2,

    /// <summary>Push on the stack.</summary>
    Stack = 3,

    /// <summary>
    /// Resume printing a compressed string: the address holds the byte
    /// to continue in and the destination the bit number within it.
    /// </summary>
    ResumeCompressedString = 10,

    /// <summary>
    /// Resume executing function code after a string completes. The
    /// frame pointer in the stub is ignored, since the string printed
    /// in the same frame.
    /// </summary>
    ResumeFunction = 11,

    /// <summary>
    /// Resume printing a signed decimal integer: the address holds the
    /// integer and the destination the position of the next digit.
    /// </summary>
    ResumeInteger = 12,

    /// <summary>
    /// Resume printing a C-style string: the address holds the next
    /// character.
    /// </summary>
    ResumeCString = 13,

    /// <summary>
    /// Resume printing a Unicode string: the address holds the next
    /// four-byte character.
    /// </summary>
    ResumeUnicodeString = 14,
}

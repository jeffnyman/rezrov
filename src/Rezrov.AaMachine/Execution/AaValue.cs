namespace Rezrov.AaMachine.Execution;

/// <summary>
/// [aam runtime] What a sixteen-bit word is standing for.
/// </summary>
/// <remarks>
/// The tag is not a field of its own. It is however many of the top
/// bits it takes to tell the kinds apart, which is one bit for a
/// number and eight for the empty list, so that the kinds a game uses
/// most leave the most room for their values.
/// </remarks>
public enum AaTag
{
    /// <summary>Nothing at all, which is the word zero.</summary>
    Null,

    /// <summary>
    /// One of the game's objects. The word "object" on its own means
    /// too many other things in C# to use as a name here.
    /// </summary>
    Thing,

    /// <summary>
    /// A word of the dictionary. The specification calls this one
    /// "dict" where it would otherwise be confused with a machine
    /// word, which is any sixteen bits at all.
    /// </summary>
    Word,

    /// <summary>A single character.</summary>
    Character,

    /// <summary>The empty list.</summary>
    Empty,

    /// <summary>An integer from 0 to 16383.</summary>
    Number,

    /// <summary>
    /// A cell on the heap, which holds either a value or, when it
    /// holds zero, nothing yet. An unbound variable is a reference to
    /// a cell holding zero.
    /// </summary>
    Reference,

    /// <summary>
    /// The head and tail of a list, two cells on the heap.
    /// </summary>
    Pair,

    /// <summary>
    /// A word the dictionary does not have, kept as a known part and
    /// a list of the characters left over.
    /// </summary>
    ExtDict,

    /// <summary>
    /// A pattern the specification has not given away yet.
    /// </summary>
    Reserved,
}

/// <summary>
/// [aam runtime] The tagged words the machine works in, and the
/// reading and writing of their tags.
/// </summary>
public static class AaValue
{
    /// <summary>Nothing, which is also an unset word.</summary>
    public const ushort Null = 0x0000;

    /// <summary>The empty list.</summary>
    public const ushort Empty = 0x3f00;

    /// <summary>
    /// [aam runtime] The word that marks a part of a heap nothing has
    /// written to, which is how peak memory use is measured.
    /// </summary>
    public const ushort Unused = 0x3f3f;

    /// <summary>The largest integer a number can hold.</summary>
    public const int MaxNumber = 0x3fff;

    /// <summary>What a word is standing for.</summary>
    public static AaTag Tag(ushort word) => word switch
    {
        0 => AaTag.Null,
        < 0x2000 => AaTag.Thing,
        < 0x3e00 => AaTag.Word,
        < 0x3f00 => AaTag.Character,
        0x3f00 => AaTag.Empty,
        < 0x4000 => AaTag.Reserved,
        < 0x8000 => AaTag.Number,
        < 0xa000 => AaTag.Reference,
        < 0xc000 => AaTag.Reserved,
        < 0xe000 => AaTag.Pair,
        _ => AaTag.ExtDict,
    };

    /// <summary>
    /// [aam runtime] The value under the tag, which is the
    /// specification's own name for it: an object number, a dictionary
    /// index, a character, an integer, or a heap address.
    /// </summary>
    public static int Value(ushort word) => Tag(word) switch
    {
        AaTag.Thing => word,
        AaTag.Word => word - 0x2000,
        AaTag.Character => word & 0xff,
        AaTag.Number => word & 0x3fff,
        AaTag.Reference or AaTag.Pair or AaTag.ExtDict => word & 0x1fff,
        _ => 0,
    };

    public static ushort Thing(int number) => (ushort)number;

    public static ushort Word(int index) => (ushort)(0x2000 + index);

    public static ushort Character(int character) =>
        (ushort)(0x3e00 | (character & 0xff));

    public static ushort Number(int value) => (ushort)(0x4000 | (value & 0x3fff));

    public static ushort Reference(int address) => (ushort)(0x8000 | (address & 0x1fff));

    public static ushort Pair(int address) => (ushort)(0xc000 | (address & 0x1fff));

    public static ushort ExtDict(int address) => (ushort)(0xe000 | (address & 0x1fff));

    public static bool IsReference(ushort word) => Tag(word) == AaTag.Reference;

    public static bool IsPair(ushort word) => Tag(word) == AaTag.Pair;

    public static bool IsExtDict(ushort word) => Tag(word) == AaTag.ExtDict;
}

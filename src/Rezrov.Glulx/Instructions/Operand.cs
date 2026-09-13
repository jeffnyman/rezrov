namespace Rezrov.Glulx.Instructions;

/// <summary>
/// What an operand refers to, once its addressing mode has been read:
/// the mode's size no longer matters, only where the value lives.
/// </summary>
public enum OperandKind
{
    /// <summary>The value itself, sign-extended from its mode.</summary>
    Constant,

    /// <summary>A main memory address.</summary>
    Memory,

    /// <summary>
    /// The top of the stack: a pop to load, a push to store.
    /// </summary>
    Stack,

    /// <summary>An offset into the current call frame's locals.</summary>
    Local,

    /// <summary>An offset from the start of RAM.</summary>
    Ram,

    /// <summary>
    /// A store to nowhere: the zero mode as a store operand.
    /// </summary>
    Discard,
}

/// <summary>
/// [glulx #instruction] One decoded operand: its addressing mode, the
/// value read for it, and whether the instruction stores to it. The
/// kind is worked out once here, since the machine asks for it on
/// every load and store.
/// </summary>
public readonly record struct Operand
{
    public Operand(AddressingMode mode, uint value, bool isStore)
    {
        Mode = mode;
        Value = value;
        IsStore = isStore;
        Kind = KindOf(mode, isStore);
    }

    public AddressingMode Mode { get; }

    /// <summary>
    /// The constant, the address, or the offset, as the mode says.
    /// </summary>
    public uint Value { get; }

    /// <summary>Whether this is a store operand.</summary>
    public bool IsStore { get; }

    public OperandKind Kind { get; }

    public override string ToString()
    {
        var text = Kind switch
        {
            OperandKind.Constant => $"#{Value:X}",
            OperandKind.Memory => $"[{Value:X}]",
            OperandKind.Stack => "sp",
            OperandKind.Local => $"loc[{Value:X}]",
            OperandKind.Ram => $"ram[{Value:X}]",
            _ => "_",
        };

        return IsStore ? "->" + text : text;
    }

    /// <summary>
    /// [glulx #instruction] Mode 0 is the constant zero to load and a
    /// discard to store; 1 to 3 are constants; 5 to 7 addresses; 8 the
    /// stack; 9 to B locals; D to F RAM offsets.
    /// </summary>
    private static OperandKind KindOf(AddressingMode mode, bool isStore) => mode switch
    {
        AddressingMode.Zero when isStore => OperandKind.Discard,
        AddressingMode.Zero or AddressingMode.Constant8 or AddressingMode.Constant16 or AddressingMode.Constant32 => OperandKind.Constant,
        AddressingMode.Memory8 or AddressingMode.Memory16 or AddressingMode.Memory32 => OperandKind.Memory,
        AddressingMode.Stack => OperandKind.Stack,
        AddressingMode.Local8 or AddressingMode.Local16 or AddressingMode.Local32 => OperandKind.Local,
        _ => OperandKind.Ram,
    };
}

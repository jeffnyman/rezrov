namespace Rezrov.AaMachine.Instructions;

/// <summary>
/// Reads instructions out of the CODE chunk.
/// </summary>
/// <remarks>
/// [aam opcode] An instruction is an opcode byte and then its
/// operands, and nothing in it says how long it is. The opcode says
/// which kinds of operand follow, and each operand's own first byte
/// says how many more bytes it takes, so an instruction cannot be
/// skipped without being read.
///
/// That would make the chunk hard to walk if the compiler left gaps in
/// it. It does not: reading straight through from address 1 lands on
/// the last instruction's last byte exactly, which is what
/// <see cref="All"/> relies on.
/// </remarks>
public sealed class InstructionDecoder
{
    private readonly byte[] _code;
    private readonly int _major;
    private readonly int _minor;
    private readonly int _shift;

    internal InstructionDecoder(byte[] code, int major, int minor, int shift)
    {
        _code = code;
        _major = major;
        _minor = minor;
        _shift = shift;
    }

    /// <summary>
    /// [aam story] The entry point, where the machine starts and
    /// starts again. Address 0 holds FAIL instead, so that a jump to
    /// nowhere fails at once.
    /// </summary>
    public const int Entry = 1;

    /// <summary>How many bytes of bytecode the story carries.</summary>
    public int Length => _code.Length;

    /// <summary>
    /// Decodes the instruction at <paramref name="address"/>.
    /// </summary>
    /// <exception cref="AaMachineException">
    /// The byte there names no opcode this story has, or its operands
    /// run past the end of the chunk.
    /// </exception>
    public Instruction Decode(int address)
    {
        if (address < 0 || address >= _code.Length)
        {
            throw new AaMachineException(
                $"There is no instruction at {address:x4} in a chunk of {_code.Length} bytes.");
        }

        var code = _code[address];

        var info = OpcodeTable.Resolve(code, _major, _minor)
            ?? throw new AaMachineException($"Unknown opcode {code:x2} at {address:x4}.");

        var at = address + 1;
        var operands = info.OperandCount == 0 ? [] : new Operand[info.OperandCount];

        for (var i = 0; i < operands.Length; i++)
        {
            operands[i] = Read(info.Operands[i], ref at, address);
        }

        return new Instruction(address, info, operands, at);
    }

    /// <summary>
    /// Every instruction in the chunk, from the entry point onwards.
    /// </summary>
    public IEnumerable<Instruction> All()
    {
        var at = Entry;

        while (at < _code.Length)
        {
            var instruction = Decode(at);

            yield return instruction;

            at = instruction.NextAddress;
        }
    }

    private Operand Read(OperandKind kind, ref int at, int address)
    {
        var first = Byte(ref at, address);

        switch (kind)
        {
            case OperandKind.Byte:
            case OperandKind.VByte:
                return new Operand(kind, OperandSource.Immediate, first);

            case OperandKind.Word:
            case OperandKind.VWord:
                return new Operand(kind, OperandSource.Immediate, (first << 8) | Byte(ref at, address));

            // [aam opcode] A raw number and a live value are written
            // the same way: two bytes of number with the top bit
            // clear, or one byte naming a register or an environment
            // slot.
            case OperandKind.Raw:
            case OperandKind.Value:
                return (first & 0x80) == 0
                    ? new Operand(kind, OperandSource.Immediate, (first << 8) | Byte(ref at, address))
                    : new Operand(kind, Source(first), first & 0x3f);

            // [aam opcode] A destination names a register or a slot,
            // and its top bit says whether the result is stored over
            // what is there or unified with it.
            case OperandKind.Dest:
                return new Operand(kind, Source(first), first & 0x3f, (first & 0x80) != 0);

            // [aam opcode] An index is one byte up to $bf and two
            // beyond that, so a style class or a variable number
            // costs a byte until the game has a great many of them.
            case OperandKind.Index:
                return new Operand(
                    kind,
                    OperandSource.Immediate,
                    (first & 0xc0) == 0xc0
                        ? ((first & 0x3f) << 8) | Byte(ref at, address)
                        : first);

            case OperandKind.Code:
                return new Operand(kind, OperandSource.Immediate, Target(first, ref at, address));

            case OperandKind.Text:
                return new Operand(kind, OperandSource.Immediate, Text(first, ref at, address));

            default:
                throw new AaMachineException($"Unknown operand kind {kind} at {address:x4}.");
        }
    }

    // [aam opcode] The one-byte form names a register when the second
    // bit is clear and an environment slot when it is set, and a
    // destination is laid out the same way under its store bit.
    private static OperandSource Source(byte first) =>
        (first & 0x40) == 0 ? OperandSource.Register : OperandSource.EnvSlot;

    private int Target(byte first, ref int at, int address)
    {
        // [aam opcode] A three-byte pointer reaches anywhere in the
        // chunk, which is up to eight megabytes.
        if ((first & 0x80) != 0)
        {
            return ((first & 0x7f) << 16) | (Byte(ref at, address) << 8) | Byte(ref at, address);
        }

        // [aam opcode] Two bytes hold a signed offset from the end of
        // the operand, reaching eight kilobytes either way.
        if ((first & 0x40) != 0)
        {
            var offset = ((first & 0x3f) << 8) | Byte(ref at, address);

            if ((offset & 0x2000) != 0)
            {
                offset -= 0x4000;
            }

            return at + offset;
        }

        // [aam opcode] One byte is either the absolute address 0,
        // which holds FAIL, or a short hop forwards of up to $3f.
        return first == 0 ? 0 : at + first;
    }

    private int Text(byte first, ref int at, int address)
    {
        // [aam opcode] A string pointer is shifted, and by how much
        // depends on its length: one byte is shifted by one, and the
        // longer two by whatever the header says.
        if (first < 0x80)
        {
            return first << 1;
        }

        var value = first < 0xc0
            ? ((first & 0x3f) << 8) | Byte(ref at, address)
            : ((first & 0x3f) << 16) | (Byte(ref at, address) << 8) | Byte(ref at, address);

        return value << _shift;
    }

    private byte Byte(ref int at, int address)
    {
        if (at >= _code.Length)
        {
            throw new AaMachineException($"The instruction at {address:x4} runs past the end of the chunk.");
        }

        return _code[at++];
    }
}

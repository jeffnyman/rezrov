namespace Rezrov.Glulx.Instructions;

/// <summary>
/// Reads one instruction from memory.
/// </summary>
/// <remarks>
/// [glulx #instruction] The three parts of an instruction each depend
/// on the one before. The top two bits of the first byte say how many
/// bytes the opcode number takes; the number says how many operands
/// there are and so how many bytes of addressing modes follow; and the
/// modes say how many bytes of operand data follow those. There is no
/// length field, and no way to skip an instruction without decoding
/// it.
/// </remarks>
public sealed class InstructionDecoder
{
    private readonly GlulxMemory _memory;

    public InstructionDecoder(GlulxMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        _memory = memory;
    }

    /// <summary>
    /// Decodes the instruction at <paramref name="address"/>.
    /// </summary>
    /// <exception cref="GlulxException">
    /// The bytes name an opcode this interpreter does not have, use an
    /// addressing mode that does not exist, or store to a constant.
    /// </exception>
    public Instruction Decode(uint address)
    {
        var at = address;

        // [glulx #instruction] The opcode number is packed into one,
        // two, or four bytes, and the top two bits of the first byte
        // say which: 00 to 7F is the number itself, 80 to BF begins a
        // two-byte number offset by 8000, and C0 to FF a four-byte
        // number offset by C0000000. So 10, 8010, and C0000010 all mean
        // opcode 10.
        var first = _memory.ReadByte(at);
        uint number;
        if (first < 0x80)
        {
            number = first;
            at += 1;
        }
        else if (first < 0xC0)
        {
            number = _memory.ReadShort(at) - 0x8000u;
            at += 2;
        }
        else
        {
            number = _memory.ReadWord(at) - 0xC0000000u;
            at += 4;
        }

        var info = OpcodeTable.Resolve(number)
            ?? throw new GlulxException($"Unknown opcode {number:X} at {address:X8}.");

        // [glulx #instruction] The addressing modes are four bits each,
        // packed two to a byte in operand order, low bits first. An odd
        // count leaves the high bits of the last byte unused.
        var count = info.OperandCount;
        var modes = new AddressingMode[count];
        for (var i = 0; i < count; i += 2)
        {
            var pair = _memory.ReadByte(at++);
            modes[i] = (AddressingMode)(pair & 0x0F);
            if (i + 1 < count)
            {
                modes[i + 1] = (AddressingMode)(pair >> 4);
            }
        }

        // [glulx #instruction] The operand data follows in the same
        // order, each operand taking the number of bytes its mode says,
        // with no padding between them.
        var operands = new Operand[count];
        for (var i = 0; i < count; i++)
        {
            var store = info.IsStore(i);
            operands[i] = new Operand(modes[i], ReadData(modes[i], store, ref at, address), store);
        }

        return new Instruction(address, info, operands, at);
    }

    private uint ReadData(AddressingMode mode, bool store, ref uint at, uint address)
    {
        // [glulx #instruction] A store operand cannot be a constant, since
        // it makes no sense to store to one; zero means discard instead.
        if (store && mode is AddressingMode.Constant8 or AddressingMode.Constant16 or AddressingMode.Constant32)
        {
            throw new GlulxException($"A store operand in constant mode {(int)mode} at {address:X8}.");
        }

        switch (mode)
        {
            case AddressingMode.Zero:
            case AddressingMode.Stack:
                return 0;

            // [glulx #instruction] The constant modes sign-extend their
            // data into a 32-bit value; the other modes do not.
            case AddressingMode.Constant8:
                return (uint)(sbyte)_memory.ReadByte(at++);
            case AddressingMode.Constant16:
                at += 2;
                return (uint)(short)_memory.ReadShort(at - 2);

            case AddressingMode.Memory8:
            case AddressingMode.Local8:
            case AddressingMode.Ram8:
                return _memory.ReadByte(at++);
            case AddressingMode.Memory16:
            case AddressingMode.Local16:
            case AddressingMode.Ram16:
                at += 2;
                return _memory.ReadShort(at - 2);

            case AddressingMode.Constant32:
            case AddressingMode.Memory32:
            case AddressingMode.Local32:
            case AddressingMode.Ram32:
                at += 4;
                return _memory.ReadWord(at - 4);

            default:
                // [glulx #instruction] Modes 4 and C are unused.
                throw new GlulxException($"Unused addressing mode {(int)mode} at {address:X8}.");
        }
    }
}

using Rezrov.ZMachine.Text;

namespace Rezrov.ZMachine.Instructions;

/// <summary>
/// Reads one instruction from memory.
/// </summary>
/// <remarks>
/// The editor's note on [zm 4.1] is the whole design: the table of an
/// instruction's parts, read top to bottom, is the decoding procedure,
/// and each step depends on the one before. The first byte gives the
/// form, the form gives the operand count and where the types come from,
/// the types give the operands' sizes, and only then is it known where
/// the store byte, branch data, and text begin. There is no length field
/// and no way to skip an instruction without decoding it.
/// </remarks>
public sealed class InstructionDecoder
{
    // [zm 4.3] The byte that introduces an extended opcode.
    private const byte ExtendedMarker = 0xBE;

    private readonly ZMemory _memory;
    private readonly ZMachineVersion _version;
    private readonly ZTextDecoder _text;

    public InstructionDecoder(ZMemory memory, StoryHeader header, ZTextDecoder text)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(text);

        _memory = memory;
        _version = header.Version;
        _text = text;
    }

    /// <summary>
    /// Decodes the instruction at <paramref name="address"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The bytes name an opcode that does not exist in this version.
    /// </exception>
    public Instruction Decode(int address)
    {
        var at = address;
        var first = _memory.ReadByte(at++);
        var types = new List<OperandType>(8);

        InstructionForm form;
        OperandCount count;
        int number;

        // [zm 4.3] The order of these tests is the point of the editor's
        // note: $BE has top bits $$10, so the extended check has to come
        // before the short-form check or every extended opcode in a
        // Version 5 game decodes as a 0OP instruction.
        if (_version >= ZMachineVersion.V5 && first == ExtendedMarker)
        {
            // [zm 4.3.4] The opcode number is the next byte, the count is
            // VAR, and [zm 4.4.3] a types byte follows.
            form = InstructionForm.ExtendedForm;
            count = OperandCount.Var;
            number = _memory.ReadByte(at++);
            ReadTypes(ref at, types);
        }
        else if ((first & 0xC0) == 0xC0)
        {
            // [zm 4.3.3] Bit 5 picks 2OP or VAR, the bottom five bits are
            // the number, and [zm 4.4.3] a types byte follows.
            form = InstructionForm.VariableForm;
            count = (first & 0x20) != 0 ? OperandCount.Var : OperandCount.TwoOp;
            number = first & 0x1F;
            ReadTypes(ref at, types);

            // [zm 4.4.3.1] call_vs2 and call_vn2 take a second types byte
            // for operands five to eight.
            if (OpcodeTable.HasSecondTypesByte(count, number))
            {
                ReadTypes(ref at, types);
            }
        }
        else if ((first & 0xC0) == 0x80)
        {
            // [zm 4.3.1] Bits 4 and 5 are the one operand's type, and if
            // they say omitted the count is 0OP. [zm 4.4.1] The number is
            // the bottom four bits.
            form = InstructionForm.ShortForm;
            number = first & 0x0F;

            var type = (OperandType)((first >> 4) & 0x03);
            if (type == OperandType.Omitted)
            {
                count = OperandCount.ZeroOp;
            }
            else
            {
                count = OperandCount.OneOp;
                types.Add(type);
            }
        }
        else
        {
            // [zm 4.3.2] Always 2OP, with the number in the bottom five
            // bits. [zm 4.4.2] Bits 6 and 5 type the two operands, and
            // the only choice is small constant or variable, which is why
            // a 2OP needing a large constant is assembled in variable
            // form instead.
            form = InstructionForm.LongForm;
            count = OperandCount.TwoOp;
            number = first & 0x1F;
            types.Add((first & 0x40) != 0 ? OperandType.Variable : OperandType.SmallConstant);
            types.Add((first & 0x20) != 0 ? OperandType.Variable : OperandType.SmallConstant);
        }

        // [zm 14.2] An opcode the version does not have is illegal, and
        // there is no way to decode past it since its layout is unknown.
        var info = OpcodeTable.Resolve(form, count, number, _version)
            ?? throw new InvalidDataException(
                $"The instruction at {address:X4} uses {Describe(form, count, number)}, which does not exist in Version {(int)_version}.");

        // [zm 4.5] The operands, each sized by its type.
        var operands = new Operand[types.Count];
        for (var i = 0; i < types.Count; i++)
        {
            operands[i] = types[i] switch
            {
                // [zm 4.2.1] Most significant byte first.
                OperandType.LargeConstant => ReadLarge(ref at),
                OperandType.SmallConstant => new Operand(OperandType.SmallConstant, _memory.ReadByte(at++)),
                _ => new Operand(OperandType.Variable, _memory.ReadByte(at++)),
            };
        }

        // [zm 4.6] One byte naming the variable to store into.
        byte? store = info.Store ? _memory.ReadByte(at++) : null;

        // [zm 4.7]
        Branch? branch = info.Branch ? ReadBranch(ref at) : null;

        // [zm 4.8] Execution continues after the last word of the text.
        int? textAddress = null;
        if (info.Text)
        {
            textAddress = at;
            at = _text.SkipString(at);
        }

        return new Instruction(address, form, count, number, info, operands, store, branch, textAddress, at);
    }

    // [zm 4.4.3] Four two-bit fields from the top of the byte down, and
    // once one says omitted the rest must too, so reading stops there.
    private void ReadTypes(ref int at, List<OperandType> types)
    {
        var b = _memory.ReadByte(at++);

        for (var shift = 6; shift >= 0; shift -= 2)
        {
            var type = (OperandType)((b >> shift) & 0x03);
            if (type == OperandType.Omitted)
            {
                break;
            }

            types.Add(type);
        }
    }

    private Operand ReadLarge(ref int at)
    {
        var value = _memory.ReadWord(at);
        at += 2;
        return new Operand(OperandType.LargeConstant, value);
    }

    // [zm 4.7] Bit 7 is the sense of the test. Bit 6 set means one byte
    // with an unsigned offset in the bottom six bits; clear means two
    // bytes holding a signed 14-bit offset. The editor's note is right
    // that the sign extension is the part to get wrong: the short form
    // can only go forward, so every loop uses the long one.
    private Branch ReadBranch(ref int at)
    {
        var first = _memory.ReadByte(at++);
        var onTrue = (first & 0x80) != 0;

        if ((first & 0x40) != 0)
        {
            return new Branch(onTrue, (short)(first & 0x3F));
        }

        var offset = ((first & 0x3F) << 8) | _memory.ReadByte(at++);
        if ((offset & 0x2000) != 0)
        {
            offset -= 0x4000;
        }

        return new Branch(onTrue, (short)offset);
    }

    private static string Describe(InstructionForm form, OperandCount count, int number)
    {
        if (form == InstructionForm.ExtendedForm)
        {
            return $"EXT:{number}";
        }

        return count switch
        {
            OperandCount.TwoOp => $"2OP:{number}",
            OperandCount.OneOp => $"1OP:{number + 128}",
            OperandCount.ZeroOp => $"0OP:{number + 176}",
            _ => $"VAR:{number + 224}",
        };
    }
}

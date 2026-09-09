using Rezrov.ZMachine;
using Rezrov.ZMachine.Instructions;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

public class InstructionDecoderTests
{
    private const int Code = 0x400;

    /// <summary>
    /// A story file with the given bytes placed at $400, ready to decode.
    /// </summary>
    private static (InstructionDecoder Decoder, ZMemory Memory) Decoder(ZMachineVersion version, params byte[] code)
    {
        var bytes = new byte[2048];
        bytes[0x00] = (byte)version;
        bytes[0x05] = 0x40;
        bytes[0x0F] = 0x40;
        code.CopyTo(bytes, Code);

        var memory = new ZMemory(bytes);
        var header = new StoryHeader(memory);
        return (new InstructionDecoder(memory, header, new ZTextDecoder(memory, header)), memory);
    }

    private static Instruction Decode(ZMachineVersion version, params byte[] code) =>
        Decoder(version, code).Decoder.Decode(Code);

    [Fact]
    public void DecodesTheStandardsIncChkExample()
    {
        // [zm 4] The first worked example: @inc_chk c 0 label as
        // 05 02 00 d4. Long form, 2OP, opcode 5, two small constants, and
        // a one-byte branch on true with offset 20.
        var instruction = Decode(ZMachineVersion.V3, 0x05, 0x02, 0x00, 0xD4);

        Assert.Equal(InstructionForm.LongForm, instruction.Form);
        Assert.Equal(OperandCount.TwoOp, instruction.OperandCount);
        Assert.Equal(5, instruction.OpcodeNumber);
        Assert.Equal(Opcode.IncChk, instruction.Opcode);
        Assert.Equal(
            [new Operand(OperandType.SmallConstant, 2), new Operand(OperandType.SmallConstant, 0)],
            instruction.Operands);

        var branch = Assert.NotNull(instruction.Branch);
        Assert.True(branch.OnTrue);
        Assert.Equal(20, branch.Offset);
        Assert.Equal(Code + 4, instruction.NextAddress);

        // [zm 4.7.2] "Since label is 18 bytes forward from here" means
        // from the byte after the branch data.
        Assert.Equal(instruction.NextAddress + 18, branch.Target(instruction.NextAddress));
        Assert.Equal("inc_chk #02 #00 ?20", instruction.ToString());
    }

    [Fact]
    public void DecodesTheStandardsPrintExample()
    {
        // [zm 4] @print "Hello.^" as b2 11 aa 46 34 16 45 9c a5: short
        // form, 0OP, opcode 2, then four words of text ending with the one
        // whose top bit is set.
        var instruction = Decode(ZMachineVersion.V3, 0xB2, 0x11, 0xAA, 0x46, 0x34, 0x16, 0x45, 0x9C, 0xA5);

        Assert.Equal(InstructionForm.ShortForm, instruction.Form);
        Assert.Equal(OperandCount.ZeroOp, instruction.OperandCount);
        Assert.Equal(Opcode.Print, instruction.Opcode);
        Assert.Empty(instruction.Operands);
        Assert.Equal(Code + 1, instruction.TextAddress);

        // [zm 4.8] Execution continues after the last word of the text.
        Assert.Equal(Code + 9, instruction.NextAddress);
        Assert.Equal(9, instruction.Length);
    }

    [Fact]
    public void DecodesTheStandardsMulExample()
    {
        // [zm 4] @mul 1000 c -> sp as d6 2f 03 e8 02 00: variable form,
        // 2OP, opcode 22, types byte giving a large constant and a
        // variable, then the store to the stack.
        var instruction = Decode(ZMachineVersion.V3, 0xD6, 0x2F, 0x03, 0xE8, 0x02, 0x00);

        Assert.Equal(InstructionForm.VariableForm, instruction.Form);
        Assert.Equal(OperandCount.TwoOp, instruction.OperandCount);
        Assert.Equal(22, instruction.OpcodeNumber);
        Assert.Equal(Opcode.Mul, instruction.Opcode);
        Assert.Equal(
            [new Operand(OperandType.LargeConstant, 1000), new Operand(OperandType.Variable, 2)],
            instruction.Operands);
        Assert.Equal((byte)0, instruction.StoreVariable);
        Assert.Null(instruction.Branch);
        Assert.Equal(Code + 6, instruction.NextAddress);
        Assert.Equal("mul #03E8 L01 -> sp", instruction.ToString());
    }

    [Fact]
    public void DecodesTheStandardsCall1nExample()
    {
        // [zm 4] @call_1n Message as 8f 01 56: short form, 1OP, opcode
        // 15, one large constant. call_1n exists from Version 5.
        var instruction = Decode(ZMachineVersion.V5, 0x8F, 0x01, 0x56);

        Assert.Equal(InstructionForm.ShortForm, instruction.Form);
        Assert.Equal(OperandCount.OneOp, instruction.OperandCount);
        Assert.Equal(15, instruction.OpcodeNumber);
        Assert.Equal(Opcode.Call1n, instruction.Opcode);
        Assert.Equal([new Operand(OperandType.LargeConstant, 0x0156)], instruction.Operands);
        Assert.Null(instruction.StoreVariable);
        Assert.Equal(Code + 3, instruction.NextAddress);
    }

    [Fact]
    public void TheSameBytesAreNotInVersion4()
    {
        // [zm 14] 1OP:15 is not, a store opcode, through Version 4, so
        // the same three bytes need a fourth for the store variable.
        var instruction = Decode(ZMachineVersion.V4, 0x8F, 0x01, 0x56, 0x03);

        Assert.Equal(Opcode.Not, instruction.Opcode);
        Assert.Equal((byte)3, instruction.StoreVariable);
        Assert.Equal(Code + 4, instruction.NextAddress);
    }

    [Fact]
    public void DecodesExtendedFormFromVersion5()
    {
        // [zm 4.3.4] $BE, then the opcode number, then a types byte. Two
        // small constants for log_shift, and a store.
        var instruction = Decode(ZMachineVersion.V5, 0xBE, 0x02, 0x5F, 0x10, 0x03, 0x00);

        Assert.Equal(InstructionForm.ExtendedForm, instruction.Form);
        Assert.Equal(OperandCount.Var, instruction.OperandCount);
        Assert.Equal(2, instruction.OpcodeNumber);
        Assert.Equal(Opcode.LogShift, instruction.Opcode);
        Assert.Equal(2, instruction.Operands.Count);
        Assert.Equal((byte)0, instruction.StoreVariable);
        Assert.Equal(Code + 6, instruction.NextAddress);
    }

    [Fact]
    public void ByteBeIsAnIllegal0OpBeforeVersion5()
    {
        // The editor's note on [zm 4.3]: $BE has top bits $$10, so before
        // Version 5 it is short form 0OP:14, which no version defines.
        Assert.Throws<InvalidDataException>(() => Decode(ZMachineVersion.V4, 0xBE, 0x02, 0x5F));
    }

    [Fact]
    public void AVariableForm2OpCanHaveFourOperands()
    {
        // [zm 4.4.3] $C1 is variable form with bit 5 clear, so 2OP:1, je,
        // and the types byte $55 gives four small constants.
        var instruction = Decode(ZMachineVersion.V3, 0xC1, 0x55, 0x01, 0x02, 0x03, 0x04, 0xC2);

        Assert.Equal(OperandCount.TwoOp, instruction.OperandCount);
        Assert.Equal(Opcode.Je, instruction.Opcode);
        Assert.Equal(4, instruction.Operands.Count);
        Assert.Equal(new Branch(true, 2), instruction.Branch);
        Assert.Equal(Code + 7, instruction.NextAddress);
    }

    [Fact]
    public void OmittedTypesEndTheOperandList()
    {
        // [zm 4.4.3] The standard's example: $$00101111 is a large
        // constant, a variable, and nothing more.
        var instruction = Decode(ZMachineVersion.V5, 0xE0, 0x2F, 0x12, 0x34, 0x05, 0x00);

        Assert.Equal(Opcode.CallVs, instruction.Opcode);
        Assert.Equal(
            [new Operand(OperandType.LargeConstant, 0x1234), new Operand(OperandType.Variable, 5)],
            instruction.Operands);

        // And $$11111111 is a VAR instruction with no operands at all.
        var bare = Decode(ZMachineVersion.V5, 0xE0, 0xFF, 0x00);
        Assert.Empty(bare.Operands);
        Assert.Equal(Code + 3, bare.NextAddress);
    }

    [Fact]
    public void CallVs2TakesASecondTypesByte()
    {
        // [zm 4.4.3.1] VAR:12 reads two types bytes: here a large constant
        // and three variables, then two more large constants, six
        // operands in all, followed by the store.
        var instruction = Decode(
            ZMachineVersion.V5,
            0xEC, 0x2A, 0x0F,
            0x12, 0x34, 0x01, 0x02, 0x03, 0x00, 0x10, 0x00, 0x20,
            0x00);

        Assert.Equal(Opcode.CallVs2, instruction.Opcode);
        Assert.Equal(6, instruction.Operands.Count);
        Assert.Equal(new Operand(OperandType.LargeConstant, 0x0020), instruction.Operands[5]);
        Assert.Equal((byte)0, instruction.StoreVariable);
        Assert.Equal(Code + 13, instruction.NextAddress);
    }

    [Fact]
    public void ALongBranchIsASigned14BitOffset()
    {
        // [zm 4.7] Bit 6 clear means two bytes. Here jz with branch on
        // false and an offset of -3, which is $3FFD in 14 bits.
        var instruction = Decode(ZMachineVersion.V3, 0x90, 0x05, 0x3F, 0xFD);

        var branch = Assert.NotNull(instruction.Branch);
        Assert.False(branch.OnTrue);
        Assert.Equal(-3, branch.Offset);
        Assert.Equal(Code + 4, instruction.NextAddress);

        // [zm 4.7.2] Backward, past the whole instruction and one more.
        Assert.Equal(Code - 1, branch.Target(instruction.NextAddress));
        Assert.Equal("jz #05 ?~-3", instruction.ToString());

        // A positive long offset, 300, for good measure.
        var forward = Decode(ZMachineVersion.V3, 0x90, 0x05, 0x81, 0x2C);
        Assert.Equal(300, Assert.NotNull(forward.Branch).Offset);
    }

    [Fact]
    public void BranchOffsetsZeroAndOneReturnFromTheRoutine()
    {
        // [zm 4.7.1]
        var returnsFalse = Assert.NotNull(Decode(ZMachineVersion.V3, 0x90, 0x05, 0xC0).Branch);
        Assert.True(returnsFalse.ReturnsFalse);
        Assert.Equal("?rfalse", returnsFalse.ToString());

        var returnsTrue = Assert.NotNull(Decode(ZMachineVersion.V3, 0x90, 0x05, 0x41).Branch);
        Assert.True(returnsTrue.ReturnsTrue);
        Assert.False(returnsTrue.OnTrue);
        Assert.Equal("?~rtrue", returnsTrue.ToString());
    }

    [Fact]
    public void AnOpcodeCanBothStoreAndBranch()
    {
        // [zm 14] get_child is marked in both columns: the store byte
        // comes first, then the branch.
        var instruction = Decode(ZMachineVersion.V3, 0xA2, 0x05, 0x00, 0xC5);

        Assert.Equal(Opcode.GetChild, instruction.Opcode);
        Assert.Equal([new Operand(OperandType.Variable, 5)], instruction.Operands);
        Assert.Equal((byte)0, instruction.StoreVariable);
        Assert.Equal(new Branch(true, 5), instruction.Branch);
        Assert.Equal("get_child L04 -> sp ?5", instruction.ToString());
    }

    [Fact]
    public void SaveChangesShapeWithTheVersion()
    {
        // [zm 14] 0OP:181 branches through Version 3, stores in Version
        // 4, and does not exist from Version 5.
        var early = Decode(ZMachineVersion.V3, 0xB5, 0xC2);
        Assert.Equal(Opcode.Save, early.Opcode);
        Assert.NotNull(early.Branch);
        Assert.Null(early.StoreVariable);

        var middle = Decode(ZMachineVersion.V4, 0xB5, 0x07);
        Assert.Equal(Opcode.Save, middle.Opcode);
        Assert.Equal((byte)7, middle.StoreVariable);
        Assert.Null(middle.Branch);

        Assert.Throws<InvalidDataException>(() => Decode(ZMachineVersion.V5, 0xB5, 0x07));
    }

    [Fact]
    public void PullStoresOnlyInVersion6()
    {
        // [zm 14] VAR:233 names its variable as an operand through
        // Version 5 and stores in Version 6. The types byte $7F is one
        // small constant and three omitted.
        var early = Decode(ZMachineVersion.V5, 0xE9, 0x7F, 0x01);
        Assert.Equal(Opcode.Pull, early.Opcode);
        Assert.Single(early.Operands);
        Assert.Null(early.StoreVariable);
        Assert.Equal(Code + 3, early.NextAddress);

        var late = Decode(ZMachineVersion.V6, 0xE9, 0x7F, 0x01, 0x02);
        Assert.Equal((byte)2, late.StoreVariable);
        Assert.Equal(Code + 4, late.NextAddress);
    }

    [Fact]
    public void OpcodesTheVersionDoesNotHaveAreIllegal()
    {
        // [zm 14.2] 2OP:0 never existed, 2OP:29 does not either, and
        // call_2s arrived in Version 4.
        Assert.Throws<InvalidDataException>(() => Decode(ZMachineVersion.V5, 0x00, 0x01, 0x02));
        Assert.Throws<InvalidDataException>(() => Decode(ZMachineVersion.V5, 0x1D, 0x01, 0x02));
        Assert.Throws<InvalidDataException>(() => Decode(ZMachineVersion.V3, 0x19, 0x01, 0x02, 0x00));
        Assert.Equal(Opcode.Call2s, Decode(ZMachineVersion.V4, 0x19, 0x01, 0x02, 0x00).Opcode);
    }

    [Fact]
    public void ExtendedOpcodesFrom30UpAreUnknownRatherThanIllegal()
    {
        // [zm 14.2.1] To be ignored, so they decode, with no store or
        // branch assumed, and can be stepped over.
        var instruction = Decode(ZMachineVersion.V5, 0xBE, 0x1E, 0xFF);

        Assert.Equal(Opcode.Unknown, instruction.Opcode);
        Assert.Equal("ext_30", instruction.Name);
        Assert.Empty(instruction.Operands);
        Assert.Equal(Code + 3, instruction.NextAddress);
    }

    [Fact]
    public void Version6OpcodesAreNotInVersion5()
    {
        // [zm 14] draw_picture is EXT:5, Version 6 only; save_undo is
        // EXT:9 in any version from 5.
        Assert.Throws<InvalidDataException>(() => Decode(ZMachineVersion.V5, 0xBE, 0x05, 0xFF));
        Assert.Equal(Opcode.DrawPicture, Decode(ZMachineVersion.V6, 0xBE, 0x05, 0xFF).Opcode);
        Assert.Equal(Opcode.SaveUndo, Decode(ZMachineVersion.V5, 0xBE, 0x09, 0xFF, 0x00).Opcode);
    }

    [Fact]
    public void SoundEffectIsAcceptedFromVersion3()
    {
        // [zm 14] Listed as "5/3": a Version 5 opcode that The Lurking
        // Horror uses in Version 3.
        Assert.Equal(Opcode.SoundEffect, Decode(ZMachineVersion.V3, 0xF5, 0x5F, 0x01, 0x02).Opcode);
        Assert.Throws<InvalidDataException>(() => Decode(ZMachineVersion.V2, 0xF5, 0x5F, 0x01, 0x02));
    }

    [Fact]
    public void TheOpcodeTableCoversEveryRowOfSection14()
    {
        // [zm 14.1] 119 opcodes. Counting distinct names across every
        // count, number, and version, less the ones the standard ignores
        // rather than defines.
        var names = new HashSet<string>();

        foreach (var version in Enum.GetValues<ZMachineVersion>())
        {
            for (var number = 0; number < 32; number++)
            {
                Add(OpcodeTable.Resolve(InstructionForm.LongForm, OperandCount.TwoOp, number, version));
                Add(OpcodeTable.Resolve(InstructionForm.VariableForm, OperandCount.Var, number, version));
            }

            for (var number = 0; number < 16; number++)
            {
                Add(OpcodeTable.Resolve(InstructionForm.ShortForm, OperandCount.OneOp, number, version));
                Add(OpcodeTable.Resolve(InstructionForm.ShortForm, OperandCount.ZeroOp, number, version));
            }

            for (var number = 0; number < 30; number++)
            {
                Add(OpcodeTable.Resolve(InstructionForm.ExtendedForm, OperandCount.Var, number, version));
            }
        }

        // The table's 119 count and this 120 differ because four numbers
        // change meaning with the version and so carry two names each
        // (call and call_vs, sread and aread, not and call_1n, pop and
        // catch), while save, restore, and not each appear under two
        // operand counts and are one name here.
        Assert.Equal(120, names.Count);

        void Add(OpcodeInfo? info)
        {
            if (info is not null)
            {
                names.Add(info.Name);
            }
        }
    }

    [Fact]
    public void ReadsRoutineHeadersByVersion()
    {
        var (_, memory) = Decoder(ZMachineVersion.V3, 0x02, 0x00, 0x05, 0x00, 0x07, 0xB0);

        // [zm 5.2] Two locals, and [zm 5.2.1] their initial values, then
        // [zm 5.3] the code.
        var early = RoutineHeader.Read(memory, ZMachineVersion.V3, Code);
        Assert.Equal(2, early.LocalCount);
        Assert.Equal([(ushort)5, (ushort)7], early.InitialLocals);
        Assert.Equal(Code + 5, early.CodeAddress);

        // [zm 5.2.1] From Version 5 the values are all zero and absent.
        var late = RoutineHeader.Read(memory, ZMachineVersion.V5, Code);
        Assert.Equal(2, late.LocalCount);
        Assert.Equal([(ushort)0, (ushort)0], late.InitialLocals);
        Assert.Equal(Code + 1, late.CodeAddress);

        // [zm 5.2] At most 15.
        var (_, bad) = Decoder(ZMachineVersion.V5, 0x10);
        Assert.Throws<InvalidDataException>(() => RoutineHeader.Read(bad, ZMachineVersion.V5, Code));
    }
}

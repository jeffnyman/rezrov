using Rezrov.Glulx;
using Rezrov.Glulx.Instructions;

namespace Rezrov.Tests;

public class GlulxDecoderTests
{
    // The first byte of RAM in the test file, where the code is placed.
    private const uint Code = 0x100;

    private static Instruction Decode(params byte[] code)
    {
        var file = TestGlulx.File();
        code.CopyTo(file, (int)Code);
        return new InstructionDecoder(new GlulxMemory(file)).Decode(Code);
    }

    private static Operand Load(AddressingMode mode, uint value) => new(mode, value, false);

    private static Operand Store(AddressingMode mode, uint value) => new(mode, value, true);

    [Fact]
    public void DecodesAddWithTwoConstantsAndAPush()
    {
        // @add 1 2 sp: opcode 10, modes 1 and 1 packed as 11, then mode
        // 8 alone as 08, then the two constant bytes.
        var instruction = Decode(0x10, 0x11, 0x08, 0x01, 0x02);

        Assert.Equal(Opcode.Add, instruction.Opcode);
        Assert.Equal(
            [Load(AddressingMode.Constant8, 1), Load(AddressingMode.Constant8, 2), Store(AddressingMode.Stack, 0)],
            instruction.Operands);
        Assert.Equal(Code + 5, instruction.NextAddress);
        Assert.Equal(5u, instruction.Length);
        Assert.Equal("add #1 #2 ->sp", instruction.ToString());
    }

    [Fact]
    public void OpcodeNumbersComeInThreeLengths()
    {
        // [glulx #instruction] 10, 8010, and C0000010 all mean add.
        var one = Decode(0x10, 0x00, 0x00);
        var two = Decode(0x80, 0x10, 0x00, 0x00);
        var four = Decode(0xC0, 0x00, 0x00, 0x10, 0x00, 0x00);

        Assert.Equal(Opcode.Add, one.Opcode);
        Assert.Equal(Opcode.Add, two.Opcode);
        Assert.Equal(Opcode.Add, four.Opcode);
        Assert.Equal((3u, 4u, 6u), (one.Length, two.Length, four.Length));

        // A number above 7F needs the two-byte form: gestalt is 100.
        var gestalt = Decode(0x81, 0x00, 0x00, 0x00);
        Assert.Equal(Opcode.Gestalt, gestalt.Opcode);
        Assert.Equal(3, gestalt.Operands.Count);
    }

    [Fact]
    public void ConstantsAreSignExtendedAndAddressesAreNot()
    {
        // [glulx #instruction] copy L1 S1 with the store discarded, and
        // the load in each mode that carries data.
        Assert.Equal(0xFFFFFFFFu, Decode(0x40, 0x01, 0xFF).Operands[0].Value);
        Assert.Equal(0xFFFFFFFEu, Decode(0x40, 0x02, 0xFF, 0xFE).Operands[0].Value);
        Assert.Equal(0x7Fu, Decode(0x40, 0x01, 0x7F).Operands[0].Value);
        Assert.Equal(0x12345678u, Decode(0x40, 0x03, 0x12, 0x34, 0x56, 0x78).Operands[0].Value);

        Assert.Equal(0xFFu, Decode(0x40, 0x05, 0xFF).Operands[0].Value);
        Assert.Equal(0xFFFEu, Decode(0x40, 0x06, 0xFF, 0xFE).Operands[0].Value);
        Assert.Equal(0xFFFFFFFCu, Decode(0x40, 0x07, 0xFF, 0xFF, 0xFF, 0xFC).Operands[0].Value);
        Assert.Equal(0xFFu, Decode(0x40, 0x09, 0xFF).Operands[0].Value);
        Assert.Equal(0xFFFEu, Decode(0x40, 0x0A, 0xFF, 0xFE).Operands[0].Value);
        Assert.Equal(0xFFu, Decode(0x40, 0x0D, 0xFF).Operands[0].Value);
        Assert.Equal(0xFFFEu, Decode(0x40, 0x0E, 0xFF, 0xFE).Operands[0].Value);
        Assert.Equal(0xFFFFFFFCu, Decode(0x40, 0x0F, 0xFF, 0xFF, 0xFF, 0xFC).Operands[0].Value);
    }

    [Fact]
    public void EachModeHasItsKindAndSpelling()
    {
        // copy with L1 in mode 5 and S1 in mode 7: one address byte,
        // then four.
        var instruction = Decode(0x40, 0x75, 0x10, 0x00, 0x20, 0x00, 0x30);
        Assert.Equal("copy [10] ->[200030]", instruction.ToString());

        Assert.Equal(OperandKind.Constant, Decode(0x40, 0x00).Operands[0].Kind);
        Assert.Equal(OperandKind.Memory, Decode(0x40, 0x05, 0x10).Operands[0].Kind);
        Assert.Equal(OperandKind.Stack, Decode(0x40, 0x08).Operands[0].Kind);
        Assert.Equal(OperandKind.Local, Decode(0x40, 0x09, 0x10).Operands[0].Kind);
        Assert.Equal(OperandKind.Ram, Decode(0x40, 0x0D, 0x10).Operands[0].Kind);
        Assert.Equal("copy loc[4] ->ram[8]", Decode(0x40, 0xD9, 0x04, 0x08).ToString());
    }

    [Fact]
    public void ModesArePackedTwoToAByteLowBitsFirst()
    {
        // astore L1 L2 L3 with modes 3, 1, 8: the first byte is 13, the
        // second 08 with its high nibble unused.
        var instruction = Decode(0x4C, 0x13, 0x08, 0x00, 0x00, 0x04, 0x00, 0x05);

        Assert.Equal(
            [Load(AddressingMode.Constant32, 0x400), Load(AddressingMode.Constant8, 5), Load(AddressingMode.Stack, 0)],
            instruction.Operands);
        Assert.Equal("astore #400 #5 sp", instruction.ToString());

        // [glulx #instruction] Eight operands take four mode bytes.
        var search = Decode(0x81, 0x51, 0x00, 0x00, 0x00, 0x00);
        Assert.Equal(Opcode.BinarySearch, search.Opcode);
        Assert.Equal(8, search.Operands.Count);
        Assert.Equal(6u, search.Length);
    }

    [Fact]
    public void AnUnusedHighNibbleIsIgnored()
    {
        // return L1 has one operand, so only the low nibble counts.
        var instruction = Decode(0x31, 0xF8);

        Assert.Equal(Opcode.Return, instruction.Opcode);
        Assert.Equal([Load(AddressingMode.Stack, 0)], instruction.Operands);
    }

    [Fact]
    public void AStoreOperandCanDiscardOrPushButNotBeAConstant()
    {
        // [glulx #instruction] Mode 0 throws the value away and mode 8
        // pushes; the constant modes cannot be stored to.
        Assert.Equal(OperandKind.Discard, Decode(0x40, 0x00).Operands[1].Kind);
        Assert.Equal("copy #0 ->_", Decode(0x40, 0x00).ToString());
        Assert.Equal(OperandKind.Stack, Decode(0x40, 0x80).Operands[1].Kind);

        foreach (var mode in new byte[] { 0x10, 0x20, 0x30 })
        {
            var e = Assert.Throws<GlulxException>(() => Decode(0x40, mode, 0, 0, 0, 0, 0));
            Assert.Contains("store operand in constant mode", e.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(0x04)]
    [InlineData(0x0C)]
    public void UnusedModesAreRejected(byte mode)
    {
        // [glulx #instruction] Modes 4 and C are unused.
        var e = Assert.Throws<GlulxException>(() => Decode(0x40, mode));
        Assert.Contains($"Unused addressing mode {mode}", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new byte[] { 0x7F }, "7F")]
    [InlineData(new byte[] { 0x80, 0x05 }, "5")]
    [InlineData(new byte[] { 0xC0, 0x00, 0x10, 0x00 }, "1000")]
    public void UnknownOpcodesAreRejectedByNumber(byte[] code, string number)
    {
        var e = Assert.Throws<GlulxException>(() => Decode(code));
        Assert.Contains($"Unknown opcode {number} ", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CatchStoresBeforeItLoads()
    {
        // [glulx #dictionary-of-opcodes] catch S1 L1: the store comes
        // first, so with modes 8 and 2 the byte is 28.
        var instruction = Decode(0x32, 0x28, 0x00, 0x10);

        Assert.Equal([Store(AddressingMode.Stack, 0), Load(AddressingMode.Constant16, 0x10)], instruction.Operands);
        Assert.Equal("catch ->sp #10", instruction.ToString());
    }

    [Fact]
    public void EveryOpcodeInTheTableDecodes()
    {
        var failures = new List<string>();

        foreach (var info in OpcodeTable.All)
        {
            // The shortest encoding of the number, then all-zero modes,
            // which are legal for loads and stores alike.
            var code = new List<byte>();
            var number = info.Number;
            if (number < 0x80)
            {
                code.Add((byte)number);
            }
            else
            {
                code.Add((byte)(0x80 | (number >> 8)));
                code.Add((byte)number);
            }

            var modeBytes = (info.OperandCount + 1) / 2;
            code.AddRange(new byte[modeBytes]);

            try
            {
                var instruction = Decode(code.ToArray());
                if (instruction.Opcode != info.Opcode || instruction.Operands.Count != info.OperandCount || instruction.Length != code.Count)
                {
                    failures.Add($"{info.Name}: decoded as {instruction}");
                }
            }
            catch (GlulxException e)
            {
                failures.Add($"{info.Name}: {e.Message}");
            }
        }

        Assert.Empty(failures);
    }
}

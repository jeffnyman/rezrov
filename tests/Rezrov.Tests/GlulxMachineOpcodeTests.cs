using Rezrov.Glulx;
using Rezrov.Glulx.Instructions;
using static Rezrov.Tests.GlulxAssembler;

namespace Rezrov.Tests;

/// <summary>
/// The opcodes that compute and move data: integer math, copies of
/// every size, arrays and bits, the stack opcodes, and block memory.
/// </summary>
public class GlulxMachineOpcodeTests
{
    private static GlulxAssembler Program() => new GlulxAssembler().Function("main");

    private static uint Compute(Opcode opcode, long left, long right) =>
        GlulxRun.Run(Program().Op(opcode, C(left), C(right), Ram(0)).Return(C(0))).Ram(0);

    [Theory]
    [InlineData(Opcode.Add, 1, 2, 3u)]
    [InlineData(Opcode.Add, -1, 1, 0u)]
    [InlineData(Opcode.Sub, 1, 2, 0xFFFFFFFFu)]
    [InlineData(Opcode.Mul, 0x10000, 0x10000, 0u)]
    [InlineData(Opcode.Mul, -3, 4, 0xFFFFFFF4u)]
    [InlineData(Opcode.Div, 11, 2, 5u)]
    [InlineData(Opcode.Div, -11, 2, 0xFFFFFFFBu)]
    [InlineData(Opcode.Div, 11, -2, 0xFFFFFFFBu)]
    [InlineData(Opcode.Div, -11, -2, 5u)]
    [InlineData(Opcode.Mod, 13, 5, 3u)]
    [InlineData(Opcode.Mod, -13, 5, 0xFFFFFFFDu)]
    [InlineData(Opcode.Mod, 13, -5, 3u)]
    [InlineData(Opcode.Mod, -13, -5, 0xFFFFFFFDu)]
    [InlineData(Opcode.BitAnd, 0xFF0F, 0x0FF0, 0x0F00u)]
    [InlineData(Opcode.BitOr, 0xFF00, 0x00F0, 0xFFF0u)]
    [InlineData(Opcode.BitXor, 0xFFFF, 0x0FF0, 0xF00Fu)]
    public void IntegerMathIsSignedAndTruncates(Opcode opcode, long left, long right, uint expected)
    {
        // [glulx #integer-math] The specification's own division and
        // remainder examples: rounding toward zero, the remainder taking
        // the sign of the dividend.
        Assert.Equal(expected, Compute(opcode, left, right));
    }

    [Fact]
    public void NegatesAndComplements()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.Neg, C(1), Ram(0))
            .Op(Opcode.Neg, C(int.MinValue), Ram(4))
            .Op(Opcode.BitNot, C(0), Ram(8))
            .Return(C(0)));

        Assert.Equal(0xFFFFFFFFu, machine.Ram(0));
        Assert.Equal(0x80000000u, machine.Ram(4));
        Assert.Equal(0xFFFFFFFFu, machine.Ram(8));
    }

    [Theory]
    [InlineData(Opcode.ShiftL, 1, 31, 0x80000000u)]
    [InlineData(Opcode.ShiftL, 1, 32, 0u)]
    [InlineData(Opcode.ShiftL, 1, -1, 0u)]
    [InlineData(Opcode.ShiftL, 5, 0, 5u)]
    [InlineData(Opcode.UShiftR, int.MinValue, 31, 1u)]
    [InlineData(Opcode.UShiftR, int.MinValue, 32, 0u)]
    [InlineData(Opcode.SShiftR, int.MinValue, 31, 0xFFFFFFFFu)]
    [InlineData(Opcode.SShiftR, int.MinValue, 32, 0xFFFFFFFFu)]
    [InlineData(Opcode.SShiftR, int.MinValue, 40, 0xFFFFFFFFu)]
    [InlineData(Opcode.SShiftR, 0x40000000, 40, 0u)]
    [InlineData(Opcode.SShiftR, -8, 1, 0xFFFFFFFCu)]
    public void ShiftsOfThirtyTwoOrMoreClearOrFill(Opcode opcode, long left, long right, uint expected)
    {
        // [glulx op:shiftl] The count is unsigned, so -1 is more than
        // 32; [glulx op:sshiftr] the sign fills a signed right shift.
        Assert.Equal(expected, Compute(opcode, left, right));
    }

    [Theory]
    [InlineData(Opcode.Div, 1, 0)]
    [InlineData(Opcode.Mod, 1, 0)]
    [InlineData(Opcode.Div, int.MinValue, -1)]
    [InlineData(Opcode.Mod, int.MinValue, -1)]
    public void DivisionByZeroAndTheOverflowingQuotientAreFatal(Opcode opcode, long left, long right)
    {
        // [glulx op:div] Both are errors, the second since 2024.
        Assert.Throws<GlulxException>(() => Compute(opcode, left, right));
    }

    [Fact]
    public void CopiesOfEverySizeTruncateAndNeverSignExtend()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.Copy, C(0x12345678), Ram(0))
            .Op(Opcode.CopyB, Ram(0), Ram(4))
            .Op(Opcode.CopyS, Ram(0), Ram(8))
            .Op(Opcode.CopyB, Ram(1), Sp)
            .Op(Opcode.Copy, Sp, Ram(12))
            .Op(Opcode.CopyB, C(0x1FF), Sp)
            .Op(Opcode.Copy, Sp, Ram(16))
            .Op(Opcode.Copy, C(0x1FFFF), Sp)
            .Op(Opcode.CopyS, Sp, Sp)
            .Op(Opcode.Copy, Sp, Ram(20))
            .Op(Opcode.Copy, C(-1), Ram(24))
            .Op(Opcode.CopyS, C(0x1234), Ram(24))
            .Return(C(0)));

        // [glulx op:copyb] One byte from memory to memory; two bytes;
        // a byte to the stack is a value from 0 to 255; a constant is
        // truncated; a value through the stack is popped, truncated,
        // and pushed; and a store writes only its field.
        Assert.Equal(0x12000000u, machine.Ram(4));
        Assert.Equal(0x12340000u, machine.Ram(8));
        Assert.Equal(0x34u, machine.Ram(12));
        Assert.Equal(0xFFu, machine.Ram(16));
        Assert.Equal(0xFFFFu, machine.Ram(20));
        Assert.Equal(0x1234FFFFu, machine.Ram(24));
    }

    [Fact]
    public void SignExtendsShortsAndBytes()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.SexS, C(0x8000), Ram(0))
            .Op(Opcode.SexS, C(0x12347FFF), Ram(4))
            .Op(Opcode.SexB, C(0x80), Ram(8))
            .Op(Opcode.SexB, C(0x1234567F), Ram(12))
            .Return(C(0)));

        // [glulx op:sexs] The upper bits are overwritten either way.
        Assert.Equal(0xFFFF8000u, machine.Ram(0));
        Assert.Equal(0x7FFFu, machine.Ram(4));
        Assert.Equal(0xFFFFFF80u, machine.Ram(8));
        Assert.Equal(0x7Fu, machine.Ram(12));
    }

    [Fact]
    public void ArraysIndexBySignedElementCount()
    {
        const uint Base = GlulxRun.RamStart;
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.AStore, C(Base), C(1), C(0x11223344))
            .Op(Opcode.ALoad, C(Base + 8), C(-1), Ram(8))
            .Op(Opcode.AStoreS, C(Base), C(6), C(0x1FFFF))
            .Op(Opcode.ALoadS, C(Base + 16), C(-2), Ram(16))
            .Op(Opcode.AStoreB, C(Base), C(20), C(0x1FE))
            .Op(Opcode.ALoadB, C(Base + 24), C(-4), Ram(24))
            .Return(C(0)));

        // [glulx #array-data] Stores truncate, loads widen without sign
        // extension, and a negative index reaches before the base.
        Assert.Equal(0x11223344u, machine.Ram(4));
        Assert.Equal(0x11223344u, machine.Ram(8));
        Assert.Equal(0xFFFF0000u, machine.Ram(12));
        Assert.Equal(0xFFFFu, machine.Ram(16));
        Assert.Equal(0xFE000000u, machine.Ram(20));
        Assert.Equal(0xFEu, machine.Ram(24));
    }

    [Fact]
    public void BitsAreNumberedFromTheLeastSignificantBitAcrossBytes()
    {
        // [glulx op:astorebit] The specification's examples at 1002,
        // moved to 402 so that -9 lands on the first byte of RAM.
        const uint Base = GlulxRun.RamStart + 2;
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.AStoreBit, C(Base), C(0), C(1))
            .Op(Opcode.AStoreBit, C(Base), C(7), C(1))
            .Op(Opcode.AStoreBit, C(Base), C(8), C(1))
            .Op(Opcode.AStoreBit, C(Base), C(9), C(1))
            .Op(Opcode.AStoreBit, C(Base), C(-1), C(1))
            .Op(Opcode.AStoreBit, C(Base), C(-3), C(1))
            .Op(Opcode.AStoreBit, C(Base), C(-8), C(1))
            .Op(Opcode.AStoreBit, C(Base), C(-9), C(1))
            .Op(Opcode.ALoadBit, C(Base), C(-9), Ram(4))
            .Op(Opcode.ALoadBit, C(Base), C(1), Ram(8))
            .Op(Opcode.AStoreBit, C(Base), C(7), C(0))
            .Op(Opcode.ALoadB, C(Base), C(0), Ram(12))
            .Return(C(0)));

        // Bytes 400 to 403 after the eight sets and the one clear: 80,
        // A1, 01, 03.
        Assert.Equal(0x80A10103u, machine.Ram(0));
        Assert.Equal(1u, machine.Ram(4));
        Assert.Equal(0u, machine.Ram(8));
        Assert.Equal(0x01u, machine.Ram(12));
    }

    [Fact]
    public void StkCopyDuplicatesTheTopValuesInOrder()
    {
        // [glulx op:stkcopy] The specification's example: 5 4 3 2 1 0
        // with 0 on top, copy 3, gives 5 4 3 2 1 0 2 1 0.
        var code = Program();
        for (var value = 5; value >= 0; value--)
        {
            code.Op(Opcode.Copy, C(value), Sp);
        }

        code.Op(Opcode.StkCopy, C(3));
        var machine = GlulxRun.Run(PopAll(code, 9));

        Assert.Equal(new uint[] { 0, 1, 2, 0, 1, 2, 3, 4, 5 }, Popped(machine, 9));
    }

    [Fact]
    public void StkRollRotatesUpAndDown()
    {
        // [glulx op:stkroll] The specification's example: 8 7 6 5 4 3 2
        // 1 0 with 0 on top; roll 5 by 1 gives 8 7 6 5 0 4 3 2 1; roll 9
        // by -3 gives 5 0 4 3 2 1 8 7 6.
        var code = Program();
        for (var value = 8; value >= 0; value--)
        {
            code.Op(Opcode.Copy, C(value), Sp);
        }

        code.Op(Opcode.StkRoll, C(5), C(1));
        var after = GlulxRun.Run(PopAll(new GlulxAssembler().Function("main").Op(Opcode.Copy, C(0), Sp), 1));
        Assert.Equal(new uint[] { 0 }, Popped(after, 1));

        code.Op(Opcode.StkRoll, C(9), C(-3));
        var machine = GlulxRun.Run(PopAll(code, 9));

        Assert.Equal(new uint[] { 6, 7, 8, 1, 2, 3, 4, 0, 5 }, Popped(machine, 9));
    }

    [Fact]
    public void StkRollByOneStepMatchesTheFirstExample()
    {
        var code = Program();
        for (var value = 8; value >= 0; value--)
        {
            code.Op(Opcode.Copy, C(value), Sp);
        }

        code.Op(Opcode.StkRoll, C(5), C(1));
        var machine = GlulxRun.Run(PopAll(code, 9));

        Assert.Equal(new uint[] { 1, 2, 3, 4, 0, 5, 6, 7, 8 }, Popped(machine, 9));
    }

    [Fact]
    public void StkSwapPeekAndCountWorkAboveTheFrame()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.Copy, C(1), Sp)
            .Op(Opcode.Copy, C(2), Sp)
            .Op(Opcode.Copy, C(3), Sp)
            .Op(Opcode.StkPeek, C(2), Ram(0))
            .Op(Opcode.StkCount, Sp)
            .Op(Opcode.Copy, Sp, Ram(4))
            .Op(Opcode.StkSwap)
            .Op(Opcode.Copy, Sp, Ram(8))
            .Op(Opcode.Copy, Sp, Ram(12))
            .Op(Opcode.StkCount, Ram(16))
            .Return(C(0)));

        // [glulx op:stkpeek] Depth 2 below 3 2 1 is 1; [glulx
        // op:stkcount] the count is taken before the push; and
        // [glulx op:stkswap] the top two change places.
        Assert.Equal(1u, machine.Ram(0));
        Assert.Equal(3u, machine.Ram(4));
        Assert.Equal(2u, machine.Ram(8));
        Assert.Equal(3u, machine.Ram(12));
        Assert.Equal(1u, machine.Ram(16));
    }

    [Fact]
    public void StackOpcodesCannotReachBelowTheFrame()
    {
        // [glulx #the-stack-1] Only the values above the current frame.
        var one = Program().Op(Opcode.Copy, C(1), Sp);

        Assert.Throws<GlulxException>(() => GlulxRun.Run(Program().Op(Opcode.Copy, C(1), Sp).Op(Opcode.StkPeek, C(1), Discard)));
        Assert.Throws<GlulxException>(() => GlulxRun.Run(Program().Op(Opcode.Copy, C(1), Sp).Op(Opcode.StkSwap)));
        Assert.Throws<GlulxException>(() => GlulxRun.Run(Program().Op(Opcode.Copy, C(1), Sp).Op(Opcode.StkCopy, C(2))));
        Assert.Throws<GlulxException>(() => GlulxRun.Run(Program().Op(Opcode.Copy, C(1), Sp).Op(Opcode.StkRoll, C(2), C(1))));
        Assert.Throws<GlulxException>(() => GlulxRun.Run(one.Op(Opcode.StkRoll, C(-1), C(1))));
    }

    [Fact]
    public void BlockZeroAndCopyHandleOverlapAndRom()
    {
        const uint Base = GlulxRun.RamStart + 0x40;
        var code = Program()
            .Op(Opcode.Copy, C(0x01020304), Mem(Base))
            .Op(Opcode.Copy, C(0x05060708), Mem(Base + 4))
            .Op(Opcode.MCopy, C(8), C(Base), C(Base + 2))
            .Op(Opcode.Copy, Mem(Base), Ram(0))
            .Op(Opcode.Copy, Mem(Base + 4), Ram(4))
            .Op(Opcode.Copy, Mem(Base + 8), Ram(8))
            .Op(Opcode.MCopy, C(8), C(Base + 2), C(Base))
            .Op(Opcode.Copy, Mem(Base), Ram(12))
            .Op(Opcode.Copy, Mem(Base + 4), Ram(16))
            .Op(Opcode.MZero, C(3), C(Base + 1))
            .Op(Opcode.Copy, Mem(Base), Ram(20))
            .Op(Opcode.MZero, C(0), C(0))
            .Op(Opcode.MCopy, C(0), C(0), C(0))
            .Return(C(0));

        var machine = GlulxRun.Run(code);

        // [glulx op:mcopy] Upward and downward copies over overlapping
        // blocks, [glulx op:mzero] a zeroed run, and lengths of zero
        // doing nothing even at address zero.
        Assert.Equal(0x01020102u, machine.Ram(0));
        Assert.Equal(0x03040506u, machine.Ram(4));
        Assert.Equal(0x07080000u, machine.Ram(8));
        Assert.Equal(0x01020304u, machine.Ram(12));
        Assert.Equal(0x05060708u, machine.Ram(16));
        Assert.Equal(0x01000000u, machine.Ram(20));

        Assert.Throws<GlulxException>(() => GlulxRun.Run(Program().Op(Opcode.MZero, C(4), C(0x100))));
        Assert.Throws<GlulxException>(() => GlulxRun.Run(Program().Op(Opcode.MCopy, C(4), C(0x400), C(0x100))));
    }

    // Pops the top count values into RAM in order, the first popped at
    // offset 0.
    private static GlulxAssembler PopAll(GlulxAssembler code, int count)
    {
        for (var i = 0; i < count; i++)
        {
            code.Op(Opcode.Copy, Sp, Ram((uint)(4 * i)));
        }

        return code.Return(C(0));
    }

    private static uint[] Popped(Rezrov.Glulx.Execution.GlulxMachine machine, int count) =>
        Enumerable.Range(0, count).Select(i => machine.Ram((uint)(4 * i))).ToArray();
}

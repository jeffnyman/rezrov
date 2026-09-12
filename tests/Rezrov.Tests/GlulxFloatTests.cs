using Rezrov.Glulx.Execution;
using Rezrov.Glulx.Instructions;
using static Rezrov.Tests.GlulxAssembler;

namespace Rezrov.Tests;

/// <summary>
/// [glulx #opcodes_float] The floating-point opcodes and [glulx
/// #opcodes_double] their double-precision twins: encoding, the
/// conversions and their clamps, arithmetic with its infinities and
/// NaNs, the modulo pair, the special cases of the functions, and the
/// comparisons.
/// </summary>
public class GlulxFloatTests
{
    private const uint One = 0x3F800000;
    private const uint Two = 0x40000000;
    private const uint MinusTwo = 0xC0000000;
    private const uint Half = 0x3F000000;
    private const uint MinusHalf = 0xBF000000;
    private const uint MinusZero = 0x80000000;
    private const uint Inf = 0x7F800000;
    private const uint MinusInf = 0xFF800000;
    private const uint NaN = 0x7FC00000;
    private const uint Pi = 0x40490FDB;

    private static GlulxAssembler Program() => new GlulxAssembler().Function("main");

    private static uint Unary(Opcode opcode, uint value) =>
        GlulxRun.Run(Program().Op(opcode, C(value), Ram(0)).Return(C(0))).Ram(0);

    private static uint Binary(Opcode opcode, uint left, uint right) =>
        GlulxRun.Run(Program().Op(opcode, C(left), C(right), Ram(0)).Return(C(0))).Ram(0);

    private static uint F(float value) => GlulxFloat.Encode(value);

    /// <summary>
    /// [glulx #opcodes_float] An inexact result may differ in its last
    /// bit from one platform's math library to another's, as pi does
    /// between 40490FDA and 40490FDB, so a transcendental result is
    /// checked to within one unit in the last place. A zero is checked
    /// exactly, sign and all.
    /// </summary>
    private static void AssertNear(float expected, uint actual)
    {
        if (expected == 0)
        {
            Assert.Equal(F(expected), actual);
            return;
        }

        var decoded = GlulxFloat.Decode(actual);
        Assert.True(
            decoded == expected || decoded == MathF.BitIncrement(expected) || decoded == MathF.BitDecrement(expected),
            $"Expected {F(expected):X8} or a neighbor, got {actual:X8}.");
    }

    private static void AssertNear(double expected, double actual) =>
        Assert.True(
            actual == expected || actual == Math.BitIncrement(expected) || actual == Math.BitDecrement(expected),
            $"Expected {expected:R} or a neighbor, got {actual:R}.");

    private static (uint High, uint Low) D(double value) => GlulxFloat.EncodeDouble(value);

    /// <summary>
    /// A double opcode on one pair, storing low then high into RAM.
    /// </summary>
    private static double DUnary(Opcode opcode, double value)
    {
        var (high, low) = D(value);
        var machine = GlulxRun.Run(Program().Op(opcode, C(high), C(low), Ram(4), Ram(0)).Return(C(0)));
        return GlulxFloat.DecodeDouble(machine.Ram(0), machine.Ram(4));
    }

    private static double DBinary(Opcode opcode, double left, double right)
    {
        var (lh, ll) = D(left);
        var (rh, rl) = D(right);
        var machine = GlulxRun.Run(Program().Op(opcode, C(lh), C(ll), C(rh), C(rl), Ram(4), Ram(0)).Return(C(0)));
        return GlulxFloat.DecodeDouble(machine.Ram(0), machine.Ram(4));
    }

    /// <summary>
    /// Whether a branch opcode with these operands branches.
    /// </summary>
    private static bool Branches(Opcode opcode, params uint[] operands)
    {
        var args = operands.Select(o => C(o)).Append(To("taken")).ToArray();
        var machine = GlulxRun.Run(Program()
            .Op(opcode, args)
            .Return(C(0))
            .Label("taken")
            .Op(Opcode.Copy, C(1), Ram(0))
            .Return(C(0)));
        return machine.Ram(0) == 1;
    }

    [Theory]
    [InlineData(0, 0u)]
    [InlineData(1, One)]
    [InlineData(-2, MinusTwo)]
    [InlineData(100, 0x42C80000u)]
    [InlineData(-1, 0xBF800000u)]
    [InlineData(0x1000001, 0x4B800000u)]
    public void IntegersConvertToTheSpecificationsEncodings(int value, uint expected)
    {
        // [glulx op:numtof] The specification's examples, and a value
        // past 1000000 hex that rounds to a power of two.
        Assert.Equal(expected, Unary(Opcode.NumToF, (uint)value));
    }

    [Theory]
    [InlineData(One, 1u, 1u)]
    [InlineData(Pi, 3u, 3u)]
    [InlineData(0xC0200000u, 0xFFFFFFFEu, 0xFFFFFFFDu)]
    [InlineData(MinusHalf, 0u, 0xFFFFFFFFu)]
    [InlineData(Inf, 0x7FFFFFFFu, 0x7FFFFFFFu)]
    [InlineData(MinusInf, 0x80000000u, 0x80000000u)]
    [InlineData(NaN, 0x7FFFFFFFu, 0x7FFFFFFFu)]
    [InlineData(0xFFC00000u, 0x80000000u, 0x80000000u)]
    [InlineData(0x4F000000u, 0x7FFFFFFFu, 0x7FFFFFFFu)]
    [InlineData(0xCF000000u, 0x80000000u, 0x80000000u)]
    public void FloatsConvertToIntegersTruncatedOrRoundedAndClamped(uint value, uint truncated, uint nearest)
    {
        // [glulx op:ftonumz] Toward zero, [glulx op:ftonumn] to the
        // nearest with halves away from zero, and 7FFFFFFF or 80000000
        // by the sign for anything that does not fit, NaN included.
        Assert.Equal(truncated, Unary(Opcode.FToNumZ, value));
        Assert.Equal(nearest, Unary(Opcode.FToNumN, value));
    }

    [Fact]
    public void ArithmeticFollowsTheSpecificationsTable()
    {
        // [glulx #floats] The table of special values, and 1 + 1 = 2.
        Assert.Equal(Two, Binary(Opcode.FAdd, One, One));
        Assert.Equal(MinusTwo, Binary(Opcode.FSub, One, 0x40400000));
        Assert.Equal(Inf, Binary(Opcode.FDiv, One, 0));
        Assert.Equal(MinusInf, Binary(Opcode.FDiv, 0xBF800000, 0));
        Assert.Equal(0u, Binary(Opcode.FDiv, One, Inf));
        Assert.Equal(MinusZero, Binary(Opcode.FDiv, One, MinusInf));
        Assert.True(GlulxFloat.IsNaN(Binary(Opcode.FDiv, 0, 0)));
        Assert.Equal(0u, Binary(Opcode.FMul, Two, 0));
        Assert.Equal(MinusZero, Binary(Opcode.FMul, Two, MinusZero));
        Assert.True(GlulxFloat.IsNaN(Binary(Opcode.FMul, Inf, 0)));
        Assert.Equal(Inf, Binary(Opcode.FMul, Inf, One));
        Assert.Equal(Inf, Binary(Opcode.FAdd, Inf, Inf));
        Assert.True(GlulxFloat.IsNaN(Binary(Opcode.FSub, Inf, Inf)));
        Assert.True(GlulxFloat.IsNaN(Binary(Opcode.FDiv, Inf, Inf)));
        Assert.True(GlulxFloat.IsNaN(Binary(Opcode.FAdd, NaN, One)));
    }

    [Theory]
    [InlineData(5.5f, 2f, 1.5f, 2f)]
    [InlineData(-5.5f, 2f, -1.5f, -2f)]
    [InlineData(5.5f, -2f, 1.5f, -2f)]
    [InlineData(-5.5f, -2f, -1.5f, 2f)]
    [InlineData(0f, 2f, 0f, 0f)]
    [InlineData(0f, -2f, 0f, -0f)]
    [InlineData(1f, float.PositiveInfinity, 1f, 0f)]
    [InlineData(-1f, float.PositiveInfinity, -1f, -0f)]
    [InlineData(0.5f, 1f, 0.5f, 0f)]
    public void ModuloGivesRemainderAndQuotient(float dividend, float divisor, float remainder, float quotient)
    {
        // [glulx op:fmod] The remainder with the dividend's sign, the
        // quotient rounded toward zero with the division's sign, even
        // when it is zero.
        var machine = GlulxRun.Run(Program().Op(Opcode.FMod, C(F(dividend)), C(F(divisor)), Ram(0), Ram(4)).Return(C(0)));
        Assert.Equal(F(remainder), machine.Ram(0));
        Assert.Equal(F(quotient), machine.Ram(4));
    }

    [Fact]
    public void ModuloOfAnInfinityOrByZeroIsNaN()
    {
        // [glulx op:fmod] Both results.
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.FMod, C(Inf), C(One), Ram(0), Ram(4))
            .Op(Opcode.FMod, C(One), C(0), Ram(8), Ram(12))
            .Return(C(0)));
        Assert.True(GlulxFloat.IsNaN(machine.Ram(0)));
        Assert.True(GlulxFloat.IsNaN(machine.Ram(4)));
        Assert.True(GlulxFloat.IsNaN(machine.Ram(8)));
        Assert.True(GlulxFloat.IsNaN(machine.Ram(12)));
    }

    [Theory]
    [InlineData(Half, One, 0u)]
    [InlineData(MinusHalf, MinusZero, 0xBF800000u)]
    [InlineData(MinusZero, MinusZero, MinusZero)]
    [InlineData(Inf, Inf, Inf)]
    [InlineData(MinusInf, MinusInf, MinusInf)]
    [InlineData(Two, Two, Two)]
    public void CeilingAndFloorKeepTheSign(uint value, uint ceiling, uint floor)
    {
        // [glulx op:ceil] ceil(-0.5) is -0, floor(0.5) is 0, -0 stays
        // -0, and an infinity stays itself.
        Assert.Equal(ceiling, Unary(Opcode.Ceil, value));
        Assert.Equal(floor, Unary(Opcode.Floor, value));
    }

    [Fact]
    public void RootsExponentialsAndLogarithms()
    {
        // [glulx op:sqrt] The special cases listed.
        Assert.Equal(Two, Unary(Opcode.Sqrt, 0x40800000));
        Assert.Equal(MinusZero, Unary(Opcode.Sqrt, MinusZero));
        Assert.True(GlulxFloat.IsNaN(Unary(Opcode.Sqrt, 0xBF800000)));
        Assert.Equal(One, Unary(Opcode.Exp, 0));
        Assert.Equal(One, Unary(Opcode.Exp, MinusZero));
        Assert.Equal(0u, Unary(Opcode.Exp, MinusInf));
        Assert.Equal(0u, Unary(Opcode.Log, One));
        Assert.Equal(MinusInf, Unary(Opcode.Log, 0));
        Assert.Equal(MinusInf, Unary(Opcode.Log, MinusZero));
        Assert.True(GlulxFloat.IsNaN(Unary(Opcode.Log, 0xBF800000)));
    }

    [Theory]
    [InlineData(2f, 3f, 8f)]
    [InlineData(1f, float.NaN, 1f)]
    [InlineData(float.NaN, 0f, 1f)]
    [InlineData(-1f, float.PositiveInfinity, 1f)]
    [InlineData(-1f, float.NegativeInfinity, 1f)]
    [InlineData(0f, -1f, float.PositiveInfinity)]
    [InlineData(0f, 3f, 0f)]
    [InlineData(0.5f, float.NegativeInfinity, float.PositiveInfinity)]
    [InlineData(2f, float.NegativeInfinity, 0f)]
    [InlineData(0.5f, float.PositiveInfinity, 0f)]
    [InlineData(2f, float.PositiveInfinity, float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity, -3f, -0f)]
    [InlineData(float.NegativeInfinity, -2f, 0f)]
    [InlineData(float.NegativeInfinity, 3f, float.NegativeInfinity)]
    [InlineData(float.NegativeInfinity, 2f, float.PositiveInfinity)]
    [InlineData(float.PositiveInfinity, -1f, 0f)]
    [InlineData(float.PositiveInfinity, 1f, float.PositiveInfinity)]
    public void PowHonorsItsBreathtakingSpecialCases(float value, float exponent, float expected)
    {
        // [glulx op:pow] The list from the libc man page.
        Assert.Equal(F(expected), Binary(Opcode.Pow, F(value), F(exponent)));
    }

    [Fact]
    public void PowOfNegativeZeroAndOfANegativeBase()
    {
        // [glulx op:pow] The cases where the sign of a zero matters,
        // and a negative base to a non-integer power.
        Assert.Equal(One, Binary(Opcode.Pow, NaN, MinusZero));
        Assert.Equal(MinusInf, Binary(Opcode.Pow, MinusZero, 0xBF800000));
        Assert.Equal(Inf, Binary(Opcode.Pow, MinusZero, MinusTwo));
        Assert.Equal(MinusZero, Binary(Opcode.Pow, MinusZero, 0x40400000));
        Assert.Equal(0u, Binary(Opcode.Pow, MinusZero, Two));
        Assert.True(GlulxFloat.IsNaN(Binary(Opcode.Pow, MinusTwo, Half)));
    }

    [Fact]
    public void TrigonometryAndItsSpecialCases()
    {
        // [glulx op:sin] sin and cos of infinity are NaN, asin beyond
        // one is NaN, atan of infinity is pi/2, and the exact cases.
        Assert.Equal(0u, Unary(Opcode.Sin, 0));
        Assert.Equal(One, Unary(Opcode.Cos, 0));
        Assert.Equal(0u, Unary(Opcode.Tan, 0));
        Assert.True(GlulxFloat.IsNaN(Unary(Opcode.Sin, Inf)));
        Assert.True(GlulxFloat.IsNaN(Unary(Opcode.Cos, MinusInf)));
        Assert.True(GlulxFloat.IsNaN(Unary(Opcode.Tan, Inf)));
        Assert.True(GlulxFloat.IsNaN(Unary(Opcode.ASin, Two)));
        Assert.True(GlulxFloat.IsNaN(Unary(Opcode.ACos, MinusTwo)));
        AssertNear(MathF.PI / 2, Unary(Opcode.ATan, Inf));
        AssertNear(-MathF.PI / 2, Unary(Opcode.ATan, MinusInf));
        Assert.Equal(0u, Unary(Opcode.ASin, 0));
        AssertNear(MathF.PI / 2, Unary(Opcode.ACos, 0));
    }

    [Theory]
    [InlineData(0f, -2f, MathF.PI)]
    [InlineData(0f, 2f, 0f)]
    [InlineData(1f, 0f, MathF.PI / 2)]
    [InlineData(1f, float.NegativeInfinity, MathF.PI)]
    [InlineData(float.PositiveInfinity, 1f, MathF.PI / 2)]
    [InlineData(float.NegativeInfinity, float.NegativeInfinity, -3 * MathF.PI / 4)]
    [InlineData(float.PositiveInfinity, float.PositiveInfinity, MathF.PI / 4)]
    public void ATan2HonorsItsSpecialCases(float y, float x, float expected)
    {
        // [glulx op:atan2] Y first, X second, and the list.
        AssertNear(expected, Binary(Opcode.ATan2, F(y), F(x)));
    }

    [Fact]
    public void ATan2DistinguishesTheSignsOfZero()
    {
        // [glulx op:atan2] The cases where the sign of a zero matters.
        AssertNear(MathF.PI, Binary(Opcode.ATan2, 0, MinusZero));
        AssertNear(-MathF.PI, Binary(Opcode.ATan2, MinusZero, MinusZero));
        Assert.Equal(0u, Binary(Opcode.ATan2, 0, 0));
        Assert.Equal(MinusZero, Binary(Opcode.ATan2, MinusZero, 0));
        AssertNear(-MathF.PI / 2, Binary(Opcode.ATan2, 0xBF800000, MinusZero));
        Assert.Equal(MinusZero, Binary(Opcode.ATan2, 0xBF800000, Inf));
    }

    [Fact]
    public void NaNAndInfinityAreRecognizedByTheirBits()
    {
        // [glulx op:jisnan] Any NaN of either sign; [glulx op:jisinf]
        // exactly the two infinities.
        Assert.True(Branches(Opcode.JIsNaN, NaN));
        Assert.True(Branches(Opcode.JIsNaN, 0xFF800001));
        Assert.False(Branches(Opcode.JIsNaN, Inf));
        Assert.False(Branches(Opcode.JIsNaN, One));
        Assert.True(Branches(Opcode.JIsInf, Inf));
        Assert.True(Branches(Opcode.JIsInf, MinusInf));
        Assert.False(Branches(Opcode.JIsInf, NaN));
        Assert.False(Branches(Opcode.JIsInf, 0x7F7FFFFF));
    }

    [Fact]
    public void NearEqualityFollowsTheTolerance()
    {
        var nearOne = F(1.05f);
        var tenth = F(0.1f);
        var minusTenth = F(-0.1f);

        // [glulx op:jfeq] Within the tolerance either way, its sign
        // ignored; never with a NaN; always with an infinite tolerance
        // except opposite infinities; exactly with a zero tolerance,
        // where +0 equals -0.
        Assert.True(Branches(Opcode.JFeq, One, nearOne, tenth));
        Assert.True(Branches(Opcode.JFeq, nearOne, One, minusTenth));
        Assert.False(Branches(Opcode.JFeq, One, Two, tenth));
        Assert.False(Branches(Opcode.JFeq, NaN, One, tenth));
        Assert.False(Branches(Opcode.JFeq, One, NaN, tenth));
        Assert.False(Branches(Opcode.JFeq, One, One, NaN));
        Assert.True(Branches(Opcode.JFeq, One, Two, Inf));
        Assert.True(Branches(Opcode.JFeq, Inf, One, Inf));
        Assert.False(Branches(Opcode.JFeq, Inf, MinusInf, Inf));
        Assert.True(Branches(Opcode.JFeq, Inf, Inf, 0));
        Assert.True(Branches(Opcode.JFeq, 0, MinusZero, 0));
        Assert.False(Branches(Opcode.JFeq, One, nearOne, 0));

        // [glulx op:jfne] The reverse, and NaN branches.
        Assert.False(Branches(Opcode.JFne, One, nearOne, tenth));
        Assert.True(Branches(Opcode.JFne, One, Two, tenth));
        Assert.True(Branches(Opcode.JFne, NaN, One, tenth));
        Assert.True(Branches(Opcode.JFne, One, One, NaN));
    }

    [Fact]
    public void OrderingComparisonsNeverBranchOnNaN()
    {
        // [glulx op:jflt] +0 and -0 are equal, and NaN is nothing.
        Assert.True(Branches(Opcode.JFlt, One, Two));
        Assert.False(Branches(Opcode.JFlt, Two, One));
        Assert.True(Branches(Opcode.JFle, One, One));
        Assert.True(Branches(Opcode.JFle, MinusZero, 0));
        Assert.True(Branches(Opcode.JFge, MinusZero, 0));
        Assert.False(Branches(Opcode.JFgt, 0, MinusZero));
        Assert.True(Branches(Opcode.JFgt, Inf, One));
        Assert.True(Branches(Opcode.JFlt, MinusInf, MinusTwo));
        Assert.False(Branches(Opcode.JFlt, NaN, One));
        Assert.False(Branches(Opcode.JFle, NaN, NaN));
        Assert.False(Branches(Opcode.JFgt, One, NaN));
        Assert.False(Branches(Opcode.JFge, NaN, One));
    }

    [Fact]
    public void DoublesConvertAndAreStoredLowWordFirst()
    {
        // [glulx op:numtod] The result is S2:S1, so the low word goes
        // to the first store; [glulx op:dtonumz] and back, clamped.
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.NumToD, C(1), Ram(4), Ram(0))
            .Op(Opcode.NumToD, C(-2), Ram(12), Ram(8))
            .Op(Opcode.DToNumZ, C(0x40091EB8), C(0x51EB851F), Ram(16))
            .Op(Opcode.DToNumN, C(0x40091EB8), C(0x51EB851F), Ram(20))
            .Op(Opcode.DToNumN, C(0xC0040000), C(0), Ram(24))
            .Op(Opcode.DToNumZ, C(0x7FF00000), C(0), Ram(28))
            .Op(Opcode.DToNumZ, C(0xFFF80000), C(0), Ram(32))
            .Op(Opcode.DToNumZ, C(0x41E00000), C(0), Ram(36))
            .Return(C(0)));

        Assert.Equal(0x3FF00000u, machine.Ram(0));
        Assert.Equal(0u, machine.Ram(4));
        Assert.Equal(0xC0000000u, machine.Ram(8));
        Assert.Equal(0u, machine.Ram(12));
        Assert.Equal(3u, machine.Ram(16));
        Assert.Equal(3u, machine.Ram(20));
        Assert.Equal(0xFFFFFFFDu, machine.Ram(24));
        Assert.Equal(0x7FFFFFFFu, machine.Ram(28));
        Assert.Equal(0x80000000u, machine.Ram(32));
        Assert.Equal(0x7FFFFFFFu, machine.Ram(36));
    }

    [Fact]
    public void FloatsAndDoublesConvertToEachOther()
    {
        // [glulx op:ftod] A float widens exactly, [glulx op:dtof] and a
        // double narrows, an infinity staying one.
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.FToD, C(Pi), Ram(4), Ram(0))
            .Op(Opcode.DToF, Ram(0), Ram(4), Ram(8))
            .Op(Opcode.DToF, C(0x7FF00000), C(0), Ram(12))
            .Op(Opcode.DToF, C(0x40091EB8), C(0x51EB851F), Ram(16))
            .Return(C(0)));

        var (high, low) = D(GlulxFloat.Decode(Pi));
        Assert.Equal(high, machine.Ram(0));
        Assert.Equal(low, machine.Ram(4));
        Assert.Equal(Pi, machine.Ram(8));
        Assert.Equal(Inf, machine.Ram(12));
        Assert.Equal(F(3.14f), machine.Ram(16));
    }

    [Fact]
    public void DoubleArithmeticAndFunctions()
    {
        // [glulx #opcodes_double] The same rules in double precision.
        Assert.Equal(2.0, DBinary(Opcode.DAdd, 1, 1));
        Assert.Equal(-2.0, DBinary(Opcode.DSub, 1, 3));
        Assert.Equal(6.0, DBinary(Opcode.DMul, 2, 3));
        Assert.Equal(double.PositiveInfinity, DBinary(Opcode.DDiv, 1, 0));
        Assert.True(double.IsNaN(DBinary(Opcode.DDiv, 0, 0)));
        Assert.Equal(1.5, DBinary(Opcode.DModR, 5.5, 2));
        Assert.Equal(2.0, DBinary(Opcode.DModQ, 5.5, 2));
        Assert.Equal(-1.5, DBinary(Opcode.DModR, -5.5, 2));
        Assert.Equal(-2.0, DBinary(Opcode.DModQ, -5.5, 2));
        Assert.True(double.IsNegative(DBinary(Opcode.DModQ, 0, -2)));
        Assert.Equal(2.0, DUnary(Opcode.DSqrt, 4));
        Assert.Equal(1.0, DUnary(Opcode.DExp, 0));
        Assert.Equal(0.0, DUnary(Opcode.DLog, 1));
        Assert.Equal(1024.0, DBinary(Opcode.DPow, 2, 10));
        Assert.Equal(1.0, DBinary(Opcode.DPow, double.NaN, 0));
        Assert.Equal(1.0, DBinary(Opcode.DPow, -1, double.NegativeInfinity));
        Assert.Equal(0.0, DUnary(Opcode.DSin, 0));
        Assert.Equal(1.0, DUnary(Opcode.DCos, 0));
        Assert.Equal(0.0, DUnary(Opcode.DTan, 0));
        Assert.Equal(0.0, DUnary(Opcode.DASin, 0));
        AssertNear(Math.PI / 2, DUnary(Opcode.DACos, 0));
        AssertNear(Math.PI / 2, DUnary(Opcode.DATan, double.PositiveInfinity));
        AssertNear(Math.PI / 4, DBinary(Opcode.DATan2, double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(1.0, DUnary(Opcode.DCeil, 0.5));
        Assert.True(double.IsNegative(DUnary(Opcode.DCeil, -0.5)));
        Assert.Equal(-1.0, DUnary(Opcode.DFloor, -0.5));
        Assert.Equal(0.0, DUnary(Opcode.DFloor, 0.5));
    }

    [Fact]
    public void DoubleComparisonsTakePairs()
    {
        var (oneHigh, oneLow) = D(1);
        var (nearHigh, nearLow) = D(1.05);
        var (tenthHigh, tenthLow) = D(0.1);
        var (twoHigh, twoLow) = D(2);
        var (nanHigh, nanLow) = D(double.NaN);
        var (infHigh, infLow) = D(double.PositiveInfinity);

        // [glulx #opcodes_doublebranch] Each value is two operands.
        Assert.True(Branches(Opcode.JDIsNaN, nanHigh, nanLow));
        Assert.False(Branches(Opcode.JDIsNaN, infHigh, infLow));
        Assert.True(Branches(Opcode.JDIsInf, infHigh, infLow));
        Assert.False(Branches(Opcode.JDIsInf, oneHigh, oneLow));
        Assert.True(Branches(Opcode.JDeq, oneHigh, oneLow, nearHigh, nearLow, tenthHigh, tenthLow));
        Assert.False(Branches(Opcode.JDeq, oneHigh, oneLow, twoHigh, twoLow, tenthHigh, tenthLow));
        Assert.False(Branches(Opcode.JDeq, nanHigh, nanLow, oneHigh, oneLow, tenthHigh, tenthLow));
        Assert.True(Branches(Opcode.JDne, nanHigh, nanLow, oneHigh, oneLow, tenthHigh, tenthLow));
        Assert.True(Branches(Opcode.JDlt, oneHigh, oneLow, twoHigh, twoLow));
        Assert.False(Branches(Opcode.JDlt, twoHigh, twoLow, oneHigh, oneLow));
        Assert.True(Branches(Opcode.JDle, oneHigh, oneLow, oneHigh, oneLow));
        Assert.True(Branches(Opcode.JDgt, twoHigh, twoLow, oneHigh, oneLow));
        Assert.True(Branches(Opcode.JDge, infHigh, infLow, twoHigh, twoLow));
        Assert.False(Branches(Opcode.JDge, nanHigh, nanLow, oneHigh, oneLow));
    }

    [Fact]
    public void APairOnTheStackHasItsHighWordOnTop()
    {
        // [glulx #opcodes_double] dadd Xhi Xlo Yhi Ylo sp sp leaves the
        // result ordered for the next opcode, which reads high first.
        var (high, low) = D(1.5);
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.DAdd, C(high), C(low), C(high), C(low), Sp, Sp)
            .Op(Opcode.DMul, Sp, Sp, C(0x40000000), C(0), Ram(4), Ram(0))
            .Return(C(0)));

        Assert.Equal(6.0, GlulxFloat.DecodeDouble(machine.Ram(0), machine.Ram(4)));
    }

    [Fact]
    public void GestaltAnswersFloatAndDouble()
    {
        var machine = GlulxRun.Run(Program()
            .Op(Opcode.Gestalt, C(11), C(0), Ram(0))
            .Op(Opcode.Gestalt, C(13), C(0), Ram(4))
            .Return(C(0)));

        // [glulx #opcodes_misc] Float (11) and Double (13).
        Assert.Equal(1u, machine.Ram(0));
        Assert.Equal(1u, machine.Ram(4));
    }
}

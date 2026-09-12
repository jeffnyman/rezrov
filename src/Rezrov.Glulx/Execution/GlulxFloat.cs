namespace Rezrov.Glulx.Execution;

/// <summary>
/// [glulx #floats] Floating-point values as the machine holds them: a
/// float is one word in the IEEE 754 single-precision encoding, and
/// [glulx #doubles] a double is two, the high word first.
/// </summary>
/// <remarks>
/// The framework's float and double are those encodings, so a value
/// is decoded by reinterpreting its bits and encoded the same way, and
/// the arithmetic is the platform's IEEE 754 arithmetic, which gives
/// the infinities, zeroes, and NaNs the specification lists. What the
/// specification pins down beyond IEEE 754, the conversions that clamp,
/// the sign of a zero quotient, and the special cases of pow, is here.
/// [glulx #opcodes_float] Which NaN comes out of a failed operation is
/// not guaranteed, and it is whichever the platform gives.
/// </remarks>
public static class GlulxFloat
{
    /// <summary>The float a word encodes.</summary>
    public static float Decode(uint bits) => BitConverter.UInt32BitsToSingle(bits);

    /// <summary>The word a float is encoded as.</summary>
    public static uint Encode(float value) => BitConverter.SingleToUInt32Bits(value);

    /// <summary>The double a pair of words encodes.</summary>
    public static double DecodeDouble(uint high, uint low) => BitConverter.UInt64BitsToDouble(((ulong)high << 32) | low);

    /// <summary>The pair of words a double is encoded as.</summary>
    public static (uint High, uint Low) EncodeDouble(double value)
    {
        var bits = BitConverter.DoubleToUInt64Bits(value);
        return ((uint)(bits >> 32), (uint)bits);
    }

    /// <summary>
    /// [glulx op:jisnan] Whether a word is a NaN: the exponent all ones
    /// and the mantissa not zero.
    /// </summary>
    public static bool IsNaN(uint bits) => (bits & 0x7F800000) == 0x7F800000 && (bits & 0x007FFFFF) != 0;

    /// <summary>
    /// [glulx op:jisinf] Whether a word is an infinity of either sign.
    /// </summary>
    public static bool IsInfinity(uint bits) => (bits & 0x7FFFFFFF) == 0x7F800000;

    /// <summary>
    /// [glulx op:ftonumz] A float as an integer, rounded toward zero,
    /// or [glulx op:ftonumn] to the nearest integer with halves away
    /// from zero, as the reference interpreter rounds them.
    /// </summary>
    public static uint ToInteger(float value, bool nearest) =>
        Clamp(nearest ? MathF.Round(value, MidpointRounding.AwayFromZero) : MathF.Truncate(value), float.IsNegative(value));

    /// <summary>[glulx op:dtonumz] The same for a double.</summary>
    public static uint ToInteger(double value, bool nearest) =>
        Clamp(nearest ? Math.Round(value, MidpointRounding.AwayFromZero) : Math.Truncate(value), double.IsNegative(value));

    /// <summary>
    /// [glulx op:ceil] The value rounded up, keeping the sign of the
    /// argument when the result is a zero.
    /// </summary>
    public static float Ceiling(float value) => KeepZeroSign(MathF.Ceiling(value), value);

    /// <summary>[glulx op:dceil] The same for a double.</summary>
    public static double Ceiling(double value) => KeepZeroSign(Math.Ceiling(value), value);

    /// <summary>
    /// [glulx op:fmod] The remainder and the quotient of a division,
    /// the quotient rounded toward zero and the remainder with the sign
    /// of the dividend. A zero quotient has the sign the division would
    /// have had, which the subtraction loses.
    /// </summary>
    public static (float Remainder, float Quotient) Modulo(float dividend, float divisor)
    {
        var remainder = dividend % divisor;
        var quotient = (dividend - remainder) / divisor;
        if (quotient == 0)
        {
            quotient = MathF.CopySign(0, Decode((Encode(dividend) ^ Encode(divisor)) & 0x80000000));
        }

        return (remainder, quotient);
    }

    /// <summary>[glulx op:dmodr] The same for doubles.</summary>
    public static (double Remainder, double Quotient) Modulo(double dividend, double divisor)
    {
        var remainder = dividend % divisor;
        var quotient = (dividend - remainder) / divisor;
        if (quotient == 0)
        {
            var (high, _) = EncodeDouble(dividend);
            var (divisorHigh, _) = EncodeDouble(divisor);
            quotient = Math.CopySign(0, DecodeDouble((high ^ divisorHigh) & 0x80000000, 0));
        }

        return (remainder, quotient);
    }

    /// <summary>
    /// [glulx op:pow] A power, with the special cases the specification
    /// lists that a platform's pow might not honor: 1 to any power, any
    /// value to the zeroth power, and -1 to an infinite power are 1.
    /// </summary>
    public static float Pow(float value, float exponent)
    {
        if (value == 1 || exponent == 0 || (value == -1 && float.IsInfinity(exponent)))
        {
            return 1;
        }

        return MathF.Pow(value, exponent);
    }

    /// <summary>[glulx op:dpow] The same for doubles.</summary>
    public static double Pow(double value, double exponent)
    {
        if (value == 1 || exponent == 0 || (value == -1 && double.IsInfinity(exponent)))
        {
            return 1;
        }

        return Math.Pow(value, exponent);
    }

    /// <summary>
    /// [glulx op:jfeq] Whether two values are within a tolerance of
    /// each other, the tolerance's sign ignored: never when any of the
    /// three is NaN, always for an infinite tolerance unless the values
    /// are opposite infinities, and exactly for a zero tolerance, with
    /// +0 equal to -0 and infinities of one sign equal.
    /// </summary>
    public static bool NearlyEqual(float left, float right, float tolerance)
    {
        if (float.IsNaN(tolerance))
        {
            return false;
        }

        if (float.IsInfinity(left) && float.IsInfinity(right))
        {
            return left == right;
        }

        var difference = right - left;
        var bound = MathF.Abs(tolerance);
        return difference <= bound && difference >= -bound;
    }

    /// <summary>[glulx op:jdeq] The same for doubles.</summary>
    public static bool NearlyEqual(double left, double right, double tolerance)
    {
        if (double.IsNaN(tolerance))
        {
            return false;
        }

        if (double.IsInfinity(left) && double.IsInfinity(right))
        {
            return left == right;
        }

        var difference = right - left;
        var bound = Math.Abs(tolerance);
        return difference <= bound && difference >= -bound;
    }

    // [glulx op:ftonumz] Outside the 32-bit range, or NaN or infinite,
    // the result is the largest positive or negative integer by the
    // sign bit of the value.
    private static uint Clamp(double rounded, bool negative)
    {
        if (double.IsNaN(rounded) || rounded > int.MaxValue || rounded < int.MinValue)
        {
            return negative ? 0x80000000 : 0x7FFFFFFF;
        }

        return (uint)(int)rounded;
    }

    private static float KeepZeroSign(float result, float value) => result == 0 ? MathF.CopySign(0, value) : result;

    private static double KeepZeroSign(double result, double value) => result == 0 ? Math.CopySign(0, value) : result;
}

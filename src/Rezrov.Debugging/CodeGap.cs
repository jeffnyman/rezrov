namespace Rezrov.Debugging;

/// <summary>
/// A stretch of high memory no routine accounts for.
/// </summary>
/// <remarks>
/// [zm 1.1.1] High memory holds the routines and the static strings
/// together, so a gap in a listing is usually text rather than
/// anything missing. Saying how many strings exactly fill the gap is
/// the honest way to say so: either they tile it end to end, in which
/// case the bytes are explained, or they do not, in which case the gap
/// is reported as so many bytes and nothing is claimed about them.
/// </remarks>
/// <param name="Address">Where the gap begins.</param>
/// <param name="Length">How many bytes it covers.</param>
/// <param name="Strings">
/// The number of encoded strings that exactly fill the gap, or 0 where
/// the gap does not read as text.
/// </param>
public sealed record CodeGap(int Address, int Length, int Strings)
{
    /// <summary>The first byte after the gap.</summary>
    public int EndAddress => Address + Length;
}

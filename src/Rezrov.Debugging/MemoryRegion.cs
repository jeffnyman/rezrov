namespace Rezrov.Debugging;

/// <summary>
/// A stretch of a story file that is known to be something.
/// </summary>
/// <param name="Name">What the bytes are.</param>
/// <param name="Address">Where the region begins.</param>
/// <param name="Length">How many bytes it covers.</param>
public readonly record struct MemoryRegion(string Name, int Address, int Length)
{
    /// <summary>
    /// The name given to bytes nothing in the header accounts for.
    /// </summary>
    public const string Unaccounted = "unaccounted";

    /// <summary>The first byte after the region.</summary>
    public int EndAddress => Address + Length;
}

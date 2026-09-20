namespace Rezrov.AaMachine.Execution;

/// <summary>
/// [aam opcode] The random numbers a story gets.
/// </summary>
/// <remarks>
/// The specification says only that any timer-seeded generator will
/// do, which would be enough for play and useless for testing, since
/// the conformance transcripts were recorded from one particular
/// generator with the seed 1234. This is that generator: the linear
/// congruential one that C libraries carried for years, stepping the
/// state by 41c64e6d and taking the middle fifteen bits.
/// </remarks>
public sealed class AaRandom
{
    private uint _state;

    public AaRandom(int? seed) => Seed = seed;

    /// <summary>
    /// The seed a play was started with, or null to take one from the
    /// clock so that no two plays are the same.
    /// </summary>
    public int? Seed { get; }

    /// <summary>Starts the sequence again, as a restart does.</summary>
    public void Reset() =>
        _state = Seed is { } seed ? (uint)seed : (uint)Environment.TickCount64;

    /// <summary>A number from 0 to 32767.</summary>
    public int Next()
    {
        var high = (_state >> 16) & 0xffff;
        var low = _state & 0xffff;
        var carried = ((0x15a * low) + (0x4e35 * high)) & 0xffff;

        _state = (carried << 16) + (0x4e35 * low) + 1;

        return (int)((_state >> 16) & 0x7fff);
    }
}

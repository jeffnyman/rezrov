namespace Rezrov.Glulx.Execution;

/// <summary>
/// The random number generator behind the random opcode.
/// </summary>
/// <remarks>
/// [glulx op:setrandom] A nonzero seed starts a sequence that is always
/// the same for that seed, and a zero seed asks for numbers as
/// unpredictable as the interpreter can make them. The machine starts
/// in the unpredictable mode. The generator is a 32-bit xorshift over a
/// mixed seed, chosen for being short enough to read in one sitting;
/// the specification asks nothing of the sequence but determinism.
/// [glulx #saveformat] Its state is not part of a saved game.
/// </remarks>
public sealed class GlulxRandom
{
    private uint _state;

    /// <summary>
    /// A generator seeded with <paramref name="seed"/>, or from the
    /// clock when no seed is given.
    /// </summary>
    public GlulxRandom(uint? seed = null)
    {
        if (seed is { } given && given != 0)
        {
            Seed(given);
        }
        else
        {
            SeedRandomly();
        }
    }

    /// <summary>
    /// [glulx op:setrandom] Starts the deterministic sequence for a
    /// nonzero seed.
    /// </summary>
    public void Seed(uint seed)
    {
        // A xorshift generator must never hold zero, and nearby seeds
        // would otherwise give similar early values, so the seed is
        // scrambled first.
        var mixed = seed;
        mixed ^= mixed >> 16;
        mixed *= 0x85EBCA6B;
        mixed ^= mixed >> 13;
        mixed *= 0xC2B2AE35;
        mixed ^= mixed >> 16;
        _state = mixed == 0 ? 0x9E3779B9 : mixed;
    }

    /// <summary>
    /// [glulx op:setrandom] Leaves determinism behind: the sequence from
    /// here on depends on the clock.
    /// </summary>
    public void SeedRandomly() => Seed((uint)Random.Shared.NextInt64(1, uint.MaxValue));

    /// <summary>The next 32 bits of the sequence.</summary>
    public uint Next()
    {
        var x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }

    /// <summary>
    /// [glulx op:random] A number from 0 to <paramref name="range"/>
    /// less one for a positive range, from the range plus one to 0 for
    /// a negative one, and anything at all for zero.
    /// </summary>
    public uint InRange(uint range)
    {
        var signed = (int)range;
        return signed switch
        {
            0 => Next(),
            > 0 => Next() % range,
            _ => (uint)-(int)(Next() % (uint)-signed),
        };
    }
}

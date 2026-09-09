namespace Rezrov.ZMachine.Execution;

/// <summary>
/// The random number generator, with the two states the standard
/// requires.
/// </summary>
/// <remarks>
/// [zm 2.4] The generator is either "random" or "predictable". It starts
/// random, [zm 2.4.1] produces uniform values from 1 to n on request,
/// and [zm 2.4.2] becomes predictable when given a seed, after which the
/// same seed always yields the same sequence.
///
/// The predictable state follows the algorithm the standard's remarks
/// suggest: a seed below 1000 counts 1, 2, 3, up to the seed and round
/// again, which walks through every value in turn and is useful for
/// testing, while a larger seed feeds an ordinary seeded generator. The
/// remarks also warn off the C library's rand(), which made Balances
/// unwinnable on some ports; .NET's generator has no such trouble.
/// </remarks>
public sealed class RandomGenerator
{
    private Random _random = new();
    private int _sequenceLength;
    private int _sequenceNext;

    /// <summary>A generator in the random state.</summary>
    public RandomGenerator()
    {
    }

    /// <summary>
    /// A generator already seeded, for reproducible runs.
    /// </summary>
    public RandomGenerator(int seed)
    {
        Seed(seed);
    }

    /// <summary>
    /// Whether the generator is in the predictable state.
    /// </summary>
    public bool IsPredictable { get; private set; }

    /// <summary>
    /// [zm 2.4] Switches to the random state, as at the start of a game.
    /// </summary>
    public void SeedRandomly()
    {
        _random = new Random();
        _sequenceLength = 0;
        IsPredictable = false;
    }

    /// <summary>
    /// [zm 2.4.2] Switches to the predictable state with a seed.
    /// </summary>
    public void Seed(int seed)
    {
        IsPredictable = true;

        if (seed < 1000)
        {
            _sequenceLength = Math.Max(seed, 1);
            _sequenceNext = 1;
        }
        else
        {
            _sequenceLength = 0;
            _random = new Random(seed);
        }
    }

    /// <summary>
    /// [zm 2.4.1] A value from 1 to <paramref name="range"/> inclusive.
    /// </summary>
    public ushort Next(int range)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(range, 1);

        if (_sequenceLength > 0)
        {
            // The rising sequence, reduced to the range asked for so that
            // the result is still 1 to range.
            var value = _sequenceNext;
            _sequenceNext = (_sequenceNext % _sequenceLength) + 1;
            return (ushort)(((value - 1) % range) + 1);
        }

        return (ushort)_random.Next(1, range + 1);
    }
}

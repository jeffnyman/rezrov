using System.Security.Cryptography;

namespace Rezrov.ZMachine.Execution;

/// <summary>
/// The random number generator, with the two states the standard
/// requires, and a session seed for replaying whole games.
/// </summary>
/// <remarks>
/// [zm 2.4] The generator is either "random" or "predictable". It starts
/// random, [zm 2.4.1] produces uniform values from 1 to n on request,
/// and [zm 2.4.2] becomes predictable when the game gives it a seed,
/// after which the same seed always yields the same sequence.
///
/// The predictable state follows the algorithm the standard's remarks
/// suggest: a seed below 1000 counts 1, 2, 3, up to the seed and round
/// again, which walks through every value in turn and is useful for
/// testing a game, while a larger seed feeds the ordinary generator.
///
/// A session seed, the one the interpreter's <c>--seed</c> option and
/// an acceptance script give, is a different thing. It seeds the
/// ordinary generator without entering the predictable state, so the
/// game sees the same dice it always does, only the same dice every
/// session. That is what a recorded playthrough needs: a fight with
/// the troll that goes the way it went, not a fight where every roll
/// is 1, 2, 3.
///
/// The stream is a xorshift32 owned here rather than the framework's
/// generator, so that a seed produces the same session forever and a
/// recording is never invalidated by a runtime upgrade. Its quality
/// comfortably clears the bar the standard sets, whose remarks warn
/// only against the poorest of the old C generators. [zm 2.4 deviates]
/// In a seeded session, a game that asks to be reseeded "as randomly
/// as possible" is reseeded from the stream instead, so that the
/// session stays a function of the one seed it was given; a session
/// with no seed gets the entropy the standard has in mind.
/// </remarks>
public sealed class RandomGenerator
{
    // Seeds below this count the rising sequence; from here up they
    // seed the stream.
    private const int SequenceSeedLimit = 1000;

    // Spreads a seed across the state so that small seeds do not start
    // the stream in a correlated corner, and never yields the zero that
    // xorshift can never leave.
    private const uint MixIncrement = 0x9E3779B9;
    private const uint MixMultiplier1 = 0x85EBCA6B;
    private const uint MixMultiplier2 = 0xC2B2AE35;

    private readonly bool _sessionSeeded;
    private uint _state;
    private int _sequenceLength;
    private int _sequenceNext;

    /// <summary>A generator in the random state.</summary>
    public RandomGenerator()
    {
        _state = Entropy();
    }

    /// <summary>
    /// A generator with a session seed: in the random state as far as
    /// the game can tell, but rolling the same dice every time.
    /// </summary>
    public RandomGenerator(int seed)
    {
        _sessionSeeded = true;
        _state = Mixed(unchecked((uint)seed));
    }

    /// <summary>
    /// Whether the generator is in the predictable state.
    /// </summary>
    public bool IsPredictable { get; private set; }

    /// <summary>
    /// [zm 2.4] Switches to the random state, as at the start of a game,
    /// or in a seeded session to a fresh point on the seeded stream.
    /// </summary>
    public void SeedRandomly()
    {
        _sequenceLength = 0;
        IsPredictable = false;
        _state = _sessionSeeded ? Mixed(Advance()) : Entropy();
    }

    /// <summary>
    /// [zm 2.4.2] Switches to the predictable state with a seed.
    /// </summary>
    public void Seed(int seed)
    {
        IsPredictable = true;

        if (seed < SequenceSeedLimit)
        {
            _sequenceLength = Math.Max(seed, 1);
            _sequenceNext = 1;
        }
        else
        {
            _sequenceLength = 0;
            _state = Mixed(unchecked((uint)seed));
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

        // Folding by modulo skews the distribution by under one part in
        // 131072 for the largest legal range, far below anything a
        // game's dice could notice.
        return (ushort)((Advance() % (uint)range) + 1);
    }

    // One step of xorshift32, returning the new state.
    private uint Advance()
    {
        var state = _state;
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        _state = state;
        return state;
    }

    private static uint Mixed(uint value)
    {
        unchecked
        {
            value += MixIncrement;
            value ^= value >> 16;
            value *= MixMultiplier1;
            value ^= value >> 13;
            value *= MixMultiplier2;
            value ^= value >> 16;
        }

        return value == 0 ? MixIncrement : value;
    }

    private static uint Entropy()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        return Mixed(((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3]);
    }
}

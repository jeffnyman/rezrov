using Rezrov.ZMachine.Execution;

namespace Rezrov.Tests;

/// <summary>
/// The generator's two states, and the session seed that makes a whole
/// game repeatable.
/// </summary>
public class RandomGeneratorTests
{
    [Fact]
    public void ASessionSeedRollsTheSameDiceForever()
    {
        // These values are what the stream produced when it was first
        // written down, and they must never change: every recorded
        // playthrough depends on them.
        var twenty = new RandomGenerator(20);
        var big = new RandomGenerator(1234);

        Assert.Equal([14, 26, 89, 90, 90, 90], Enumerable.Range(0, 6).Select(_ => (int)twenty.Next(100)));
        Assert.Equal([4, 4, 5, 4, 2, 5], Enumerable.Range(0, 6).Select(_ => (int)big.Next(6)));
        Assert.False(twenty.IsPredictable);
    }

    [Fact]
    public void AGameSeedIsThePredictableStateWhateverTheSession()
    {
        // [zm 2.4.2] The game's own seeding is the standard's business:
        // a low seed counts, a high one starts a known stream.
        var counting = new RandomGenerator(20);
        counting.Seed(3);
        Assert.True(counting.IsPredictable);
        Assert.Equal([1, 2, 3, 1], Enumerable.Range(0, 4).Select(_ => (int)counting.Next(100)));

        var seeded = new RandomGenerator(20);
        seeded.Seed(5000);
        Assert.Equal([418, 867, 558], Enumerable.Range(0, 3).Select(_ => (int)seeded.Next(1000)));
    }

    [Fact]
    public void ReseedingRandomlyInASeededSessionStaysRepeatable()
    {
        // [zm 2.4 deviates] A game that asks for fresh randomness in a
        // seeded session gets the next point on the seeded stream, so
        // two sessions with the seed still agree.
        var first = new RandomGenerator(20);
        first.Next(100);
        first.SeedRandomly();

        Assert.False(first.IsPredictable);
        Assert.Equal([36, 90, 80], Enumerable.Range(0, 3).Select(_ => (int)first.Next(100)));
    }

    [Fact]
    public void AnUnseededSessionIsNotRepeatable()
    {
        var a = new RandomGenerator();
        var b = new RandomGenerator();

        // 32 rolls of 1 to 1000 agreeing by chance is beyond unlikely.
        var same = Enumerable.Range(0, 32).All(_ => a.Next(1000) == b.Next(1000));
        Assert.False(same);
    }
}

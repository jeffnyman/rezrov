namespace Rezrov.Tests;

/// <summary>
/// A clock for tests that moves only when told to, so that a timer test
/// never depends on how fast the machine running it happens to be.
/// </summary>
internal sealed class ManualClock : TimeProvider
{
    private long _now;

    /// <summary>One timestamp tick is a millisecond.</summary>
    public override long TimestampFrequency => 1000;

    public override long GetTimestamp() => _now;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(int milliseconds) => _now += milliseconds;
}

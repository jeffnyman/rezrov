namespace Rezrov.Tests;

/// <summary>
/// A clock for tests that moves only when told to, so that a timer test
/// never depends on how fast the machine running it happens to be, and
/// that tells whatever time and time zone a test sets.
/// </summary>
internal sealed class ManualClock : TimeProvider
{
    private long _now;

    /// <summary>One timestamp tick is a millisecond.</summary>
    public override long TimestampFrequency => 1000;

    /// <summary>The moment the clock says it is.</summary>
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;

    /// <summary>The local time zone, universal time unless set.</summary>
    public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;

    public override TimeZoneInfo LocalTimeZone => Zone;

    public override long GetTimestamp() => _now;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(int milliseconds) => _now += milliseconds;
}

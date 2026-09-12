using Rezrov.Glulx.Glk;
using Rezrov.Glulx.Instructions;
using static Rezrov.Tests.GlulxAssembler;

namespace Rezrov.Tests;

/// <summary>
/// [glk #datetime] The system clock: the current time as a timestamp
/// and a simple time, conversions to dates in universal and local time
/// and back, normalization, and the edges a time library has.
/// </summary>
public class GlkDateTimeTests
{
    private const uint CurrentTime = 0x0160;
    private const uint TimeToDateUtc = 0x0168;
    private const uint DateToTimeUtc = 0x016C;

    // A zone two hours ahead, and one with summer time from the second
    // Sunday of March to the first Sunday of November, an hour ahead of
    // its standard five hours behind.
    private static readonly TimeZoneInfo Ahead = TimeZoneInfo.CreateCustomTimeZone("ahead", TimeSpan.FromHours(2), "Ahead", "Ahead");

    private static readonly TimeZoneInfo Summer = TimeZoneInfo.CreateCustomTimeZone(
        "summer",
        TimeSpan.FromHours(-5),
        "Summer",
        "Summer Standard",
        "Summer Daylight",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date,
                DateTime.MaxValue.Date,
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday)),
        ]);

    private static (GlkLibrary Glk, ManualClock Clock) Library(TimeZoneInfo? zone = null)
    {
        var clock = new ManualClock { Zone = zone ?? TimeZoneInfo.Utc };
        return (new GlkLibrary(new RecordingGlkDisplay(), clock: clock), clock);
    }

    [Fact]
    public void TheCurrentTimeIsUnixSecondsAndMicroseconds()
    {
        var (glk, clock) = Library();
        var moment = new DateTimeOffset(2026, 9, 12, 10, 20, 30, TimeSpan.Zero).AddTicks(1234567);
        clock.UtcNow = moment;

        // [glk op:current_time] Seconds since 1970 and the microseconds
        // beyond them, the clock's tenths of a microsecond dropped.
        Assert.Equal((moment.ToUnixTimeSeconds(), 123456), glk.CurrentTime());

        // [glk #datetime] Before 1970 the seconds are negative, rounded
        // down, and the microseconds are still a positive fraction.
        clock.UtcNow = DateTimeOffset.UnixEpoch.AddMilliseconds(-500);
        Assert.Equal((-1L, 500000), glk.CurrentTime());
    }

    [Fact]
    public void ASimpleTimeIsDividedRoundingTowardNegativeInfinity()
    {
        var (glk, clock) = Library();
        clock.UtcNow = DateTimeOffset.UnixEpoch.AddSeconds(125);

        // [glk op:current_simple_time] 125 seconds is two minutes; half
        // a second before 1970 is minute -1; a zero factor is refused.
        Assert.Equal(2, glk.CurrentSimpleTime(60));
        Assert.Equal(125, glk.CurrentSimpleTime(1));
        clock.UtcNow = DateTimeOffset.UnixEpoch.AddMilliseconds(-500);
        Assert.Equal(-1, glk.CurrentSimpleTime(60));
        clock.UtcNow = DateTimeOffset.UnixEpoch.AddSeconds(-120);
        Assert.Equal(-2, glk.CurrentSimpleTime(60));
        Assert.Equal(0, glk.CurrentSimpleTime(0));
        Assert.Single(glk.Warnings);
    }

    [Fact]
    public void ATimestampBecomesADateInUniversalOrLocalTime()
    {
        var (glk, _) = Library(Ahead);
        var moment = new DateTimeOffset(2026, 9, 12, 23, 20, 30, TimeSpan.Zero);
        var seconds = moment.ToUnixTimeSeconds();

        // [glk op:time_to_date_utc] The fields, the weekday from 0 for
        // Sunday, and the microseconds carried over; [glk
        // op:time_to_date_local] two hours ahead it is the next day.
        Assert.Equal(new GlkDate(2026, 9, 12, 6, 23, 20, 30, 42), glk.TimeToDate(seconds, 42, false));
        Assert.Equal(new GlkDate(2026, 9, 13, 0, 1, 20, 30, 42), glk.TimeToDate(seconds, 42, true));

        // Before 1970, and the last moment a date can hold.
        Assert.Equal(new GlkDate(1969, 12, 31, 3, 23, 59, 59, 0), glk.TimeToDate(-1, 0, false));
        Assert.Equal(new GlkDate(9999, 12, 31, 5, 23, 59, 59, 0), glk.TimeToDate(253402300799, 0, false));
        Assert.Equal(GlkDate.Unrepresentable, glk.TimeToDate(253402300800, 0, false));
        Assert.Equal(GlkDate.Unrepresentable, glk.TimeToDate(253402300799, 0, true));
        Assert.Equal(GlkDate.Unrepresentable, glk.TimeToDate(long.MinValue, 0, false));
    }

    [Fact]
    public void ASimpleTimeBecomesADateWithoutMicroseconds()
    {
        var (glk, _) = Library(Ahead);

        // [glk op:simple_time_to_date_utc] The time times the factor.
        Assert.Equal(new GlkDate(1970, 1, 1, 4, 2, 0, 0, 0), glk.SimpleTimeToDate(2, 3600, false));
        Assert.Equal(new GlkDate(1970, 1, 1, 4, 4, 0, 0, 0), glk.SimpleTimeToDate(2, 3600, true));
        Assert.Equal(new GlkDate(1969, 12, 31, 3, 23, 59, 0, 0), glk.SimpleTimeToDate(-1, 60, false));
    }

    [Fact]
    public void ADateBecomesATimestampWithItsFieldsNormalized()
    {
        var (glk, _) = Library(Ahead);
        var date = new GlkDate(2026, 9, 12, 99, 23, 20, 30, 42);
        var seconds = new DateTimeOffset(2026, 9, 12, 23, 20, 30, TimeSpan.Zero).ToUnixTimeSeconds();

        // [glk op:date_to_time_utc] Back again, the weekday ignored;
        // [glk op:date_to_time_local] two hours earlier in the zone.
        Assert.Equal((seconds, 42), glk.DateToTime(date, false));
        Assert.Equal((seconds - 7200, 42), glk.DateToTime(date, true));

        // [glk op:date_to_time_utc] Out-of-range fields normalize: the
        // thirteenth month, the zeroth day, the 25th hour, a negative
        // second, and a microsecond count over a second or under zero.
        Assert.Equal(glk.DateToTime(new GlkDate(2027, 1, 1, 0, 0, 0, 0, 0), false), glk.DateToTime(new GlkDate(2026, 13, 1, 0, 0, 0, 0, 0), false));
        Assert.Equal(glk.DateToTime(new GlkDate(2026, 2, 28, 0, 0, 0, 0, 0), false), glk.DateToTime(new GlkDate(2026, 3, 0, 0, 0, 0, 0, 0), false));
        Assert.Equal(glk.DateToTime(new GlkDate(2026, 3, 2, 0, 1, 0, 0, 0), false), glk.DateToTime(new GlkDate(2026, 3, 1, 0, 25, 0, 0, 0), false));
        Assert.Equal(glk.DateToTime(new GlkDate(2026, 2, 28, 0, 23, 59, 59, 0), false), glk.DateToTime(new GlkDate(2026, 3, 1, 0, 0, 0, -1, 0), false));
        Assert.Equal(glk.DateToTime(new GlkDate(2026, 3, 1, 0, 0, 0, 1, 500000), false), glk.DateToTime(new GlkDate(2026, 3, 1, 0, 0, 0, 0, 1500000), false));
        Assert.Equal(glk.DateToTime(new GlkDate(2026, 2, 28, 0, 23, 59, 59, 999999), false), glk.DateToTime(new GlkDate(2026, 3, 1, 0, 0, 0, 0, -1), false));

        // [glk #datetime] A year no time library holds gives -1.
        Assert.Equal((-1L, 0), glk.DateToTime(new GlkDate(0, 1, 1, 0, 0, 0, 0, 0), false));
        Assert.Equal((-1L, 0), glk.DateToTime(new GlkDate(9999, 12, 31, 0, 23, 59, 60, 0), false));
    }

    [Fact]
    public void ADateBecomesASimpleTime()
    {
        var (glk, _) = Library(Ahead);
        var date = new GlkDate(1970, 1, 1, 0, 2, 0, 30, 0);

        // [glk op:date_to_simple_time_utc] Divided by the factor, and
        // rounded down when the local reading falls before 1970.
        Assert.Equal(2, glk.DateToSimpleTime(date, 3600, false));
        Assert.Equal(0, glk.DateToSimpleTime(date, 3600, true));
        Assert.Equal(-2, glk.DateToSimpleTime(new GlkDate(1970, 1, 1, 0, 0, 30, 0, 0), 3600, true));
        Assert.Equal(-1, glk.DateToSimpleTime(new GlkDate(0, 1, 1, 0, 0, 0, 0, 0), 1, false));
        Assert.Equal(0, glk.DateToSimpleTime(date, 0, false));
        Assert.Single(glk.Warnings);
    }

    [Fact]
    public void LocalTimeFollowsSummerTime()
    {
        var (glk, _) = Library(Summer);
        var winter = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var summer = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        // [glk op:time_to_date_local] Five hours behind in January, four
        // in July, and [glk op:date_to_time_local] the same the other
        // way round.
        Assert.Equal(7, glk.TimeToDate(winter, 0, true).Hour);
        Assert.Equal(8, glk.TimeToDate(summer, 0, true).Hour);
        Assert.Equal((winter, 0), glk.DateToTime(new GlkDate(2026, 1, 15, 0, 7, 0, 0, 0), true));
        Assert.Equal((summer, 0), glk.DateToTime(new GlkDate(2026, 7, 15, 0, 8, 0, 0, 0), true));
    }

    [Fact]
    public void GestaltPromisesTheClock()
    {
        Assert.Equal(1u, GlkLibrary.Gestalt((uint)GestaltSelector.DateTime, 0, null));
    }

    [Fact]
    public void AGameReadsTheClockThroughTheOpcode()
    {
        const uint Time = GlulxRun.RamStart + 0x100;
        const uint Date = GlulxRun.RamStart + 0x110;
        const uint Back = GlulxRun.RamStart + 0x130;
        var code = new GlulxAssembler().Function("main").Op(Opcode.SetIOSys, C(2), C(0));
        Call(code, CurrentTime, Discard, C(Time));
        Call(code, TimeToDateUtc, Discard, C(Time), C(Date));
        Call(code, DateToTimeUtc, Discard, C(Date), C(Back));
        code.Return(C(0));

        var clock = new ManualClock { UtcNow = new DateTimeOffset(2026, 9, 12, 10, 20, 30, TimeSpan.Zero).AddTicks(1234560) };
        var machine = GlulxRun.Run(code, glk: new GlkLibrary(new RecordingGlkDisplay(), clock: clock));

        // [glk op:current_time] The three words of a glktimeval_t, then
        // [glk op:time_to_date_utc] the eight of a glkdate_t, then the
        // same timestamp again from the date.
        var seconds = (uint)clock.UtcNow.ToUnixTimeSeconds();
        Assert.Equal(0u, machine.Memory.ReadWord(Time));
        Assert.Equal(seconds, machine.Memory.ReadWord(Time + 4));
        Assert.Equal(123456u, machine.Memory.ReadWord(Time + 8));
        Assert.Equal(2026u, machine.Memory.ReadWord(Date));
        Assert.Equal(9u, machine.Memory.ReadWord(Date + 4));
        Assert.Equal(12u, machine.Memory.ReadWord(Date + 8));
        Assert.Equal(6u, machine.Memory.ReadWord(Date + 12));
        Assert.Equal(10u, machine.Memory.ReadWord(Date + 16));
        Assert.Equal(123456u, machine.Memory.ReadWord(Date + 28));
        Assert.Equal(seconds, machine.Memory.ReadWord(Back + 4));
        Assert.Equal(123456u, machine.Memory.ReadWord(Back + 8));
    }

    /// <summary>
    /// [glulx op:glk] Pushes the arguments last first and calls the
    /// function, storing its result.
    /// </summary>
    private static GlulxAssembler Call(GlulxAssembler code, uint selector, Arg result, params Arg[] args)
    {
        for (var i = args.Length - 1; i >= 0; i--)
        {
            code.Op(Opcode.Copy, args[i], Sp);
        }

        return code.Op(Opcode.Glk, C(selector), C(args.Length), result);
    }
}

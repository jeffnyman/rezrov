namespace Rezrov.Glulx.Glk;

/// <summary>
/// [glk #datetime] A broken-out date and time, as glkdate_t has it:
/// the year in full, the month from 1, the day from 1, the weekday
/// from 0 for Sunday, and the time of day down to microseconds.
/// </summary>
public readonly record struct GlkDate(int Year, int Month, int Day, int Weekday, int Hour, int Minute, int Second, int Microsecond)
{
    /// <summary>
    /// [glk #datetime] The date a time library cannot represent: every
    /// field -1.
    /// </summary>
    public static GlkDate Unrepresentable => new(-1, -1, -1, -1, -1, -1, -1, -1);

    /// <summary>
    /// The date at a moment, with the weekday and the microseconds
    /// that moment has.
    /// </summary>
    public static GlkDate Of(DateTimeOffset moment) => new(
        moment.Year,
        moment.Month,
        moment.Day,
        (int)moment.DayOfWeek,
        moment.Hour,
        moment.Minute,
        moment.Second,
        (int)(moment.Ticks % TimeSpan.TicksPerSecond / 10));

    /// <summary>
    /// The eight fields of a glkdate_t a game passed in.
    /// </summary>
    public static GlkDate FromFields(GlkCall call, int index)
    {
        ArgumentNullException.ThrowIfNull(call);
        return new GlkDate(
            (int)call.In(index, 0),
            (int)call.In(index, 1),
            (int)call.In(index, 2),
            (int)call.In(index, 3),
            (int)call.In(index, 4),
            (int)call.In(index, 5),
            (int)call.In(index, 6),
            (int)call.In(index, 7));
    }

    /// <summary>The eight fields of a glkdate_t, in order.</summary>
    public uint[] ToFields() =>
        [(uint)Year, (uint)Month, (uint)Day, (uint)Weekday, (uint)Hour, (uint)Minute, (uint)Second, (uint)Microsecond];
}

/// <summary>
/// [glk #datetime] The system clock: the current time as a Unix
/// timestamp, and conversions between timestamps and dates in
/// universal or local time.
/// </summary>
/// <remarks>
/// The clock and the local time zone are the <see cref="TimeProvider"/>
/// the library was given, so a test can set the moment and the zone.
/// A timestamp is a signed 64-bit count of seconds since the start of
/// 1970 in universal time, with microseconds beside it; a simple time
/// is that count divided by a factor, rounded toward negative
/// infinity, as the specification says.
/// </remarks>
public sealed partial class GlkLibrary
{
    private const long MicrosecondsPerSecond = 1_000_000;
    private const long TicksPerMicrosecond = TimeSpan.TicksPerSecond / MicrosecondsPerSecond;

    /// <summary>
    /// [glk op:current_time] The current Unix time in seconds and the
    /// microseconds beyond it.
    /// </summary>
    public (long Seconds, int Microseconds) CurrentTime() => Split(_clock.GetUtcNow());

    /// <summary>
    /// [glk op:current_simple_time] The current Unix time divided by
    /// <paramref name="factor"/>, which must not be zero.
    /// </summary>
    public int CurrentSimpleTime(uint factor)
    {
        if (factor == 0)
        {
            Warn("current_simple_time: the factor cannot be zero.");
            return 0;
        }

        return Simplify(Split(_clock.GetUtcNow()).Seconds, factor);
    }

    /// <summary>
    /// [glk op:time_to_date_utc] The date of a timestamp in universal
    /// time, or [glk op:time_to_date_local] in local time, with the
    /// microseconds carried over, or every field -1 for a moment
    /// outside the years a date can hold.
    /// </summary>
    public GlkDate TimeToDate(long seconds, int microseconds, bool local)
    {
        if (!TryMoment(seconds, microseconds, local, out var moment))
        {
            return GlkDate.Unrepresentable;
        }

        return GlkDate.Of(moment);
    }

    /// <summary>
    /// [glk op:simple_time_to_date_utc] The date of a simple time,
    /// which is the timestamp divided by <paramref name="factor"/>,
    /// with no microseconds.
    /// </summary>
    public GlkDate SimpleTimeToDate(int time, uint factor, bool local) => TimeToDate((long)time * factor, 0, local);

    /// <summary>
    /// [glk op:date_to_time_utc] The timestamp of a date, read as
    /// universal or [glk op:date_to_time_local] local time, with every
    /// field normalized: a thirteenth month is January of the next
    /// year, a zeroth day the last of the month before, and so on. The
    /// weekday is ignored. A date outside the years a time can hold
    /// gives -1 for the seconds.
    /// </summary>
    public (long Seconds, int Microseconds) DateToTime(GlkDate date, bool local)
    {
        if (!TryTimestamp(date, local, out var seconds, out var microseconds))
        {
            return (-1, 0);
        }

        return (seconds, microseconds);
    }

    /// <summary>
    /// [glk op:date_to_simple_time_utc] The timestamp of a date divided
    /// by <paramref name="factor"/>, which must not be zero, or -1 for
    /// a date outside the years a time can hold.
    /// </summary>
    public int DateToSimpleTime(GlkDate date, uint factor, bool local)
    {
        if (factor == 0)
        {
            Warn("date_to_simple_time: the factor cannot be zero.");
            return 0;
        }

        if (!TryTimestamp(date, local, out var seconds, out _))
        {
            return -1;
        }

        return Simplify(seconds, factor);
    }

    // [glk op:current_simple_time] Rounded toward negative infinity.
    private static int Simplify(long seconds, uint factor)
    {
        var quotient = seconds / factor;
        if (seconds < 0 && seconds % factor != 0)
        {
            quotient--;
        }

        return (int)quotient;
    }

    // A moment as a Unix timestamp and the microseconds beyond it,
    // which for a moment before 1970 means the seconds rounded down
    // and a positive count of microseconds after them.
    private static (long Seconds, int Microseconds) Split(DateTimeOffset moment)
    {
        var ticks = moment.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        var seconds = ticks / TimeSpan.TicksPerSecond;
        var rest = ticks % TimeSpan.TicksPerSecond;
        if (rest < 0)
        {
            seconds--;
            rest += TimeSpan.TicksPerSecond;
        }

        return (seconds, (int)(rest / TicksPerMicrosecond));
    }

    // The moment a timestamp names, in universal or local time, if it
    // is within the years a date can hold.
    private bool TryMoment(long seconds, int microseconds, bool local, out DateTimeOffset moment)
    {
        // The years 1 to 9999 are what a date can hold, and the seconds
        // are checked first so that the ticks cannot overflow.
        moment = default;
        if (seconds is < -62135596800 or > 253402300799)
        {
            return false;
        }

        var utcTicks = DateTimeOffset.UnixEpoch.UtcTicks + (seconds * TimeSpan.TicksPerSecond) + (microseconds * TicksPerMicrosecond);
        if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        moment = new DateTimeOffset(utcTicks, TimeSpan.Zero);
        if (!local)
        {
            return true;
        }

        // The zone's offset may carry the moment past either end of
        // time, which is not a date either; the framework would clamp
        // it there, so the wall clock is checked first.
        var offset = _clock.LocalTimeZone.GetUtcOffset(moment);
        var wallTicks = utcTicks + offset.Ticks;
        if (wallTicks < DateTime.MinValue.Ticks || wallTicks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        moment = new DateTimeOffset(wallTicks, offset);
        return true;
    }

    // [glk op:date_to_time_utc] The fields need not be in their normal
    // ranges: each is added to the start of the year in turn, months
    // first, so that any value of any field normalizes as a calendar
    // would have it.
    private bool TryTimestamp(GlkDate date, bool local, out long seconds, out int microseconds)
    {
        seconds = 0;
        microseconds = 0;

        if (date.Year is < 1 or > 9999)
        {
            return false;
        }

        try
        {
            var wall = new DateTime(date.Year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
                .AddMonths(date.Month - 1)
                .AddDays(date.Day - 1)
                .AddHours(date.Hour)
                .AddMinutes(date.Minute)
                .AddSeconds(date.Second)
                .AddTicks(date.Microsecond * TicksPerMicrosecond);

            // [glk op:date_to_time_local] Local time is read in the
            // zone's offset for that wall clock time, which is as much
            // divining of summer time as there is to do.
            var offset = local ? _clock.LocalTimeZone.GetUtcOffset(wall) : TimeSpan.Zero;
            (seconds, microseconds) = Split(new DateTimeOffset(wall, offset));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}

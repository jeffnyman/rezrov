namespace Rezrov.ZMachine.Input;

/// <summary>
/// The timer a game can attach to a read or read_char: an interval and
/// what to do when it elapses.
/// </summary>
/// <remarks>
/// [zm op:read] In Version 4 and later, when the time and routine
/// operands are supplied and non-zero, the routine is called every
/// time/10 seconds while the player is being waited on. If it returns
/// true the reading stops at once. The interpreter builds one of these
/// with the routine call inside it, so that an input source only has to
/// keep time and ask.
/// </remarks>
public sealed class InputTimer
{
    private readonly Func<bool> _interrupt;

    public InputTimer(int tenthsOfSeconds, Func<bool> interrupt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tenthsOfSeconds, 1);
        ArgumentNullException.ThrowIfNull(interrupt);

        TenthsOfSeconds = tenthsOfSeconds;
        _interrupt = interrupt;
    }

    /// <summary>
    /// The interval as the game gave it, in tenths of a second.
    /// </summary>
    public int TenthsOfSeconds { get; }

    /// <summary>The interval as a span of time.</summary>
    public TimeSpan Interval => TimeSpan.FromMilliseconds(TenthsOfSeconds * 100);

    /// <summary>
    /// Runs the game's interrupt routine, and returns true if input is
    /// to stop, with whatever was typed so far handed back under
    /// terminator 0.
    /// </summary>
    public bool Interrupt() => _interrupt();
}

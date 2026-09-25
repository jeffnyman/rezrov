namespace Rezrov.ZMachine.Execution;

/// <summary>
/// Why a run of the interpreter came back.
/// </summary>
/// <remarks>
/// A game being played only ever ends one way, which is why
/// <see cref="Interpreter.Run"/> says nothing. Something driving the
/// game rather than playing it needs to know the difference between
/// having arrived somewhere and having run out of road.
/// </remarks>
public enum StopReason
{
    /// <summary>[zm op:quit] The game ended.</summary>
    Quit,

    /// <summary>
    /// The next instruction is one the caller asked to stop at.
    /// </summary>
    Breakpoint,

    /// <summary>
    /// The call chain came back to the depth the caller asked for,
    /// which is what running to a return looks like from outside.
    /// </summary>
    Unwound,

    /// <summary>
    /// The instruction count ran out with the game still going.
    /// </summary>
    Limit,
}

namespace Rezrov.ZMachine.Input;

/// <summary>
/// What the read opcode asks of an input source.
/// </summary>
/// <param name="MaxLength">
/// [zm op:read] The most characters the source may accept, counting
/// the ones in <paramref name="Initial"/>. The interpreter cuts anything
/// longer, but a source that knows the limit can refuse keystrokes as a
/// real keyboard would.
/// </param>
/// <param name="Initial">
/// [zm op:read] Characters left over from an interrupted earlier read,
/// which the source continues from rather than starting afresh. The game
/// has already redisplayed them, so the source should not.
/// </param>
/// <param name="Terminators">
/// [zm 10.5.2] Which keys end the command: newline, and in Version 5 and
/// later whatever function keys the story's table names.
/// </param>
/// <param name="Timer">
/// [zm op:read] The timer to run while waiting, or null for none.
/// </param>
public sealed record LineInputRequest(
    int MaxLength,
    IReadOnlyList<ushort> Initial,
    TerminatingCharacters Terminators,
    InputTimer? Timer);

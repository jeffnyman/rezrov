namespace Rezrov.ZMachine.Input;

/// <summary>
/// A command as an input source hands it back.
/// </summary>
/// <param name="Text">
/// The characters typed, as ZSCII, without the terminator. Case is as
/// typed; [zm op:read] lowering it is the interpreter's job.
/// </param>
/// <param name="Terminator">
/// [zm op:read] The key that ended the command: 13 for a carriage return
/// however the keyboard produced it, a function key from the terminating
/// characters table, or 0 when the timer's interrupt stopped the read.
/// </param>
public sealed record LineInput(IReadOnlyList<ushort> Text, ushort Terminator);

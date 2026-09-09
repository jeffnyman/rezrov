namespace Rezrov.ZMachine.Saves;

/// <summary>
/// [zm 6.1] The state of play, captured: dynamic memory, the stack, the
/// program counter, and the routine call state with every routine's
/// locals.
/// </summary>
/// <remarks>
/// The editor's note on [zm 6.1] says why there are four things and not
/// one: only dynamic memory is inside the memory map, and the other
/// three live in the interpreter, so saving is not dumping memory. This
/// is what <see cref="Quetzal"/> writes to a file and what the undo
/// cache keeps in memory, and it is a plain copy so that a restored
/// state cannot be disturbed by the game that goes on running.
/// </remarks>
/// <param name="DynamicMemory">
/// The whole of dynamic memory, header included, as it was.
/// </param>
/// <param name="ProgramCounter">
/// Where execution resumes. For a saved game this is the store or
/// branch data of the save instruction, as [quetzal 5.8] has it.
/// </param>
/// <param name="Frames">The call chain, oldest first.</param>
/// <param name="Stack">The whole value stack, oldest word first.</param>
public sealed record SavedState(
    byte[] DynamicMemory,
    int ProgramCounter,
    IReadOnlyList<SavedFrame> Frames,
    ushort[] Stack);

/// <summary>
/// One routine of the saved call chain.
/// </summary>
/// <param name="ReturnAddress">Where the routine returns to.</param>
/// <param name="StoreVariable">
/// The variable its result goes into, or null when the result is thrown
/// away, which [quetzal 4.6] marks with the p flag.
/// </param>
/// <param name="Locals">Its local variables.</param>
/// <param name="ArgumentCount">
/// [quetzal 4.7] How many arguments were supplied.
/// </param>
/// <param name="StackBase">
/// How many words of the stack belong to older frames.
/// </param>
public sealed record SavedFrame(
    int ReturnAddress,
    byte? StoreVariable,
    ushort[] Locals,
    int ArgumentCount,
    int StackBase);

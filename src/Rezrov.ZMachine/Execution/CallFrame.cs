namespace Rezrov.ZMachine.Execution;

/// <summary>
/// One routine on the call stack: where to go back to, what to do with
/// the result, its locals, and how much of the value stack is its own.
/// </summary>
/// <remarks>
/// [zm 6.1] The routine call state, the chain of routines that have
/// called each other and their locals, lives in the interpreter's own
/// memory rather than in the Z-machine's, so a game can neither read
/// nor corrupt it. This is one link of that chain.
/// </remarks>
public sealed class CallFrame
{
    public CallFrame(
        int returnAddress,
        byte? storeVariable,
        ushort[] locals,
        int argumentCount,
        int stackBase,
        int? routineAddress = null)
    {
        ArgumentNullException.ThrowIfNull(locals);

        ReturnAddress = returnAddress;
        StoreVariable = storeVariable;
        Locals = locals;
        ArgumentCount = argumentCount;
        StackBase = stackBase;
        RoutineAddress = routineAddress;
    }

    /// <summary>
    /// Where execution resumes when this routine returns.
    /// </summary>
    public int ReturnAddress { get; }

    /// <summary>
    /// The variable the routine's result goes into, or null when the
    /// result is thrown away, as [zm 6.4.1] the call_vn opcodes do.
    /// </summary>
    public byte? StoreVariable { get; }

    /// <summary>
    /// [zm 6.4.4] The routine's local variables, initialized from the
    /// header or to zero and then overwritten by the arguments.
    /// </summary>
    public ushort[] Locals { get; }

    /// <summary>
    /// [zm 6.4.4.1] How many arguments the caller supplied, which the
    /// locals alone cannot reveal and check_arg_count needs.
    /// </summary>
    public int ArgumentCount { get; }

    /// <summary>
    /// The depth of the value stack when the routine was entered. The
    /// editor's note on [zm 6.3.1] says it plainly: record the base on
    /// entry and refuse to pop below it.
    /// </summary>
    public int StackBase { get; }

    /// <summary>
    /// Which routine this frame is running, or null where nothing here
    /// knows.
    /// </summary>
    /// <remarks>
    /// The machine has no use for this, which is why it took until a
    /// debugger wanted a call stack for anyone to record it. It is
    /// null in the two cases where it is genuinely not known rather
    /// than merely inconvenient to find. [zm 5.5] Outside Version 6 a
    /// game begins at an address rather than inside a routine, so the
    /// outermost frame is running no routine at all. And [quetzal 4]
    /// records a frame as its return address, its locals, and its
    /// stack, so a frame rebuilt from a saved game has nothing to say
    /// here.
    ///
    /// Both are recoverable from outside: the routine a frame is in
    /// contains the return address of the frame above it, and the
    /// innermost contains the program counter. That is a job for
    /// whatever holds a disassembly, which this does not.
    /// </remarks>
    public int? RoutineAddress { get; }
}

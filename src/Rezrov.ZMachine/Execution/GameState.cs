using Rezrov.ZMachine.Instructions;

namespace Rezrov.ZMachine.Execution;

/// <summary>
/// The state of play: memory, the stack, the program counter, and the
/// chain of routine calls with their local variables.
/// </summary>
/// <remarks>
/// [zm 6.1] Those four things are the whole state, and the editor's note
/// explains why three of them are kept out of the memory map: dynamic
/// memory is the only part a story file can address, so the stack, the
/// program counter, and the call chain are beyond its reach by design.
///
/// This class holds all four and implements the rules for changing them:
/// [zm 6.2] globals, [zm 6.3] the stack, [zm 6.4] routine calls and
/// returns, and [zm 6.5] stack frames for catch and throw. It knows
/// nothing about opcodes; the interpreter loop applies these operations
/// as the instructions it decodes require.
/// </remarks>
public sealed class GameState
{
    /// <summary>
    /// [zm 6.3.3] The standard's minimum is 1024 words, but it advises
    /// more since modern games need it, and cites 32768 in Windows Frotz
    /// and 61440 in nfrotz. This is comfortably above both.
    /// </summary>
    public const int MaxStackWords = 65536;

    /// <summary>[zm 6.2] The globals table holds 240 words.</summary>
    public const int GlobalCount = 240;

    private readonly byte[] _originalDynamicMemory;
    private readonly List<ushort> _stack = [];
    private readonly List<CallFrame> _frames = [];

    public GameState(ZMemory memory, StoryHeader header)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(header);

        Memory = memory;
        Header = header;

        // [zm 6.1.3] A restart restores dynamic memory from the original
        // story file, so a copy is kept from before anything ran.
        _originalDynamicMemory = memory.Slice(0, header.StaticMemoryBase).ToArray();

        Start();
    }

    public ZMemory Memory { get; }

    public StoryHeader Header { get; }

    /// <summary>
    /// The address of the next instruction to execute.
    /// </summary>
    public int ProgramCounter { get; set; }

    /// <summary>The routine currently running.</summary>
    public CallFrame CurrentFrame => _frames[^1];

    /// <summary>
    /// [zm 6.5] The current stack frame as a Z-machine number: how many
    /// routines deep the call chain is, with the first at 1. This is what
    /// catch returns and throw takes.
    /// </summary>
    public int FrameNumber => _frames.Count;

    /// <summary>
    /// How many values the current routine has on the stack. [zm 6.3.1]
    /// A routine sees only what it pushed itself, not the caller's values
    /// beneath.
    /// </summary>
    public int StackDepth => _stack.Count - CurrentFrame.StackBase;

    /// <summary>
    /// Reads a variable by number, which for the stack means popping.
    /// </summary>
    /// <remarks>
    /// [zm 4.2.2] $00 is the stack, $01 to $0F the locals, $10 to $FF the
    /// globals. [zm 6.3] Reading the stack pointer pulls a value off.
    /// </remarks>
    public ushort ReadVariable(int number) => number switch
    {
        0 => Pop(),
        < 16 => ReadLocal(number),
        _ => ReadGlobal(number),
    };

    /// <summary>
    /// Writes a variable by number, which for the stack means pushing.
    /// </summary>
    /// <remarks>
    /// [zm 6.3] Writing to the stack pointer pushes a value.
    /// </remarks>
    public void WriteVariable(int number, ushort value)
    {
        switch (number)
        {
            case 0:
                Push(value);
                break;
            case < 16:
                WriteLocal(number, value);
                break;
            default:
                WriteGlobal(number, value);
                break;
        }
    }

    /// <summary>
    /// Reads a variable by reference, which for the stack means looking at
    /// the top without removing it.
    /// </summary>
    /// <remarks>
    /// [zm 6.3.4] In the seven opcodes that take a variable by reference
    /// (inc, dec, inc_chk, dec_chk, load, store, pull) a reference to the
    /// stack pointer reads or writes the top item in place. The editor's
    /// note calls this the subtlest rule in the instruction set, because
    /// treating it as a pop and a push gets the depth right and only the
    /// value underneath wrong.
    /// </remarks>
    public ushort ReadVariableInPlace(int number) => number == 0 ? Peek() : ReadVariable(number);

    /// <summary>
    /// Writes a variable by reference, which for the stack means replacing
    /// the top item. See <see cref="ReadVariableInPlace"/>.
    /// </summary>
    public void WriteVariableInPlace(int number, ushort value)
    {
        if (number == 0)
        {
            RequireStackValue();
            _stack[^1] = value;
        }
        else
        {
            WriteVariable(number, value);
        }
    }

    /// <summary>[zm 6.3] Pushes a word onto the stack.</summary>
    public void Push(ushort value)
    {
        if (_stack.Count >= MaxStackWords)
        {
            throw new InvalidOperationException($"The stack has overflowed its {MaxStackWords} words.");
        }

        _stack.Add(value);
    }

    /// <summary>[zm 6.3] Pulls the top word off the stack.</summary>
    /// <exception cref="InvalidOperationException">
    /// [zm 6.3.1] The current routine has nothing on the stack. The pull
    /// opcode says the interpreter should halt on underflow.
    /// </exception>
    public ushort Pop()
    {
        RequireStackValue();

        var value = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        return value;
    }

    /// <summary>The top word of the stack, left where it is.</summary>
    public ushort Peek()
    {
        RequireStackValue();

        return _stack[^1];
    }

    /// <summary>
    /// Pulls the top word off the stack if the current routine has one,
    /// for a caller that would rather report an underflow than throw.
    /// </summary>
    public bool TryPop(out ushort value)
    {
        if (_stack.Count <= CurrentFrame.StackBase)
        {
            value = 0;
            return false;
        }

        value = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        return true;
    }

    /// <summary>
    /// Reads the top word of the stack without removing it, if the
    /// current routine has one.
    /// </summary>
    public bool TryPeek(out ushort value)
    {
        if (_stack.Count <= CurrentFrame.StackBase)
        {
            value = 0;
            return false;
        }

        value = _stack[^1];
        return true;
    }

    /// <summary>
    /// Reads a global variable, numbered $10 to $FF as variables are.
    /// </summary>
    /// <remarks>
    /// [zm 6.2] Globals live in a table of 240 words in dynamic memory,
    /// at the address in header word $0C, so a game may read and write
    /// them directly as memory too.
    /// </remarks>
    public ushort ReadGlobal(int number) => Memory.ReadWord(GlobalAddress(number));

    public void WriteGlobal(int number, ushort value) => Memory.WriteWord(GlobalAddress(number), value);

    /// <summary>
    /// Calls a routine: creates its frame and locals, passes the
    /// arguments, and moves the program counter to its first instruction.
    /// </summary>
    /// <param name="packedAddress">The routine's packed address.</param>
    /// <param name="arguments">The arguments, first to last.</param>
    /// <param name="storeVariable">
    /// Where the result goes on return, or null to discard it.
    /// </param>
    /// <param name="returnAddress">
    /// Where execution resumes on return.
    /// </param>
    /// <remarks>
    /// [zm 6.4.3] A call to packed address 0 is legal and does nothing
    /// except return false. The editor's note stresses this is a feature:
    /// Inform stores 0 for "no routine" and calls it without checking.
    ///
    /// [zm 6.4.4] Locals are created with the header's initial values in
    /// Versions 1 to 4 and zero afterward, and then the arguments are
    /// written over them, so an argument always wins and a default only
    /// survives where none was passed. [zm 6.4.4.1] Extra arguments are
    /// thrown away and missing ones leave the defaults alone.
    /// </remarks>
    public void CallRoutine(ushort packedAddress, ReadOnlySpan<ushort> arguments, byte? storeVariable, int returnAddress)
    {
        if (packedAddress == 0)
        {
            if (storeVariable is { } variable)
            {
                WriteVariable(variable, 0);
            }

            ProgramCounter = returnAddress;
            return;
        }

        if (_frames.Count >= MaxStackWords / 4)
        {
            throw new InvalidOperationException("The call stack has overflowed.");
        }

        var routine = RoutineHeader.Read(Memory, Header.Version, Header.UnpackRoutineAddress(packedAddress));

        var locals = routine.InitialLocals.ToArray();
        var supplied = Math.Min(arguments.Length, locals.Length);
        for (var i = 0; i < supplied; i++)
        {
            locals[i] = arguments[i];
        }

        // [zm 6.3.1] The stack is empty as far as the new routine is
        // concerned, which the frame records as its base.
        _frames.Add(new CallFrame(returnAddress, storeVariable, locals, arguments.Length, _stack.Count));
        ProgramCounter = routine.CodeAddress;
    }

    /// <summary>
    /// Returns from the current routine with a value.
    /// </summary>
    /// <remarks>
    /// [zm 6.3.2] Anything the routine pushed is thrown away. [zm 6.4.2]
    /// The caller's locals and stack are as they were, except that the
    /// result is stored where the call asked, which may be the caller's
    /// stack. [zm 6.4.5] Any number is a valid result; false is 0 and true
    /// is 1.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// [zm 5.4] and [zm 5.5] The routine is the one the game started in,
    /// and returning from it is illegal.
    /// </exception>
    public void Return(ushort value)
    {
        if (_frames.Count == 1)
        {
            throw new InvalidOperationException("The game's starting routine cannot return.");
        }

        var frame = _frames[^1];
        _frames.RemoveAt(_frames.Count - 1);
        _stack.RemoveRange(frame.StackBase, _stack.Count - frame.StackBase);
        ProgramCounter = frame.ReturnAddress;

        // Stored after the frame is gone, so that a store to the stack
        // pushes onto the caller's stack rather than the discarded one.
        if (frame.StoreVariable is { } variable)
        {
            WriteVariable(variable, value);
        }
    }

    /// <summary>
    /// Unwinds the call chain to a frame from <see cref="FrameNumber"/>
    /// and returns from it with a value.
    /// </summary>
    /// <remarks>
    /// [zm 6.5] The interpreter must be able to set the routine call state
    /// to one further down the chain, throwing away its recent history.
    /// [zm op:throw] Afterward it returns as if from the routine that
    /// caught the frame value.
    /// </remarks>
    public void ThrowTo(int frameNumber, ushort value)
    {
        if (frameNumber < 1 || frameNumber > _frames.Count)
        {
            throw new InvalidOperationException(
                $"There is no stack frame {frameNumber} to throw to; the chain is {_frames.Count} deep.");
        }

        while (_frames.Count > frameNumber)
        {
            var discarded = _frames[^1];
            _frames.RemoveAt(_frames.Count - 1);
            _stack.RemoveRange(discarded.StackBase, _stack.Count - discarded.StackBase);
        }

        Return(value);
    }

    /// <summary>
    /// Whether the caller supplied argument <paramref name="number"/>,
    /// counting from 1.
    /// </summary>
    /// <remarks>
    /// [zm op:check_arg_count] This is how a routine tells routine(1) from
    /// routine(1, 0), which its locals cannot.
    /// </remarks>
    public bool ArgumentWasSupplied(int number) => number >= 1 && number <= CurrentFrame.ArgumentCount;

    /// <summary>
    /// Reads a byte of memory on the game's behalf.
    /// </summary>
    public byte ReadByte(int address) => Memory.ReadByte(address);

    public ushort ReadWord(int address) => Memory.ReadWord(address);

    /// <summary>
    /// Writes a byte of memory on the game's behalf, which is only allowed
    /// in dynamic memory.
    /// </summary>
    /// <remarks>
    /// [zm 1.1.1] Dynamic memory can be written; [zm 1.1.2] it is illegal
    /// for a game to write to static memory. The boundary is the static
    /// memory base from the header.
    /// </remarks>
    public void WriteByte(int address, byte value)
    {
        RequireDynamic(address);
        Memory.WriteByte(address, value);
    }

    public void WriteWord(int address, ushort value)
    {
        RequireDynamic(address);
        RequireDynamic(address + 1);
        Memory.WriteWord(address, value);
    }

    /// <summary>
    /// Restarts the game: dynamic memory back to the story file, the
    /// stack emptied, execution back at the start.
    /// </summary>
    /// <remarks>
    /// [zm 6.1.3] Flags 2 is preserved. The editor's note on [zm 6.1.2]
    /// explains why: its bits describe the player's session, such as
    /// whether a transcript is on, rather than the story's state. The
    /// header fields marked Rst in [zm 11.1] are the screen model's to
    /// reset, since they describe the interpreter rather than the game.
    /// </remarks>
    public void Restart()
    {
        var flags2 = Memory.ReadWord(0x10);

        for (var address = 0; address < _originalDynamicMemory.Length; address++)
        {
            Memory.WriteByte(address, _originalDynamicMemory[address]);
        }

        Memory.WriteWord(0x10, flags2);
        Start();
    }

    // [zm 5.4] In Version 6 the game begins by calling the main routine.
    // [zm 5.5] Everywhere else it begins at the initial program counter,
    // in an environment with no locals. Either way, that first frame can
    // never be returned from.
    private void Start()
    {
        _stack.Clear();
        _frames.Clear();

        if (Header.Version == ZMachineVersion.V6)
        {
            _frames.Add(new CallFrame(0, null, [], 0, 0));
            CallRoutine(Header.MainRoutinePackedAddress, [], null, 0);
            _frames.RemoveAt(0);
        }
        else
        {
            _frames.Add(new CallFrame(0, null, [], 0, 0));
            ProgramCounter = Header.InitialProgramCounter;
        }
    }

    private ushort ReadLocal(int number)
    {
        RequireLocal(number);

        return CurrentFrame.Locals[number - 1];
    }

    private void WriteLocal(int number, ushort value)
    {
        RequireLocal(number);

        CurrentFrame.Locals[number - 1] = value;
    }

    // [zm 4.2.2] It is illegal to refer to a local the routine does not
    // have, and there may be none at all.
    private void RequireLocal(int number)
    {
        if (number > CurrentFrame.Locals.Length)
        {
            throw new InvalidOperationException(
                $"Local variable {number} does not exist; the current routine has {CurrentFrame.Locals.Length}.");
        }
    }

    // [zm 6.2] Word n - 16 of the table, for variable n from $10 to $FF.
    private int GlobalAddress(int number)
    {
        if (number is < 16 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, "Global variables are numbered 16 to 255.");
        }

        return Header.GlobalVariablesAddress + ((number - 16) * 2);
    }

    private void RequireStackValue()
    {
        if (_stack.Count <= CurrentFrame.StackBase)
        {
            throw new InvalidOperationException("The stack is empty for the current routine.");
        }
    }

    private void RequireDynamic(int address)
    {
        if (address >= Header.StaticMemoryBase)
        {
            throw new InvalidOperationException(
                $"Address {address:X4} is in static memory, which a game may not write; dynamic memory ends at {Header.StaticMemoryBase:X4}.");
        }
    }
}

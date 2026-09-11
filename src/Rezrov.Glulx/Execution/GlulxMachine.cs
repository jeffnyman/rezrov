using System.Globalization;
using Rezrov.Core;
using Rezrov.Glulx.Instructions;
using Rezrov.Glulx.Text;

namespace Rezrov.Glulx.Execution;

/// <summary>
/// The Glulx machine: main memory, the stack, and the registers, run
/// one instruction at a time.
/// </summary>
/// <remarks>
/// [glulx #the-machine] The registers are the program counter, the
/// stack pointer, and the frame pointer; the last two live in the
/// <see cref="Stack"/>. Execution begins by calling the start function
/// named in the header and ends when that function returns or the game
/// quits.
///
/// Each step decodes the instruction at the program counter, advances
/// the counter past it, evaluates the load operands left to right, does
/// the opcode's work, and stores the results. That order is the one
/// the specification fixes wherever an operand uses the stack: loads
/// pop before the work is done and stores push after it.
///
/// Opcodes belonging to parts of the machine not built yet, output and
/// Glk among them, throw <see cref="NotSupportedException"/> naming the
/// opcode, so a game stops at the first thing it needs that is missing
/// rather than running on wrongly.
/// </remarks>
public sealed class GlulxMachine
{
    // Instructions in ROM cannot change, so once decoded they are kept.
    // Code in RAM is decoded afresh each time, since the game may have
    // written over it.
    private readonly Dictionary<uint, Instruction> _romInstructions = [];

    // The values of an instruction's load operands, in operand order,
    // reused from step to step.
    private readonly uint[] _values = new uint[8];

    // The decoding table in use, as a tree, and the memory version it
    // was built at, so that a table in RAM is read again if memory has
    // changed under it.
    private DecodingTable? _table;
    private uint _tableVersion;

    public GlulxMachine(GlulxMemory memory, GlulxRandom? random = null)
    {
        ArgumentNullException.ThrowIfNull(memory);

        Memory = memory;
        Stack = new GlulxStackSpace(memory.Header.StackSize);
        Decoder = new InstructionDecoder(memory);
        Random = random ?? new GlulxRandom();
        Start();
    }

    public GlulxMemory Memory { get; }

    public GlulxStackSpace Stack { get; }

    public InstructionDecoder Decoder { get; }

    public GlulxRandom Random { get; }

    /// <summary>
    /// [glulx #the-machine] The address of the next instruction.
    /// </summary>
    public uint ProgramCounter { get; private set; }

    /// <summary>
    /// [glulx op:quit] Whether the game has ended, by quitting or by
    /// returning from the start function.
    /// </summary>
    public bool HasQuit { get; private set; }

    public long InstructionsExecuted { get; private set; }

    /// <summary>
    /// [glulx op:protect] The start of the range restart leaves alone.
    /// </summary>
    public uint ProtectedStart { get; private set; }

    /// <summary>
    /// [glulx op:protect] The length of that range, zero for none.
    /// </summary>
    public uint ProtectedLength { get; private set; }

    /// <summary>
    /// [glulx op:setiosys] The I/O system output goes through.
    /// </summary>
    public IOSystem IOSystem { get; private set; }

    /// <summary>
    /// [glulx op:setiosys] The system's rock: for the filter system, the
    /// address of the function called with each character.
    /// </summary>
    public uint IORock { get; private set; }

    /// <summary>
    /// [glulx op:setstringtbl] The address of the string decoding table
    /// in use, or zero for none.
    /// </summary>
    public uint StringTable { get; private set; }

    /// <summary>
    /// [glulx #opcodes_misc] The TerpVersion gestalt: this program's
    /// version packed as the specification packs its own.
    /// </summary>
    public static uint InterpreterVersion { get; } = PackVersion(ProgramVersion.Current);

    /// <summary>Runs until the game quits.</summary>
    public void Run()
    {
        while (!HasQuit)
        {
            Step();
        }
    }

    /// <summary>Executes one instruction.</summary>
    public void Step()
    {
        if (HasQuit)
        {
            return;
        }

        var instruction = Decode(ProgramCounter);
        ProgramCounter = instruction.NextAddress;
        InstructionsExecuted++;
        Execute(instruction);
    }

    /// <summary>
    /// [glulx op:restart] Puts the machine back in its initial state:
    /// memory from the game file at its initial size, an empty stack,
    /// and the start function called again.
    /// </summary>
    /// <remarks>
    /// [glulx op:protect] The protected range is silently unaffected,
    /// which is done by copying it out and back, and the range itself
    /// survives the restart. [glulx #saveformat] The random number
    /// generator is not part of the state and is left as it is.
    /// </remarks>
    public void Restart()
    {
        var kept = ProtectedBytes();
        Memory.Reset();
        RestoreProtectedBytes(kept);
        Start();
    }

    private static uint PackVersion(string version)
    {
        var parts = version.Split('.');
        if (parts.Length != 3
            || !uint.TryParse(parts[0], out var major)
            || !uint.TryParse(parts[1], out var minor)
            || !uint.TryParse(parts[2], out var patch))
        {
            return 0;
        }

        return (major << 16) | ((minor & 0xFF) << 8) | (patch & 0xFF);
    }

    private static uint Truncate(uint value, int size) => size switch
    {
        1 => value & 0xFF,
        2 => value & 0xFFFF,
        _ => value,
    };

    private void Start()
    {
        HasQuit = false;
        Stack.Clear();

        // [glulx op:setiosys] The null system to begin with, and
        // [glulx op:setstringtbl] the header's table, at the start and
        // at a restart alike, as the reference interpreter has it.
        IOSystem = IOSystem.Null;
        IORock = 0;
        StringTable = Memory.Header.DecodingTable;

        // [glulx #the-header] Execution commences by calling the start
        // function. No call stub goes under it: when its frame is gone
        // the stack is empty, and that is how the end is recognized.
        Enter(Memory.Header.StartFunction, []);
    }

    private Instruction Decode(uint address)
    {
        if (address >= Memory.RamStart)
        {
            return Decoder.Decode(address);
        }

        if (!_romInstructions.TryGetValue(address, out var instruction))
        {
            instruction = Decoder.Decode(address);
            _romInstructions[address] = instruction;
        }

        return instruction;
    }

    private void Execute(Instruction instruction)
    {
        var ops = instruction.Operands;
        var info = instruction.Info;
        var size = info.OperandSize;
        var a = _values;
        var next = instruction.NextAddress;

        // [glulx #instruction] Operands are evaluated from left to right,
        // which matters when more than one of them pops the stack.
        for (var i = 0; i < ops.Count; i++)
        {
            if (!ops[i].IsStore)
            {
                a[i] = Load(ops[i], size);
            }
        }

        switch (instruction.Opcode)
        {
            case Opcode.Nop:
                break;

            // [glulx #integer-math] Arithmetic is 32-bit and truncates.
            case Opcode.Add:
                Store(ops[2], a[0] + a[1]);
                break;
            case Opcode.Sub:
                Store(ops[2], a[0] - a[1]);
                break;
            case Opcode.Mul:
                Store(ops[2], a[0] * a[1]);
                break;
            case Opcode.Div:
                CheckDivision(a[0], a[1]);
                Store(ops[2], (uint)((int)a[0] / (int)a[1]));
                break;
            case Opcode.Mod:
                CheckDivision(a[0], a[1]);
                Store(ops[2], (uint)((int)a[0] % (int)a[1]));
                break;
            case Opcode.Neg:
                Store(ops[1], (uint)-(int)a[0]);
                break;
            case Opcode.BitAnd:
                Store(ops[2], a[0] & a[1]);
                break;
            case Opcode.BitOr:
                Store(ops[2], a[0] | a[1]);
                break;
            case Opcode.BitXor:
                Store(ops[2], a[0] ^ a[1]);
                break;
            case Opcode.BitNot:
                Store(ops[1], ~a[0]);
                break;

            // [glulx op:shiftl] A shift of 32 or more places, counted as
            // unsigned, gives zero, or all ones for a signed right shift
            // of a negative value.
            case Opcode.ShiftL:
                Store(ops[2], a[1] >= 32 ? 0 : a[0] << (int)a[1]);
                break;
            case Opcode.UShiftR:
                Store(ops[2], a[1] >= 32 ? 0 : a[0] >> (int)a[1]);
                break;
            case Opcode.SShiftR:
                Store(ops[2], a[1] >= 32 ? ((int)a[0] < 0 ? 0xFFFFFFFF : 0) : (uint)((int)a[0] >> (int)a[1]));
                break;

            // [glulx #opcodes_branch] Signed and unsigned comparisons.
            case Opcode.Jump:
                Branch(a[0], next);
                break;
            case Opcode.Jz:
                BranchIf(a[0] == 0, a[1], next);
                break;
            case Opcode.Jnz:
                BranchIf(a[0] != 0, a[1], next);
                break;
            case Opcode.Jeq:
                BranchIf(a[0] == a[1], a[2], next);
                break;
            case Opcode.Jne:
                BranchIf(a[0] != a[1], a[2], next);
                break;
            case Opcode.Jlt:
                BranchIf((int)a[0] < (int)a[1], a[2], next);
                break;
            case Opcode.Jge:
                BranchIf((int)a[0] >= (int)a[1], a[2], next);
                break;
            case Opcode.Jgt:
                BranchIf((int)a[0] > (int)a[1], a[2], next);
                break;
            case Opcode.Jle:
                BranchIf((int)a[0] <= (int)a[1], a[2], next);
                break;
            case Opcode.Jltu:
                BranchIf(a[0] < a[1], a[2], next);
                break;
            case Opcode.Jgeu:
                BranchIf(a[0] >= a[1], a[2], next);
                break;
            case Opcode.Jgtu:
                BranchIf(a[0] > a[1], a[2], next);
                break;
            case Opcode.Jleu:
                BranchIf(a[0] <= a[1], a[2], next);
                break;
            case Opcode.JumpAbs:
                // [glulx op:jumpabs] An absolute address, no special cases.
                ProgramCounter = a[0];
                break;

            // [glulx #functions] Calls take their arguments from the stack
            // or from the operands, and store the result on return.
            case Opcode.Call:
                Call(a[0], PopArguments(a[1]), ops[2], next);
                break;
            case Opcode.CallF:
                Call(a[0], [], ops[1], next);
                break;
            case Opcode.CallFI:
                Call(a[0], [a[1]], ops[2], next);
                break;
            case Opcode.CallFII:
                Call(a[0], [a[1], a[2]], ops[3], next);
                break;
            case Opcode.CallFIII:
                Call(a[0], [a[1], a[2], a[3]], ops[4], next);
                break;
            case Opcode.Return:
                Return(a[0]);
                break;
            case Opcode.TailCall:
            {
                // [glulx op:tailcall] The arguments are taken, the frame
                // is destroyed as by a return, and the stub below it is
                // left for the new function to return through.
                var arguments = PopArguments(a[1]);
                Stack.DiscardFrame();
                Enter(a[0], arguments);
                break;
            }

            // [glulx #continuations] A catch pushes a stub and hands out
            // the stack pointer as the token; a throw cuts back to it.
            case Opcode.Catch:
            {
                var (type, address) = Destination(ops[0]);
                Stack.PushCallStub(type, address, next);
                Store(ops[0], Stack.StackPointer);
                Branch(a[1], next);
                break;
            }
            case Opcode.Throw:
                Stack.CutBackTo(a[1]);
                Resume(Stack.PopCallStub(), a[0]);
                break;

            // [glulx #moving-data] copys and copyb move 16-bit and 8-bit
            // fields, which the operand size has already applied.
            case Opcode.Copy:
            case Opcode.CopyS:
            case Opcode.CopyB:
                Store(ops[1], a[0], size);
                break;
            case Opcode.SexS:
                Store(ops[1], (uint)(short)a[0]);
                break;
            case Opcode.SexB:
                Store(ops[1], (uint)(sbyte)a[0]);
                break;

            // [glulx #array-data] The index is signed and the element
            // size scales it; loads widen without sign extension.
            case Opcode.ALoad:
                Store(ops[2], Memory.ReadWord(a[0] + (4 * a[1])));
                break;
            case Opcode.ALoadS:
                Store(ops[2], Memory.ReadShort(a[0] + (2 * a[1])));
                break;
            case Opcode.ALoadB:
                Store(ops[2], Memory.ReadByte(a[0] + a[1]));
                break;
            case Opcode.AStore:
                Memory.WriteWord(a[0] + (4 * a[1]), a[2]);
                break;
            case Opcode.AStoreS:
                Memory.WriteShort(a[0] + (2 * a[1]), (ushort)a[2]);
                break;
            case Opcode.AStoreB:
                Memory.WriteByte(a[0] + a[1], (byte)a[2]);
                break;
            case Opcode.ALoadBit:
            {
                var (address, bit) = BitAddress(a[0], a[1]);
                Store(ops[2], (uint)((Memory.ReadByte(address) >> bit) & 1));
                break;
            }
            case Opcode.AStoreBit:
            {
                var (address, bit) = BitAddress(a[0], a[1]);
                var mask = (byte)(1 << bit);
                var current = Memory.ReadByte(address);
                Memory.WriteByte(address, a[2] == 0 ? (byte)(current & ~mask) : (byte)(current | mask));
                break;
            }

            // [glulx #the-stack-1] The stack opcodes reach only the values
            // above the current frame, and count after their own
            // operands have been popped.
            case Opcode.StkCount:
                Store(ops[0], Stack.Count);
                break;
            case Opcode.StkPeek:
                Store(ops[1], Stack.Peek(a[0]));
                break;
            case Opcode.StkSwap:
                Stack.Swap();
                break;
            case Opcode.StkRoll:
                Stack.Roll(a[0], (int)a[1]);
                break;
            case Opcode.StkCopy:
                Stack.Copy(a[0]);
                break;

            // [glulx #opcodes_output] Output goes through the current I/O
            // system: nowhere for null, and to the game's own function
            // one character at a time for filter.
            case Opcode.GetIOSys:
                Store(ops[0], (uint)IOSystem);
                Store(ops[1], IORock);
                break;
            case Opcode.SetIOSys:
                SetIOSystem(a[0], a[1]);
                break;
            case Opcode.StreamChar:
                StreamCharacter(a[0] & 0xFF, next);
                break;
            case Opcode.StreamUniChar:
                StreamCharacter(a[0], next);
                break;
            case Opcode.StreamNum:
                StreamNumber((int)a[0], false, 0);
                break;
            case Opcode.StreamStr:
                StreamString(a[0], 0, 0);
                break;
            case Opcode.GetStringTbl:
                Store(ops[0], StringTable);
                break;
            case Opcode.SetStringTbl:
                StringTable = a[0];
                break;

            case Opcode.Gestalt:
                Store(ops[2], Gestalt(a[0], a[1]));
                break;
            case Opcode.DebugTrap:
                // [glulx op:debugtrap] With nothing else in mind, halt
                // with a visible message.
                throw new GlulxException($"debugtrap {a[0]:X8}: the game asked to stop here.");

            // [glulx #opcodes_memory] The size of memory is the game's to
            // change, within the rules the memory enforces.
            case Opcode.GetMemSize:
                Store(ops[0], Memory.Length);
                break;
            case Opcode.SetMemSize:
                Memory.Resize(a[0]);
                Store(ops[1], 0);
                break;

            case Opcode.Random:
                Store(ops[1], Random.InRange(a[0]));
                break;
            case Opcode.SetRandom:
                if (a[0] == 0)
                {
                    Random.SeedRandomly();
                }
                else
                {
                    Random.Seed(a[0]);
                }

                break;

            // [glulx #game-state]
            case Opcode.Quit:
                HasQuit = true;
                break;
            case Opcode.Restart:
                Restart();
                break;
            case Opcode.Protect:
                ProtectedStart = a[0];
                ProtectedLength = a[1];
                break;
            case Opcode.Verify:
                Store(ops[0], Memory.VerifyChecksum() ? 0u : 1u);
                break;

            // [glulx #searching] The options are the last load operand.
            case Opcode.LinearSearch:
                Store(ops[7], MemorySearch.Linear(Memory, a[0], a[1], a[2], a[3], a[4], a[5], (SearchOptions)a[6]));
                break;
            case Opcode.BinarySearch:
                Store(ops[7], MemorySearch.Binary(Memory, a[0], a[1], a[2], a[3], a[4], a[5], (SearchOptions)a[6]));
                break;
            case Opcode.LinkedSearch:
                Store(ops[6], MemorySearch.Linked(Memory, a[0], a[1], a[2], a[3], a[4], (SearchOptions)a[5]));
                break;

            // [glulx #opcodes_copy]
            case Opcode.MZero:
                Memory.Zero(a[1], a[0]);
                break;
            case Opcode.MCopy:
                Memory.Copy(a[1], a[2], a[0]);
                break;

            default:
                throw new NotSupportedException($"The {info.Name} opcode is not built yet.");
        }
    }

    private uint Load(Operand operand, int size)
    {
        var value = operand.Kind switch
        {
            OperandKind.Constant => operand.Value,
            OperandKind.Memory => ReadMemory(operand.Value, size),
            OperandKind.Ram => ReadMemory(Memory.RamStart + operand.Value, size),
            OperandKind.Local => Stack.ReadLocal(operand.Value, size),
            OperandKind.Stack => Stack.Pop(),
            _ => throw new GlulxException($"Cannot load from a {operand.Kind} operand."),
        };

        // [glulx op:copyb] A constant or a popped value is truncated to
        // the field size as well.
        return Truncate(value, size);
    }

    private void Store(Operand operand, uint value, int size = 4)
    {
        value = Truncate(value, size);

        switch (operand.Kind)
        {
            case OperandKind.Discard:
                break;
            case OperandKind.Memory:
                WriteMemory(operand.Value, value, size);
                break;
            case OperandKind.Ram:
                WriteMemory(Memory.RamStart + operand.Value, value, size);
                break;
            case OperandKind.Local:
                Stack.WriteLocal(operand.Value, value, size);
                break;
            case OperandKind.Stack:
                Stack.Push(value);
                break;
            default:
                throw new GlulxException($"Cannot store to a {operand.Kind} operand.");
        }
    }

    private uint ReadMemory(uint address, int size) => size switch
    {
        1 => Memory.ReadByte(address),
        2 => Memory.ReadShort(address),
        _ => Memory.ReadWord(address),
    };

    private void WriteMemory(uint address, uint value, int size)
    {
        switch (size)
        {
            case 1:
                Memory.WriteByte(address, (byte)value);
                break;
            case 2:
                Memory.WriteShort(address, (ushort)value);
                break;
            default:
                Memory.WriteWord(address, value);
                break;
        }
    }

    // [glulx #callstub] What a store operand becomes when the store
    // will happen later, on return from a call or after a throw.
    private (DestinationType Type, uint Address) Destination(Operand operand) => operand.Kind switch
    {
        OperandKind.Discard => (DestinationType.None, 0),
        OperandKind.Memory => (DestinationType.Memory, operand.Value),
        OperandKind.Ram => (DestinationType.Memory, Memory.RamStart + operand.Value),
        OperandKind.Local => (DestinationType.Local, operand.Value),
        OperandKind.Stack => (DestinationType.Stack, 0),
        _ => throw new GlulxException($"Cannot store to a {operand.Kind} operand."),
    };

    private static void CheckDivision(uint dividend, uint divisor)
    {
        // [glulx op:div] Division by zero is an error, and so is the one
        // quotient that does not fit.
        if (divisor == 0)
        {
            throw new GlulxException("Division by zero.");
        }

        if (dividend == 0x80000000 && divisor == 0xFFFFFFFF)
        {
            throw new GlulxException("Dividing -80000000 by -1 overflows.");
        }
    }

    // [glulx op:astorebit] Bit number L2 of address L1 counts from the
    // least significant bit of L1 upward, and downward into the bytes
    // before it for a negative number: -1 is bit 7 of the byte before.
    private static (uint Address, int Bit) BitAddress(uint address, uint bitNumber)
    {
        var signed = (int)bitNumber;
        return (address + (uint)(signed >> 3), signed & 7);
    }

    private void BranchIf(bool condition, uint offset, uint next)
    {
        if (condition)
        {
            Branch(offset, next);
        }
    }

    private void Branch(uint offset, uint next)
    {
        // [glulx #opcodes_branch] The destination is the address after
        // the branch plus the offset less two, except that offsets 0 and
        // 1 mean return 0 and return 1.
        switch (offset)
        {
            case 0:
                Return(0);
                break;
            case 1:
                Return(1);
                break;
            default:
                ProgramCounter = next + offset - 2;
                break;
        }
    }

    private uint[] PopArguments(uint count)
    {
        // [glulx op:call] The arguments were pushed last first, so the
        // first argument is the first popped.
        if (count > Stack.Count)
        {
            throw new GlulxException($"Stack underflow: a call wants {count} arguments and {Stack.Count} values are on the stack.");
        }

        var arguments = new uint[count];
        for (var i = 0; i < count; i++)
        {
            arguments[i] = Stack.Pop();
        }

        return arguments;
    }

    private void Call(uint address, uint[] arguments, Operand result, uint next)
    {
        // [glulx #calling-and-returning] A stub records where the result
        // goes and where to continue, then the new frame goes on top.
        var (type, destination) = Destination(result);
        Stack.PushCallStub(type, destination, next);
        Enter(address, arguments);
    }

    private void Enter(uint address, IReadOnlyList<uint> arguments)
    {
        var function = FunctionHeader.Read(Memory, address);
        Stack.PushFrame(function, arguments);
        ProgramCounter = function.CodeAddress;
    }

    private void Return(uint value)
    {
        // [glulx #calling-and-returning] The frame goes, then the stub
        // below it says where to continue. [glulx op:return] With no
        // stub, this was the start function and execution is over.
        Stack.DiscardFrame();
        if (Stack.StackPointer == 0)
        {
            HasQuit = true;
            return;
        }

        Resume(Stack.PopCallStub(), value);
    }

    private void Resume(CallStub stub, uint value)
    {
        ProgramCounter = stub.ProgramCounter;

        // [glulx #callstub] The result is stored after the program
        // counter and frame pointer are back, so a local destination
        // means a local of the resumed function.
        switch (stub.DestinationType)
        {
            case DestinationType.None:
                break;
            case DestinationType.Memory:
                Memory.WriteWord(stub.DestinationAddress, value);
                break;
            case DestinationType.Local:
                Stack.WriteLocal(stub.DestinationAddress, value, 4);
                break;
            case DestinationType.Stack:
                Stack.Push(value);
                break;

            // [glulx #callstring] A function called from inside a string
            // returns into the string, which picks up where it left off,
            // and the return value is discarded.
            case DestinationType.ResumeCompressedString:
                StreamString(stub.ProgramCounter, 0xE1, (int)stub.DestinationAddress);
                break;
            case DestinationType.ResumeInteger:
                StreamNumber((int)stub.ProgramCounter, true, (int)stub.DestinationAddress);
                break;
            case DestinationType.ResumeCString:
                StreamString(stub.ProgramCounter, 0xE0, 0);
                break;
            case DestinationType.ResumeUnicodeString:
                StreamString(stub.ProgramCounter, 0xE2, 0);
                break;
            case DestinationType.ResumeFunction:
                throw new GlulxException("A string-terminator call stub at the end of a function call.");
            default:
                throw new GlulxException($"Unknown call stub type {(uint)stub.DestinationType}.");
        }
    }

    private void SetIOSystem(uint mode, uint rock)
    {
        // [glulx op:setiosys] A system the interpreter does not support
        // falls back to the null system. Glk is not built yet, so that
        // is what happens to it for now, and gestalt says as much. The
        // rock is kept whatever the mode, as the reference interpreter
        // keeps it.
        IOSystem = mode is (uint)IOSystem.Null or (uint)IOSystem.Filter ? (IOSystem)mode : IOSystem.Null;
        IORock = rock;
    }

    private void StreamCharacter(uint character, uint next)
    {
        // [glulx #callfilter] Under the filter system a streamchar is a
        // plain call of the output function with its result discarded,
        // and execution resumes after the opcode when it returns.
        if (IOSystem == IOSystem.Filter)
        {
            Stack.PushCallStub(DestinationType.None, 0, next);
            Enter(IORock, [character]);
        }
    }

    // [glulx #callfilter] Prints a signed decimal number under the
    // filter system one character at a time, keeping its place in a
    // type 12 stub between characters: the number itself where the
    // program counter goes, and the index of the next character to
    // print, 0 for the first, where the destination goes.
    private void StreamNumber(int value, bool inMiddle, int position)
    {
        var digits = value.ToString(CultureInfo.InvariantCulture);

        if (IOSystem == IOSystem.Filter)
        {
            if (!inMiddle)
            {
                Stack.PushCallStub(DestinationType.ResumeFunction, 0, ProgramCounter);
                inMiddle = true;
            }

            if (position < digits.Length)
            {
                Stack.PushCallStub(DestinationType.ResumeInteger, (uint)(position + 1), (uint)value);
                Enter(IORock, [digits[position]]);
                return;
            }
        }

        if (inMiddle)
        {
            // The only stub left must be the one that says where the
            // function's code continues.
            var stub = Stack.PopCallStub();
            if (stub.DestinationType != DestinationType.ResumeFunction)
            {
                throw new GlulxException("A string-on-string call stub while printing a number.");
            }

            ProgramCounter = stub.ProgramCounter;
        }
    }

    // [glulx #callstring] Prints a string object, following indirect
    // references and calling functions along the way. A call suspends
    // the printing with its place kept in stubs on the stack, and the
    // function's return comes back here with inMiddle naming the kind
    // of string that was being printed, E0, E1, or E2, and bit the
    // position within the current byte of a compressed one.
    private void StreamString(uint address, byte inMiddle, int bit)
    {
        var substring = inMiddle != 0;

        while (true)
        {
            byte type;
            if (inMiddle == 0)
            {
                // [glulx #string] A new string: its type byte, then its
                // data, [glulx #string_unicode] after three padding bytes
                // for a Unicode string.
                type = Memory.ReadByte(address);
                address += type == 0xE2 ? 4u : 1u;
                bit = 0;
            }
            else
            {
                type = inMiddle;
            }

            switch (type)
            {
                case 0xE1:
                {
                    var step = StreamCompressed(ref address, ref bit, ref substring);
                    if (step.Suspended)
                    {
                        return;
                    }

                    if (step.Restarts)
                    {
                        address = step.Address;
                        inMiddle = step.Type;
                        continue;
                    }

                    break;
                }

                // [glulx #string_plain] Bytes to a zero byte. The null
                // system need not even look at them.
                case 0xE0:
                    if (IOSystem == IOSystem.Filter)
                    {
                        PushStringStart(ref substring);
                        var character = Memory.ReadByte(address++);
                        if (character != 0)
                        {
                            Stack.PushCallStub(DestinationType.ResumeCString, 0, address);
                            Enter(IORock, [character]);
                            return;
                        }
                    }

                    break;

                // [glulx #string_unicode] Four-byte code points to a zero
                // word.
                case 0xE2:
                    if (IOSystem == IOSystem.Filter)
                    {
                        PushStringStart(ref substring);
                        var character = Memory.ReadWord(address);
                        address += 4;
                        if (character != 0)
                        {
                            Stack.PushCallStub(DestinationType.ResumeUnicodeString, 0, address);
                            Enter(IORock, [character]);
                            return;
                        }
                    }

                    break;

                // [glulx #string] E3 to FF are reserved for future kinds
                // of string.
                case >= 0xE3:
                    throw new GlulxException($"Attempt to print an unknown type of string, type {type:X2}.");
                default:
                    throw new GlulxException($"Attempt to print a non-string, type byte {type:X2}.");
            }

            // The string is done. If it never needed the stack, that is
            // all; otherwise the stub on top says whether a string above
            // it is waiting, or the function is.
            if (!substring)
            {
                return;
            }

            var stub = Stack.PopCallStub();
            ProgramCounter = stub.ProgramCounter;
            switch (stub.DestinationType)
            {
                case DestinationType.ResumeFunction:
                    return;
                case DestinationType.ResumeCompressedString:
                    address = stub.ProgramCounter;
                    bit = (int)stub.DestinationAddress;
                    inMiddle = 0xE1;
                    break;
                default:
                    throw new GlulxException("A function-terminator call stub at the end of a string.");
            }
        }
    }

    // [glulx #string_enc] Decodes a compressed string from the current
    // bit, printing each leaf until the terminator. The result says
    // whether the string finished, suspended for a function call, or
    // must restart on another string an indirect reference named.
    private StringStep StreamCompressed(ref uint address, ref int bit, ref bool substring)
    {
        // [glulx op:streamstr] Illegal without a table.
        if (StringTable == 0)
        {
            throw new GlulxException("Attempt to print a compressed string with no decoding table set.");
        }

        var table = CurrentTable();
        var node = table.Root;

        while (true)
        {
            switch (node.Type)
            {
                case DecodingNodeType.Branch:
                {
                    // [glulx #string_enc] Bits are read from the low bit
                    // of each byte upward, choosing the child each time.
                    var chosen = (Memory.ReadByte(address) >> bit) & 1;
                    node = chosen == 0 ? node.Zero! : node.One!;
                    if (++bit == 8)
                    {
                        bit = 0;
                        address++;
                    }

                    continue;
                }

                case DecodingNodeType.Terminator:
                    return StringStep.Done;

                case DecodingNodeType.Character:
                case DecodingNodeType.UnicodeCharacter:
                    if (IOSystem == IOSystem.Filter)
                    {
                        PushStringStart(ref substring);
                        Stack.PushCallStub(DestinationType.ResumeCompressedString, (uint)bit, address);
                        Enter(IORock, [node.Value]);
                        return StringStep.Suspend;
                    }

                    break;

                // [glulx #string_table] A run of characters inside the
                // table is printed as a string in its own right, with
                // the decoding resumed after it.
                case DecodingNodeType.CString:
                case DecodingNodeType.UnicodeString:
                    if (IOSystem == IOSystem.Filter)
                    {
                        PushStringStart(ref substring);
                        Stack.PushCallStub(DestinationType.ResumeCompressedString, (uint)bit, address);
                        return StringStep.Restart(node.Value, node.Type == DecodingNodeType.CString ? (byte)0xE0 : (byte)0xE2);
                    }

                    break;

                case DecodingNodeType.Indirect:
                case DecodingNodeType.DoubleIndirect:
                case DecodingNodeType.IndirectWithArguments:
                case DecodingNodeType.DoubleIndirectWithArguments:
                {
                    // [glulx #string_table] The address of a string or
                    // function, or of a word holding one. A string is
                    // printed and a function called, whatever the I/O
                    // system, since a function may do anything at all.
                    var target = node.Value;
                    if (node.Type is DecodingNodeType.DoubleIndirect or DecodingNodeType.DoubleIndirectWithArguments)
                    {
                        target = Memory.ReadWord(target);
                    }

                    var targetType = Memory.ReadByte(target);
                    PushStringStart(ref substring);

                    if (targetType >= 0xE0)
                    {
                        Stack.PushCallStub(DestinationType.ResumeCompressedString, (uint)bit, address);
                        return StringStep.Restart(target, 0);
                    }

                    if (targetType is >= 0xC0 and <= 0xDF)
                    {
                        var arguments = new uint[node.ArgumentCount];
                        for (var i = 0; i < arguments.Length; i++)
                        {
                            arguments[i] = Memory.ReadWord(node.ArgumentsAddress + (uint)(4 * i));
                        }

                        Stack.PushCallStub(DestinationType.ResumeCompressedString, (uint)bit, address);
                        Enter(target, arguments);
                        return StringStep.Suspend;
                    }

                    throw new GlulxException($"Unknown object of type {targetType:X2} at {target:X8} in a string's indirect reference.");
                }

                default:
                    throw new GlulxException($"Unknown entity of type {(int)node.Type:X2} in string decoding.");
            }

            node = table.Root;
        }
    }

    // [glulx #callstring] The first time a string needs the stack, a
    // type 11 stub records where the function's code continues.
    private void PushStringStart(ref bool substring)
    {
        if (!substring)
        {
            Stack.PushCallStub(DestinationType.ResumeFunction, 0, ProgramCounter);
            substring = true;
        }
    }

    private DecodingTable CurrentTable()
    {
        // [glulx #string_table] A table in ROM is read once; one in RAM
        // is read again whenever memory has been written since, which
        // is the specification's warning about tables that change.
        if (_table is { } table
            && table.Address == StringTable
            && (table.Address + table.Length <= Memory.RamStart || _tableVersion == Memory.Version))
        {
            return table;
        }

        _table = DecodingTable.Read(Memory, StringTable);
        _tableVersion = Memory.Version;
        return _table;
    }

    // How a stretch of compressed decoding ended: at the terminator,
    // suspended for a function call, or handing over to another string.
    private readonly record struct StringStep(bool Suspended, bool Restarts, uint Address, byte Type)
    {
        public static StringStep Done => default;

        public static StringStep Suspend => new(true, false, 0, 0);

        public static StringStep Restart(uint address, byte type) => new(false, true, address, type);
    }

    private static uint Gestalt(uint selector, uint argument) => selector switch
    {
        // [glulx #opcodes_misc] The selectors, with the answers this
        // interpreter can honestly give so far. A feature answers 1
        // only once the opcodes behind it exist: Unicode does, since
        // the E2 strings, the Unicode nodes, streamunichar, and the
        // type 14 stub are all there.
        0 => GlulxHeader.SpecificationVersion,
        1 => InterpreterVersion,
        2 => 1,
        3 => 0,
        4 => argument is 0 or 1 ? 1u : 0u,
        5 => 1,
        6 => 1,
        7 => 0,
        8 => 0,
        9 => 0,
        10 => 0,
        11 => 0,
        12 => 0,
        13 => 0,
        _ => 0,
    };

    private byte[]? ProtectedBytes()
    {
        if (ProtectedLength == 0 || ProtectedStart >= Memory.Length)
        {
            return null;
        }

        var length = Math.Min(ProtectedLength, Memory.Length - ProtectedStart);
        return Memory.Slice(ProtectedStart, length).ToArray();
    }

    private void RestoreProtectedBytes(byte[]? bytes)
    {
        if (bytes is null)
        {
            return;
        }

        // Only RAM can have changed, and only what still fits after the
        // reset can be put back.
        var start = Math.Max(ProtectedStart, Memory.RamStart);
        var end = Math.Min(ProtectedStart + (uint)bytes.Length, Memory.Length);
        for (var address = start; address < end; address++)
        {
            Memory.WriteByte(address, bytes[address - ProtectedStart]);
        }
    }
}

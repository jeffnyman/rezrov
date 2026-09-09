using System.Globalization;
using Rezrov.Core.Blorb;
using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Instructions;
using Rezrov.ZMachine.Lexing;
using Rezrov.ZMachine.Objects;
using Rezrov.ZMachine.Saves;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Sound;
using Rezrov.ZMachine.Streams;
using Rezrov.ZMachine.Text;

namespace Rezrov.ZMachine.Execution;

/// <summary>
/// Runs a story file: fetches the instruction at the program counter,
/// evaluates its operands, carries it out, and repeats.
/// </summary>
/// <remarks>
/// Everything before this was a data structure. This is the loop that
/// uses them, and the opcodes of section 15 are implemented here, each
/// one cited. The opcodes of the Version 6 screen are not implemented
/// yet and say so when reached, rather than doing something approximate.
///
/// The loop sets the program counter to the next instruction before
/// carrying out the current one. The remarks on section 4 explain why
/// that is the natural arrangement: a branch or jump is then simply an
/// adjustment to where execution was already going to continue.
/// </remarks>
public sealed class Interpreter
{
    public Interpreter(
        ZMemory memory,
        IScreen screen,
        IInput input,
        RandomGenerator? random = null,
        IFileChooser? files = null,
        ISound? sound = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(input);

        Memory = memory;
        Input = input;
        Files = files ?? NoFileChooser.Instance;
        Header = new StoryHeader(memory);
        Screen = new ScreenModel(screen, Header, memory);
        Text = new ZTextDecoder(memory, Header);
        Encoder = ZTextEncoder.ForStory(Header, memory);
        ExtraCharacters = UnicodeTranslationTable.ForStory(Header, memory);
        Objects = new ObjectTable(memory, Header, Text);
        Dictionary = DictionaryTable.Standard(memory, Header, Text, Encoder);
        Decoder = new InstructionDecoder(memory, Header, Text);
        State = new GameState(memory, Header);
        Random = random ?? new RandomGenerator();
        Streams = new OutputStreams(Screen, State, Header, ExtraCharacters, Files);
        Output = Streams;
        Sound = new SoundModel(sound ?? NoSound.Instance, Header.Version);

        DescribeInterpreterInHeader();
    }

    public ZMemory Memory { get; }

    /// <summary>
    /// Where printed text goes: [zm 7] the output streams, of which the
    /// screen is the first.
    /// </summary>
    public IOutput Output { get; }

    /// <summary>[zm 7] The output streams.</summary>
    public OutputStreams Streams { get; }

    /// <summary>
    /// [zm 8] The screen model, over the frontend's screen.
    /// </summary>
    public ScreenModel Screen { get; }

    /// <summary>[zm 7.6] The frontend's way of choosing files.</summary>
    public IFileChooser Files { get; }

    /// <summary>
    /// [zm 9] The sound model, over the frontend's audio.
    /// </summary>
    public SoundModel Sound { get; }

    /// <summary>
    /// [zm 9.1] The resource file the sounds, and one day the pictures,
    /// come from, or null if there is none.
    /// </summary>
    public BlorbFile? Resources { get; private set; }

    /// <summary>
    /// Takes the sounds and pictures of a Blorb resource file, after
    /// checking it belongs to this story.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// [blorb 6] The file names another game.
    /// </exception>
    public void UseResources(BlorbFile blorb)
    {
        ArgumentNullException.ThrowIfNull(blorb);

        if (blorb.GameIdentifier is { } id
            && (id.Release != Header.Release || id.Serial != Header.SerialCode || id.Checksum != Header.Checksum))
        {
            throw new InvalidDataException(
                $"The resource file belongs to release {id.Release} serial {id.Serial}, not to release {Header.Release} serial {Header.SerialCode}.");
        }

        Resources = blorb;
        Sound.LoadResources(blorb);
    }

    /// <summary>Input stream 0, the keyboard.</summary>
    public IInput Input { get; }

    /// <summary>
    /// [zm 10.2] Which input stream is current: 0 for the keyboard or 1
    /// for a file of commands.
    /// </summary>
    public int InputStream => _commandFile is null ? 0 : 1;

    public StoryHeader Header { get; }

    public ZTextDecoder Text { get; }

    public ZTextEncoder Encoder { get; }

    /// <summary>
    /// [zm 3.8.5] The story's extra characters, needed on the way in as
    /// well as the way out.
    /// </summary>
    public UnicodeTranslationTable ExtraCharacters { get; }

    public ObjectTable Objects { get; }

    /// <summary>
    /// [zm 13.1] The game's own dictionary, read once since it lives in
    /// static memory and cannot change.
    /// </summary>
    public DictionaryTable Dictionary { get; }

    public InstructionDecoder Decoder { get; }

    public GameState State { get; }

    public RandomGenerator Random { get; }

    /// <summary>[zm op:quit] Whether the game has exited.</summary>
    public bool HasQuit { get; private set; }

    /// <summary>How many instructions have been carried out.</summary>
    public long InstructionsExecuted { get; private set; }

    /// <summary>
    /// What to do when the story file does something undefined. [zm A]
    /// Reporting each kind of error once is the middle ground the
    /// standard recommends, and the one Frotz defaults to.
    /// </summary>
    public ErrorLevel ErrorLevel { get; set; } = ErrorLevel.ReportOnce;

    /// <summary>
    /// The undefined things the story has done so far, as far as the
    /// <see cref="ErrorLevel"/> asks for them to be recorded.
    /// </summary>
    public IReadOnlyList<string> RuntimeErrors => _runtimeErrors;

    private readonly List<string> _runtimeErrors = [];
    private readonly HashSet<string> _reportedKinds = [];
    private CommandFile? _commandFile;
    private int _interruptDepth;

    /// <summary>
    /// [zm op:save_undo] The states the game has asked to be able to go
    /// back to.
    /// </summary>
    public UndoHistory Undo { get; } = new();

    /// <summary>
    /// [zm 10.2.2] Plays commands from a file for as long as it lasts,
    /// as if the game had selected input stream 1, which lets a whole
    /// game be run from a script for testing. The file's format is the
    /// one <see cref="CommandFile"/> describes.
    /// </summary>
    public void PlayCommands(TextReader commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        _commandFile?.Close();
        _commandFile = new CommandFile(commands, ExtraCharacters);
    }

    /// <summary>
    /// Carries out the instruction at the program counter and returns it.
    /// </summary>
    public Instruction Step()
    {
        if (HasQuit)
        {
            throw new InvalidOperationException("The game has quit.");
        }

        // [zm 9.4.4] A sound that ended by itself has a routine to call,
        // which happens between instructions, as an interrupt routine.
        if (Sound.HasPendingEvents)
        {
            foreach (var routine in Sound.Update())
            {
                CallInterrupt(routine);
            }
        }

        var instruction = Decoder.Decode(State.ProgramCounter);
        State.ProgramCounter = instruction.NextAddress;

        // [zm 4.5.2] Operands are evaluated first to last, which matters
        // when more than one of them is the stack.
        var arguments = new ushort[instruction.Operands.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = Evaluate(instruction, instruction.Operands[i]);
        }

        try
        {
            Execute(instruction, arguments);
        }
        catch (ArgumentOutOfRangeException e)
        {
            // The tables reject an illegal object, property, or attribute
            // number with an argument exception. Reported that way it
            // says nothing about where the game was, so it becomes an
            // interpreter error naming the instruction.
            throw Fail(instruction, e.Message);
        }

        InstructionsExecuted++;

        return instruction;
    }

    /// <summary>
    /// Runs until the game quits or <paramref name="limit"/> instructions
    /// have been carried out.
    /// </summary>
    public void Run(long limit = long.MaxValue)
    {
        while (!HasQuit && limit-- > 0)
        {
            Step();
        }
    }

    // [zm 4.2.3] A variable operand means the variable's value, and
    // [zm 6.3] reading variable 0 pops the stack.
    private ushort Evaluate(Instruction instruction, Operand operand)
    {
        if (operand.Type != OperandType.Variable)
        {
            return operand.Value;
        }

        return operand.Value == 0 ? Pop(instruction) : State.ReadVariable(operand.Value);
    }

    /// <summary>
    /// Pops the stack, or reports an underflow and gives 0.
    /// </summary>
    /// <remarks>
    /// [zm 6.3.2] It is illegal to pull from the stack unless values were
    /// first pushed, and [zm op:pull deviates] the pull opcode says the
    /// interpreter should halt. Zork I release 2 does it anyway: the
    /// routine at $5816 in its parser tests a called routine's result
    /// with je, which pops it, then pulls it a second time. Infocom's
    /// interpreter kept locals and stack together with no frame check,
    /// so the pull quietly read a local, and Frotz's layout does the
    /// same. So this is an error in the sense of [zm A], reported at the
    /// level chosen and fatal only at the strictest, and the value that
    /// was not there is 0.
    /// </remarks>
    private ushort Pop(Instruction instruction)
    {
        if (State.TryPop(out var value))
        {
            return value;
        }

        ReportRuntimeError(instruction, "the stack is empty for the current routine");
        return 0;
    }

    // [zm 6.3.4] A variable reference by number, where 0 means the top of
    // the stack in place rather than a pop, with the same leniency as
    // <see cref="Pop"/> when there is no top.
    private ushort ReadInPlace(Instruction instruction, ushort variable)
    {
        if (variable != 0)
        {
            return State.ReadVariable(variable);
        }

        if (State.TryPeek(out var value))
        {
            return value;
        }

        ReportRuntimeError(instruction, "the stack is empty for the current routine");
        return 0;
    }

    private void Execute(Instruction instruction, ushort[] a)
    {
        // [zm 7.4] The game may have set or cleared the transcript bit
        // directly since the last instruction, and stream 2 must agree
        // with it before anything is printed.
        if (!Streams.SyncWithHeader())
        {
            ReportRuntimeError(instruction, "no transcript file is available");
        }

        switch (instruction.Opcode)
        {
            // Arithmetic. [zm 2.2.1] Addition, subtraction, multiplication,
            // division, and remainder are signed, and [zm 2.3.2] results
            // out of range are reduced modulo $10000, which is what the
            // cast to 16 bits does.
            case Opcode.Add:
                Store(instruction, Unsigned(Signed(a[0]) + Signed(a[1])));
                break;
            case Opcode.Sub:
                Store(instruction, Unsigned(Signed(a[0]) - Signed(a[1])));
                break;
            case Opcode.Mul:
                Store(instruction, Unsigned(Signed(a[0]) * Signed(a[1])));
                break;
            case Opcode.Div:
                // [zm 2.3.1] Division by zero halts the interpreter. The
                // remarks give the signed cases: -11 / 2 is -5, and
                // -11 / -2 is 5, which truncation toward zero produces.
                RequireNonZeroDivisor(instruction, a[1]);
                Store(instruction, Unsigned(Signed(a[0]) / Signed(a[1])));
                break;
            case Opcode.Mod:
                // [zm 2] The remarks: -13 % 5 is -3 and 13 % -5 is 3, so
                // the remainder takes the sign of the dividend.
                RequireNonZeroDivisor(instruction, a[1]);
                Store(instruction, Unsigned(Signed(a[0]) % Signed(a[1])));
                break;

            // Bitwise. [zm 2.2.1] These are unsigned.
            case Opcode.And:
                Store(instruction, (ushort)(a[0] & a[1]));
                break;
            case Opcode.Or:
                Store(instruction, (ushort)(a[0] | a[1]));
                break;
            case Opcode.Not:
                // [zm op:not] All 16 bits reversed.
                Store(instruction, (ushort)~a[0]);
                break;
            case Opcode.LogShift:
                // [zm op:log_shift] Left for positive places; right with
                // zeros shifted in for negative.
                Store(instruction, Signed(a[1]) >= 0
                    ? Unsigned(a[0] << Signed(a[1]))
                    : (ushort)(a[0] >> -Signed(a[1])));
                break;
            case Opcode.ArtShift:
                // [zm op:art_shift] The same, except a right shift keeps
                // the sign bit and shifts it on down.
                Store(instruction, Signed(a[1]) >= 0
                    ? Unsigned(a[0] << Signed(a[1]))
                    : Unsigned(Signed(a[0]) >> -Signed(a[1])));
                break;

            // Variables. [zm 4.2.3] The first operand of these is a
            // variable number, and [zm 6.3.4] a reference to the stack
            // pointer reads or writes the top of the stack in place.
            case Opcode.Inc:
                // [zm op:inc] Signed, so -1 goes to 0.
                State.WriteVariableInPlace(a[0], Unsigned(Signed(ReadInPlace(instruction, a[0])) + 1));
                break;
            case Opcode.Dec:
                State.WriteVariableInPlace(a[0], Unsigned(Signed(ReadInPlace(instruction, a[0])) - 1));
                break;
            case Opcode.IncChk:
            {
                // [zm op:inc_chk] Increment, then branch if now greater.
                var value = Unsigned(Signed(ReadInPlace(instruction, a[0])) + 1);
                State.WriteVariableInPlace(a[0], value);
                Branch(instruction, Signed(value) > Signed(a[1]));
                break;
            }
            case Opcode.DecChk:
            {
                // [zm op:dec_chk] Decrement, then branch if now less.
                var value = Unsigned(Signed(ReadInPlace(instruction, a[0])) - 1);
                State.WriteVariableInPlace(a[0], value);
                Branch(instruction, Signed(value) < Signed(a[1]));
                break;
            }
            case Opcode.Store:
                // [zm op:store]
                State.WriteVariableInPlace(a[0], a[1]);
                break;
            case Opcode.Load:
                // [zm op:load]
                Store(instruction, ReadInPlace(instruction, a[0]));
                break;
            case Opcode.Push:
                // [zm op:push]
                State.Push(a[0]);
                break;
            case Opcode.Pull:
                if (Header.Version == ZMachineVersion.V6)
                {
                    // [zm op:pull] In Version 6 the value is stored, and
                    // the stack may be a user stack named by the operand.
                    Store(instruction, a.Length > 0 && a[0] != 0
                        ? UserStackTable.Pull(Memory, a[0])
                        : Pop(instruction));
                }
                else
                {
                    // [zm op:pull] Elsewhere the operand names the variable
                    // to pull into, in place if it is the stack pointer.
                    var value = Pop(instruction);
                    State.WriteVariableInPlace(a[0], value);
                }

                break;
            case Opcode.Pop:
                // [zm op:pop] Throw away the top of the stack.
                Pop(instruction);
                break;

            // Comparisons and branches.
            case Opcode.Je:
                // [zm op:je] Equal to any of the later operands, and one
                // operand alone is not permitted.
                if (a.Length < 2)
                {
                    throw Fail(instruction, "je with just one operand is not permitted.");
                }

                Branch(instruction, a.Skip(1).Any(v => v == a[0]));
                break;
            case Opcode.Jl:
                // [zm op:jl] Signed.
                Branch(instruction, Signed(a[0]) < Signed(a[1]));
                break;
            case Opcode.Jg:
                Branch(instruction, Signed(a[0]) > Signed(a[1]));
                break;
            case Opcode.Jz:
                // [zm op:jz]
                Branch(instruction, a[0] == 0);
                break;
            case Opcode.Test:
                // [zm op:test] All the flags in the bitmap are set.
                Branch(instruction, (a[0] & a[1]) == a[1]);
                break;
            case Opcode.Jin:
                // [zm op:jin] The parent of the first is the second. The
                // second may be 0, which asks whether the first has no
                // parent at all.
                Branch(instruction, ObjectExists(instruction, a[0]) && Objects.Parent(a[0]) == a[1]);
                break;
            case Opcode.TestAttr:
                // [zm op:test_attr]
                Branch(instruction, ObjectExists(instruction, a[0]) && Objects.HasAttribute(a[0], a[1]));
                break;
            case Opcode.CheckArgCount:
                // [zm op:check_arg_count]
                Branch(instruction, State.ArgumentWasSupplied(a[0]));
                break;
            case Opcode.Verify:
                // [zm op:verify] Branch if the checksum agrees. A file with
                // no length recorded has nothing to agree with.
                Branch(instruction, Header.HasFileLength && Header.VerifyChecksum());
                break;
            case Opcode.Piracy:
                // [zm op:piracy] Interpreters are asked to be gullible.
                Branch(instruction, true);
                break;
            case Opcode.Jump:
                // [zm op:jump] Not a branch: a signed offset applied to
                // the program counter, less 2, like the branch formula.
                State.ProgramCounter = instruction.NextAddress + Signed(a[0]) - 2;
                break;

            // Routine calls and returns. [zm 6.4] The state does the work;
            // what differs between these is whether the result is kept.
            case Opcode.Call:
            case Opcode.CallVs:
            case Opcode.CallVs2:
            case Opcode.Call1s:
            case Opcode.Call2s:
                Call(instruction, a, storesResult: true);
                break;
            case Opcode.CallVn:
            case Opcode.CallVn2:
            case Opcode.Call1n:
            case Opcode.Call2n:
                // [zm op:call_vn] Like call, but the result is thrown away.
                Call(instruction, a, storesResult: false);
                break;
            case Opcode.Ret:
                // [zm op:ret]
                State.Return(a[0]);
                break;
            case Opcode.Rtrue:
                // [zm op:rtrue]
                State.Return(1);
                break;
            case Opcode.Rfalse:
                // [zm op:rfalse]
                State.Return(0);
                break;
            case Opcode.RetPopped:
                // [zm op:ret_popped] Equivalent to ret sp.
                State.Return(Pop(instruction));
                break;
            case Opcode.Catch:
                // [zm op:catch]
                Store(instruction, (ushort)State.FrameNumber);
                break;
            case Opcode.Throw:
                // [zm op:throw] Back to the caught frame, then return.
                State.ThrowTo(a[1], a[0]);
                break;

            // Memory. [zm op:loadw] and [zm op:loadb] address array plus
            // twice or once the index, in either dynamic or static memory;
            // [zm op:storew] and [zm op:storeb] the same, but the address
            // must be in dynamic memory, which the state enforces. The
            // addition wraps at 16 bits, so a negative index works.
            case Opcode.Loadw:
                Store(instruction, Memory.ReadWord((ushort)(a[0] + (2 * a[1]))));
                break;
            case Opcode.Loadb:
                Store(instruction, Memory.ReadByte((ushort)(a[0] + a[1])));
                break;
            case Opcode.Storew:
                State.WriteWord((ushort)(a[0] + (2 * a[1])), a[2]);
                break;
            case Opcode.Storeb:
                State.WriteByte((ushort)(a[0] + a[1]), (byte)a[2]);
                break;
            case Opcode.CopyTable:
                CopyTable(a[0], a[1], a[2]);
                break;
            case Opcode.ScanTable:
                ScanTable(instruction, a);
                break;

            // Objects. Every one of these checks its object operand first,
            // because [zm A] operating on object 0 is the most common bug
            // in released games, and what happens then depends on the
            // error level rather than being a crash.
            case Opcode.GetParent:
                // [zm op:get_parent] No branch, unlike its two siblings.
                Store(instruction, ObjectExists(instruction, a[0]) ? (ushort)Objects.Parent(a[0]) : (ushort)0);
                break;
            case Opcode.GetSibling:
            {
                // [zm op:get_sibling] Store it, and branch if it exists.
                var sibling = ObjectExists(instruction, a[0]) ? (ushort)Objects.Sibling(a[0]) : (ushort)0;
                Store(instruction, sibling);
                Branch(instruction, sibling != 0);
                break;
            }
            case Opcode.GetChild:
            {
                // [zm op:get_child]
                var child = ObjectExists(instruction, a[0]) ? (ushort)Objects.Child(a[0]) : (ushort)0;
                Store(instruction, child);
                Branch(instruction, child != 0);
                break;
            }
            case Opcode.InsertObj:
                // [zm op:insert_obj] Both the object and its destination
                // have to exist.
                if (ObjectExists(instruction, a[0]) && ObjectExists(instruction, a[1]))
                {
                    Objects.Insert(a[0], a[1]);
                }

                break;
            case Opcode.RemoveObj:
                // [zm op:remove_obj]
                if (ObjectExists(instruction, a[0]))
                {
                    Objects.Remove(a[0]);
                }

                break;
            case Opcode.SetAttr:
                // [zm op:set_attr]
                if (ObjectExists(instruction, a[0]))
                {
                    Objects.SetAttribute(a[0], a[1]);
                }

                break;
            case Opcode.ClearAttr:
                // [zm op:clear_attr]
                if (ObjectExists(instruction, a[0]))
                {
                    Objects.ClearAttribute(a[0], a[1]);
                }

                break;
            case Opcode.GetProp:
                Store(instruction, ObjectExists(instruction, a[0]) ? GetProperty(a[0], a[1]) : (ushort)0);
                break;
            case Opcode.PutProp:
                if (ObjectExists(instruction, a[0]))
                {
                    PutProperty(instruction, a[0], a[1], a[2]);
                }

                break;
            case Opcode.GetPropAddr:
                // [zm op:get_prop_addr] The data's address, or 0 if the
                // object does not have the property. A property number
                // above the range is answered with 0 as well, without
                // complaint: [zm 15.3] leaves it undefined, and Inform 6's
                // own library depends on it, since its individual
                // properties are numbered from 64 and it asks here first
                // before looking in its own table.
                Store(instruction, ObjectExists(instruction, a[0])
                    && a[1] >= 1 && a[1] <= Objects.MaxPropertyNumber
                    && Objects.TryFindProperty(a[0], a[1], out var block)
                    ? (ushort)block.DataAddress
                    : (ushort)0);
                break;
            case Opcode.GetPropLen:
                // [zm op:get_prop_len] Address 0 must give 0, which some
                // Infocom games and old Inform output rely on.
                Store(instruction, a[0] == 0 ? (ushort)0 : (ushort)Objects.PropertyLength(a[0]));
                break;
            case Opcode.GetNextProp:
                Store(instruction, ObjectExists(instruction, a[0]) ? NextProperty(instruction, a[0], a[1]) : (ushort)0);
                break;

            // Text. Each of these produces ZSCII and hands it to the
            // output, which decides what it looks like.
            case Opcode.Print:
                // [zm op:print] The string is inline after the opcode.
                PrintString(instruction.TextAddress!.Value);
                break;
            case Opcode.PrintRet:
                // [zm op:print_ret] Print, then a newline, then return
                // true.
                PrintString(instruction.TextAddress!.Value);
                Output.Print(Zscii.Newline);
                State.Return(1);
                break;
            case Opcode.PrintAddr:
                // [zm op:print_addr] A byte address.
                PrintString(a[0]);
                break;
            case Opcode.PrintPaddr:
                // [zm op:print_paddr] A packed address in high memory.
                PrintString(Header.UnpackStringAddress(a[0]));
                break;
            case Opcode.PrintObj:
                PrintObjectName(instruction, a[0]);
                break;
            case Opcode.PrintChar:
                // [zm op:print_char] One ZSCII code.
                Output.Print(a[0]);
                break;
            case Opcode.PrintNum:
                // [zm op:print_num] Signed, in decimal.
                PrintNumber(Signed(a[0]));
                break;
            case Opcode.NewLine:
                // [zm op:new_line]
                Output.Print(Zscii.Newline);
                break;
            case Opcode.EncodeText:
                EncodeText(a[0], a[1], a[2], a[3]);
                break;

            // Miscellany.
            case Opcode.Random:
                RandomNumber(instruction, a[0]);
                break;
            case Opcode.Quit:
                // [zm op:quit] The only legal way to stop, since the
                // starting routine cannot return. Whatever is still
                // buffered is shown and written first, and the sound
                // stops with the game.
                Streams.Flush();
                Sound.StopAll();
                HasQuit = true;
                break;
            case Opcode.Restart:
                // [zm op:restart] Everything back to the start except the
                // transcript and fixed-pitch bits of Flags 2, which the
                // state preserves, and then [zm 6.1.3] the interpreter's
                // own header fields are set again and the screen is as
                // at the start of a game.
                State.Restart();
                Screen.Reset();
                Streams.Reset();
                Sound.StopAll();
                DescribeInterpreterInHeader();
                break;
            case Opcode.Nop:
                // [zm op:nop] Never used by any Infocom game, apparently.
                break;
            case Opcode.ShowStatus:
                // [zm op:show_status] Redraws the Version 3 status line,
                // and is to be treated as a nop where it appears by
                // accident in later versions.
                if (Header.Version == ZMachineVersion.V3)
                {
                    ShowStatusLine(instruction);
                }

                break;

            // The screen. Version 6 has a model of its own, [zm 8.8],
            // which is not built, so its games stop here rather than
            // run under the wrong one.
            case Opcode.SplitWindow when Header.Version == ZMachineVersion.V6:
            case Opcode.SetWindow when Header.Version == ZMachineVersion.V6:
            case Opcode.EraseWindow when Header.Version == ZMachineVersion.V6:
            case Opcode.EraseLine when Header.Version == ZMachineVersion.V6:
            case Opcode.SetCursor when Header.Version == ZMachineVersion.V6:
            case Opcode.GetCursor when Header.Version == ZMachineVersion.V6:
            case Opcode.SetTextStyle when Header.Version == ZMachineVersion.V6:
            case Opcode.BufferMode when Header.Version == ZMachineVersion.V6:
            case Opcode.SetColour when Header.Version == ZMachineVersion.V6:
            case Opcode.SetTrueColour when Header.Version == ZMachineVersion.V6:
            case Opcode.SetFont when Header.Version == ZMachineVersion.V6:
            case Opcode.PrintTable when Header.Version == ZMachineVersion.V6:
                throw new NotSupportedException($"{instruction.Name} needs the Version 6 screen model, which is not implemented yet.");
            case Opcode.SplitWindow:
                Screen.SplitWindow(a[0]);
                break;
            case Opcode.SetWindow:
                // [zm op:set_window] Only 0 and 1 exist before Version 6.
                if (a[0] > ScreenModel.Upper)
                {
                    ReportRuntimeError(instruction, $"there is no window {a[0]}");
                }
                else
                {
                    Screen.SetWindow(a[0]);
                }

                break;
            case Opcode.EraseWindow:
                if (!Screen.EraseWindow(Signed(a[0])))
                {
                    ReportRuntimeError(instruction, $"there is no window {Signed(a[0])}");
                }

                break;
            case Opcode.EraseLine:
                // [zm op:erase_line] Versions 4 and 5: only a value of 1
                // does anything.
                if (a[0] == 1)
                {
                    Screen.EraseLine();
                }

                break;
            case Opcode.SetCursor:
                if (!Screen.SetCursor(Signed(a[0]), Signed(a[1])))
                {
                    ReportRuntimeError(instruction, $"row {Signed(a[0])}, column {Signed(a[1])} is outside the upper window");
                }

                break;
            case Opcode.GetCursor:
            {
                // [zm op:get_cursor] Row into word 0, column into word 1.
                var (row, column) = Screen.GetCursor();
                State.WriteWord(a[0], (ushort)row);
                State.WriteWord(a[0] + 2, (ushort)column);
                break;
            }

            case Opcode.SetTextStyle:
                if ((a[0] & ~0x0F) != 0)
                {
                    ReportRuntimeError(instruction, $"{a[0]} is not a text style");
                }

                Screen.SetTextStyle(a[0]);
                break;
            case Opcode.BufferMode:
                // [zm op:buffer_mode] 1 is on, 0 is off, and anything
                // else is taken as on, as Frotz takes it.
                Screen.SetBuffering(a[0] != 0);
                break;
            case Opcode.SetColour:
                if (!Screen.SetColors((ScreenColor)Signed(a[0]), (ScreenColor)Signed(a[1])))
                {
                    ReportRuntimeError(instruction, $"colors {Signed(a[0])} and {Signed(a[1])} are not both available in this version");
                }

                break;
            case Opcode.SetTrueColour:
                if (!Screen.SetTrueColors(Signed(a[0]), Signed(a[1])))
                {
                    ReportRuntimeError(instruction, $"true colors {Signed(a[0])} and {Signed(a[1])} are not both available in this version");
                }

                break;
            case Opcode.SetFont:
                Store(instruction, (ushort)Screen.SetFont(a[0]));
                break;
            case Opcode.PrintTable:
                PrintTable(a);
                break;
            case Opcode.PrintUnicode:
                // [zm op:print_unicode] [zm 7.5] To every stream, each
                // in its own form.
                Streams.PrintUnicode((char)a[0]);
                break;
            case Opcode.OutputStream:
                SelectOutputStream(instruction, a);
                break;
            case Opcode.CheckUnicode:
                CheckUnicode(instruction, a[0]);
                break;

            // Input.
            case Opcode.Sread:
            case Opcode.Aread:
                Read(instruction, a);
                break;
            case Opcode.ReadChar:
                ReadChar(instruction, a);
                break;
            case Opcode.Tokenise:
                Tokenise(a);
                break;
            case Opcode.InputStream:
                SelectInputStream(instruction, a[0]);
                break;
            case Opcode.PopStack:
                // [zm op:pop_stack] From a user stack if one is named,
                // otherwise from the game stack.
                if (a.Length > 1 && a[1] != 0)
                {
                    UserStackTable.Pop(Memory, a[1], a[0]);
                }
                else
                {
                    for (var i = 0; i < a[0]; i++)
                    {
                        Pop(instruction);
                    }
                }

                break;
            case Opcode.PushStack:
                // [zm op:push_stack] Branch if it fit.
                Branch(instruction, UserStackTable.TryPush(Memory, a[1], a[0]));
                break;
            case Opcode.Unknown:
                // [zm 14.2.1] Extended opcodes from 30 up are ignored.
                break;

            // Not yet. Each of these needs a part of the machine that does
            // not exist in this codebase so far, and saying so beats
            // guessing at it.
            case Opcode.SoundEffect:
                SoundEffect(instruction, a);
                break;
            case Opcode.Save:
                Save(instruction, a);
                break;
            case Opcode.Restore:
                Restore(instruction, a);
                break;
            case Opcode.SaveUndo:
                SaveUndo(instruction);
                break;
            case Opcode.RestoreUndo:
                RestoreUndo(instruction);
                break;
            case Opcode.DrawPicture:
            case Opcode.PictureData:
            case Opcode.ErasePicture:
            case Opcode.SetMargins:
            case Opcode.MoveWindow:
            case Opcode.WindowSize:
            case Opcode.WindowStyle:
            case Opcode.GetWindProp:
            case Opcode.ScrollWindow:
            case Opcode.ReadMouse:
            case Opcode.MouseWindow:
            case Opcode.PutWindProp:
            case Opcode.PrintForm:
            case Opcode.MakeMenu:
            case Opcode.PictureTable:
            case Opcode.BufferScreen:
                throw new NotSupportedException($"{instruction.Name} needs the Version 6 screen model, which is not implemented yet.");

            default:
                throw Fail(instruction, "This opcode is not handled by the interpreter, which is a bug in Rezrov.");
        }
    }

    // [zm 4.6] The result goes into the variable the store byte names.
    private void Store(Instruction instruction, ushort value) =>
        State.WriteVariable(instruction.StoreVariable ?? throw Fail(instruction, "has no store variable"), value);

    // [zm 4.7] Branch when the condition matches the sense in the branch
    // data. [zm 4.7.1] Offsets 0 and 1 return false or true instead of
    // jumping, and otherwise [zm 4.7.2] the target is the address after
    // the branch data, plus the offset, minus 2.
    private void Branch(Instruction instruction, bool condition)
    {
        var branch = instruction.Branch ?? throw Fail(instruction, "has no branch data");

        if (condition != branch.OnTrue)
        {
            return;
        }

        if (branch.ReturnsFalse)
        {
            State.Return(0);
        }
        else if (branch.ReturnsTrue)
        {
            State.Return(1);
        }
        else
        {
            State.ProgramCounter = branch.Target(instruction.NextAddress);
        }
    }

    // [zm op:call] The first operand is the routine's packed address and
    // the rest are its arguments. [zm 6.4.3] Address 0 does nothing and
    // returns false, which the state handles.
    private void Call(Instruction instruction, ushort[] a, bool storesResult)
    {
        if (a.Length == 0)
        {
            throw Fail(instruction, "A call needs at least the routine to call.");
        }

        State.CallRoutine(a[0], a.AsSpan(1), storesResult ? instruction.StoreVariable : null, instruction.NextAddress);
    }

    private ushort GetProperty(int obj, int property)
    {
        // [zm op:get_prop] The default value if the object does not have
        // the property. A one-byte property is that byte; otherwise the
        // first two bytes as a word. The standard says the result is
        // unspecified for longer properties rather than illegal, and
        // reading the first word is what Frotz does, so that is what
        // happens here.
        if (!Objects.TryFindProperty(obj, property, out var block))
        {
            return Objects.PropertyDefault(property);
        }

        return block.Length == 1 ? Memory.ReadByte(block.DataAddress) : Memory.ReadWord(block.DataAddress);
    }

    private void PutProperty(Instruction instruction, int obj, int property, ushort value)
    {
        // [zm op:put_prop] The object must have the property. Only the
        // least significant byte is stored into a one-byte property, so
        // -1 becomes 255.
        if (!Objects.TryFindProperty(obj, property, out var block))
        {
            throw Fail(instruction, $"Object {obj} does not have property {property}.");
        }

        if (block.Length == 1)
        {
            State.WriteByte(block.DataAddress, (byte)value);
        }
        else
        {
            State.WriteWord(block.DataAddress, value);
        }
    }

    private ushort NextProperty(Instruction instruction, int obj, int property)
    {
        // [zm op:get_next_prop] Property 0 asks for the first; otherwise
        // the one after the given property, which must exist, and 0 at
        // the end of the list.
        using var properties = Objects.Properties(obj).GetEnumerator();

        if (property == 0)
        {
            return properties.MoveNext() ? (ushort)properties.Current.Number : (ushort)0;
        }

        while (properties.MoveNext())
        {
            if (properties.Current.Number == property)
            {
                return properties.MoveNext() ? (ushort)properties.Current.Number : (ushort)0;
            }
        }

        throw Fail(instruction, $"Object {obj} does not have property {property}, so it has no next property.");
    }

    private void CopyTable(ushort first, ushort second, ushort sizeWord)
    {
        // [zm op:copy_table] A second address of 0 means zero the bytes
        // of the first. Otherwise copy: safely, so that overlapping
        // tables come out right, when the size is positive; forward even
        // if that corrupts the source when it is negative, which Beyond
        // Zork uses to fill an array. The remarks record that copying
        // always backward broke Journey's menus, so the direction matters.
        var size = Signed(sizeWord);
        var length = Math.Abs(size);

        if (second == 0)
        {
            for (var i = 0; i < length; i++)
            {
                State.WriteByte(first + i, 0);
            }

            return;
        }

        var forward = size < 0 || second <= first || second >= first + length;
        if (forward)
        {
            for (var i = 0; i < length; i++)
            {
                State.WriteByte(second + i, Memory.ReadByte(first + i));
            }
        }
        else
        {
            for (var i = length - 1; i >= 0; i--)
            {
                State.WriteByte(second + i, Memory.ReadByte(first + i));
            }
        }
    }

    private void ScanTable(Instruction instruction, ushort[] a)
    {
        // [zm op:scan_table] Look for x among len fields of the table,
        // storing the address of the first match and branching, or 0 and
        // not. The form byte's top bit chooses words over bytes and the
        // rest is the field length, with $82 the default.
        var form = a.Length > 3 ? a[3] : 0x82;
        var fieldLength = form & 0x7F;
        var words = (form & 0x80) != 0;

        for (var i = 0; i < a[2]; i++)
        {
            var address = a[1] + (i * fieldLength);
            var value = words ? Memory.ReadWord(address) : Memory.ReadByte(address);

            if (value == a[0])
            {
                Store(instruction, (ushort)address);
                Branch(instruction, true);
                return;
            }
        }

        Store(instruction, 0);
        Branch(instruction, false);
    }

    /// <summary>
    /// [zm 8.2] The status line of Versions 1 to 3, shown
    /// [zm 8.2.4] by show_status and just before read takes input.
    /// </summary>
    /// <remarks>
    /// [zm 8.2.2] The first global names the object whose short name
    /// goes on the left, and [zm 8.2.2.1] must be a valid object number
    /// whenever the line is shown. The standard asks interpreters to
    /// protect themselves when a game gets that wrong, so an invalid
    /// number is a runtime error and the name is left blank.
    /// [zm 8.2.1] In Versions 1 and 2 every game is a score game; in
    /// Version 3 bit 1 of Flags 1 makes it a time game.
    /// </remarks>
    private void ShowStatusLine(Instruction instruction)
    {
        if (Header.Version > ZMachineVersion.V3)
        {
            return;
        }

        // [zm 6.2] Globals are variables $10 upward, so the first three
        // are $10, $11, and $12.
        var obj = State.ReadGlobal(0x10);
        var name = "";
        if (obj >= 1 && obj <= Objects.Count)
        {
            name = Objects.ShortName(obj);
        }
        else
        {
            ReportRuntimeError(instruction, $"the status line needs an object in global 0, not {obj}");
        }

        var timeGame = Header.Version == ZMachineVersion.V3
            && Header.Flags1Versions1To3.HasFlag(Flags1Versions1To3.TimeStatusLine);

        Screen.ShowStatusLine(name, timeGame, Signed(State.ReadGlobal(0x11)), Signed(State.ReadGlobal(0x12)));
    }

    private void PrintTable(ushort[] a)
    {
        // [zm op:print_table] A rectangle of ZSCII text, width by height
        // (height defaulting to 1), skipping skip characters between
        // rows, spreading right and down from the cursor.
        var text = a[0];
        var width = a[1];
        var height = a.Length > 2 ? a[2] : 1;
        var skip = a.Length > 3 ? a[3] : 0;
        var startColumn = Screen.UpperWindow.CursorColumn;

        for (var row = 0; row < height; row++)
        {
            if (row > 0)
            {
                Screen.NextTableRow(startColumn);
            }

            for (var column = 0; column < width; column++)
            {
                Output.Print(Memory.ReadByte(text++));
            }

            text = (ushort)(text + skip);
        }
    }

    private void CheckUnicode(Instruction instruction, ushort character)
    {
        // [zm op:check_unicode] Bit 0 if the screen can print it, bit 1
        // if the keyboard can produce it. [zm 10.7] The keyboard only
        // ever produces ZSCII, so a character can come in if the story's
        // translation table has a code for it.
        var result = 0;
        if (Screen.CanPrint((char)character))
        {
            result |= 1;
        }

        if (Zscii.FromUnicode((char)character, ExtraCharacters) is not null)
        {
            result |= 2;
        }

        Store(instruction, (ushort)result);
    }

    private void Read(Instruction instruction, ushort[] a)
    {
        // Etude, Andrew Plotkin's interpreter test, calls aread with the
        // text buffer alone, which the operand table allows, and a parse
        // buffer that was never given is the same as one given as 0.
        var text = a[0];
        var parse = a.Length > 1 ? a[1] : (ushort)0;
        var early = Header.Version <= ZMachineVersion.V4;

        // [zm op:read] Byte 0 of the text buffer holds the most letters
        // that may be typed, plus one for the zero terminator in Versions
        // 1 to 4. The standard asks for a halt with a message when either
        // buffer is too small to be real, since that usually means an
        // array before it was overrun, and is otherwise a bug that is very
        // hard to find.
        var maxLength = early ? Memory.ReadByte(text) - 1 : Memory.ReadByte(text);
        if (maxLength < 1)
        {
            throw Fail(instruction, $"The text buffer at {text:X4} cannot hold a single character, which usually means an array before it was overrun.");
        }

        // [zm op:read] In Versions 5 and later a parse buffer of 0 means
        // no lexical analysis at all.
        var lexing = early || parse != 0;
        if (lexing && Memory.ReadByte(parse) < 1)
        {
            throw Fail(instruction, $"The parse buffer at {parse:X4} cannot hold a single word, which usually means an array before it was overrun.");
        }

        // [zm op:read] In Versions 5 and later, a positive byte 1 is a
        // count of characters left over from an interrupted read, which
        // the new input continues from. The game redisplays them itself.
        var initial = new List<ushort>();
        if (!early)
        {
            var leftover = Math.Min(Memory.ReadByte(text + 1), maxLength);
            for (var i = 0; i < leftover; i++)
            {
                initial.Add(Memory.ReadByte(text + 2 + i));
            }
        }

        // [zm 10.5.1] In Versions 1 to 3 the status line is redisplayed
        // before input is accepted.
        ShowStatusLine(instruction);
        Screen.PrepareForInput(InputStream == 1);
        Sound.InputHappened();

        var request = new LineInputRequest(
            maxLength,
            initial,
            TerminatingCharacters.Read(Memory, Header),
            Timer(a.Length > 2 ? a[2] : (ushort)0, a.Length > 3 ? a[3] : (ushort)0));
        var line = ReadLine(request);
        Screen.InputEnded(line.Terminator == Zscii.Newline);

        // [zm op:read] The text is reduced to lower case, and [zm 10.7.2]
        // only characters defined for both input and output can be
        // stored, so anything else a source let through is dropped.
        // Nothing above 255 can appear, since [zm 3.8.1] no input code
        // does, so a byte holds each one.
        var typed = new List<byte>(line.Text.Count);
        foreach (var code in line.Text)
        {
            if (typed.Count == maxLength)
            {
                break;
            }

            if (Zscii.IsDefinedForInputAndOutput(code))
            {
                typed.Add((byte)Zscii.ToLower(code, ExtraCharacters));
            }
        }

        // [zm op:read] Versions 1 to 4 store the text from byte 1 with a
        // zero terminator. Versions 5 and later store the count in byte 1
        // and the text from byte 2, with no terminator.
        var offset = early ? 1 : 2;
        for (var i = 0; i < typed.Count; i++)
        {
            State.WriteByte(text + offset + i, typed[i]);
        }

        if (early)
        {
            State.WriteByte(text + 1 + typed.Count, 0);
        }
        else
        {
            State.WriteByte(text + 1, (byte)typed.Count);
        }

        // [zm op:read deviates] The standard says an interrupt routine
        // returning true erases all input, but the remarks on section 7
        // show a timed-out command being continued from what was typed,
        // and Frotz keeps the text, which Beyond Zork and Zork Zero rely
        // on through the leftover count in byte 1. So the text stays, and
        // only the terminator says the read was cut short.
        if (lexing)
        {
            WriteParseTable(parse, typed.ToArray(), offset, Dictionary, keepUnknownSlots: false);
        }

        // [zm op:read] In Version 5 and later this is a store instruction
        // whose result is the terminating character: 13 for the enter
        // key, whatever the keyboard called it, and 0 for a timeout.
        if (!early)
        {
            Store(instruction, line.Terminator);
        }
    }

    private void ReadChar(Instruction instruction, ushort[] a)
    {
        // [zm op:read_char] The first operand must be 1, presumably for
        // input devices that were never built. Anything else, including
        // no operand at all as strictz tries, is an error in the game,
        // and there is still only the keyboard to read.
        if (a.Length == 0 || a[0] != 1)
        {
            ReportRuntimeError(instruction, $"the input device must be 1, not {(a.Length == 0 ? "omitted" : a[0].ToString(CultureInfo.InvariantCulture))}");
        }

        var timer = Timer(a.Length > 1 ? a[1] : (ushort)0, a.Length > 2 ? a[2] : (ushort)0);
        Screen.PrepareForInput(InputStream == 1);
        Sound.InputHappened();
        Store(instruction, ReadKey(timer));
    }

    /// <summary>
    /// [zm op:read] The timer for a read or read_char: only in Version 4
    /// and later, and only when both the time and the routine are
    /// supplied and non-zero.
    /// </summary>
    private InputTimer? Timer(ushort time, ushort routine)
    {
        if (Header.Version < ZMachineVersion.V4 || time == 0 || routine == 0)
        {
            return null;
        }

        return new InputTimer(time, () => CallInterrupt(routine));
    }

    /// <summary>
    /// [zm op:read] Calls the interrupt routine in the middle of an input
    /// and reports whether it returned true, meaning stop reading.
    /// </summary>
    /// <remarks>
    /// The routine runs to completion here, inside the instruction that
    /// is waiting, by stepping the machine until the call chain is back
    /// to where it was. The return address is the instruction after the
    /// read, which the program counter already holds, and the result is
    /// pushed onto the caller's stack and popped straight off again,
    /// which leaves the state exactly as the read found it. A routine
    /// that quits or restarts the game ends the input too, since there
    /// is nothing to continue.
    /// </remarks>
    private bool CallInterrupt(ushort routine)
    {
        var depth = State.FrameNumber;
        State.CallRoutine(routine, [], storeVariable: 0, returnAddress: State.ProgramCounter);

        // [zm 6.1.1.3] Saving is illegal inside an interrupt routine, so
        // the save opcodes need to know they are inside one.
        _interruptDepth++;
        try
        {
            while (!HasQuit && State.FrameNumber > depth)
            {
                Step();
            }
        }
        finally
        {
            _interruptDepth--;
        }

        if (HasQuit || State.FrameNumber < depth)
        {
            return true;
        }

        return State.Pop() != 0;
    }

    private void SoundEffect(Instruction instruction, ushort[] a)
    {
        // [zm op:sound_effect] number effect volume routine. With no
        // operands at all the interpreter is asked to bleep as if the
        // number were 1, and in any case not to halt.
        var number = a.Length > 0 ? a[0] : 1;

        if (number is 1 or 2)
        {
            // [zm 9.2] The bleeps, for which the other operands must be
            // omitted; when they are not, the bleep happens anyway.
            if (a.Length > 1)
            {
                ReportRuntimeError(instruction, "a bleep takes no operands beyond its number");
            }

            Sound.Bleep(number);
            return;
        }

        var effect = a.Length > 1 ? a[1] : 2;
        var routine = a.Length > 3 ? a[3] : (ushort)0;

        // [zm op:sound_effect] The low byte of the third operand is the
        // volume and the high byte the number of plays, 255 meaning
        // loudest possible and forever. [zm 9.3] Volume runs 1 to 8.
        var volume = a.Length > 2 ? a[2] & 0xFF : 255;
        if (volume == 255)
        {
            volume = 8;
        }

        var repeats = a.Length > 2 ? a[2] >> 8 : 1;
        if (repeats == 255)
        {
            repeats = -1;
        }
        else if (a.Length > 2 && Header.Version < ZMachineVersion.V5 && repeats != 0)
        {
            // [zm op:sound_effect] Before Version 5 the high byte must be
            // 0; [blorb 11.4] a Version 3 game's looping is the resource
            // file's business.
            ReportRuntimeError(instruction, $"repeats are not supported before Version 5, so {repeats} is ignored");
            repeats = 1;
        }
        else if (repeats == 0)
        {
            // [zm op:sound_effect] Zero repeats is illegal in Version 5
            // and taken as once, with a warning, as the standard suggests.
            if (a.Length > 2 && Header.Version >= ZMachineVersion.V5)
            {
                ReportRuntimeError(instruction, "repeats of 0 is illegal and is taken as once");
            }

            repeats = 1;
        }

        switch (effect)
        {
            case 1:
                // [zm 9.4.1]
                if (!Sound.Prepare(number))
                {
                    ReportRuntimeError(instruction, $"there is no sound {number}");
                }

                break;
            case 2:
                // [zm 9.4.2]
                if (!Sound.CanPlaySounds)
                {
                    ReportRuntimeError(instruction, "sound effects beyond a bleep cannot be played here");
                }
                else if (!Sound.Play(number, volume, repeats, routine))
                {
                    ReportRuntimeError(instruction, $"there is no sound {number}");
                }

                break;
            case 3:
                // [zm op:sound_effect] Sound 0 means all of them.
                if (number == 0)
                {
                    Sound.StopAll();
                }
                else
                {
                    Sound.Stop(number);
                }

                break;
            case 4:
                // [zm 9.4.5]
                if (number == 0)
                {
                    Sound.FinishAll();
                }
                else
                {
                    Sound.Finish(number);
                }

                break;
            default:
                // The Lurking Horror asks for effect 8 through a bug of
                // its own, and the standard's remarks say so.
                ReportRuntimeError(instruction, $"there is no sound effect {effect}");
                break;
        }
    }

    private void Save(Instruction instruction, ushort[] a)
    {
        // [zm op:save] With operands, the Version 5 form saves a region
        // of memory to an auxiliary file instead of the game.
        if (a.Length > 0)
        {
            SaveAuxiliary(instruction, a);
            return;
        }

        var saved = false;

        if (_interruptDepth > 0)
        {
            // [zm 6.1.1.3]
            ReportRuntimeError(instruction, "the game cannot be saved inside an interrupt routine");
        }
        else if (Files.OpenSaveFile() is { } file)
        {
            try
            {
                using (file)
                {
                    Quetzal.Write(State.Snapshot() with { ProgramCounter = SavePoint(instruction) }, Header, State.OriginalDynamicMemory, file);
                }

                saved = true;
            }
            catch (IOException e)
            {
                // [zm 7.6.4] Reported to the player, as best this can.
                ReportRuntimeError(instruction, $"the game could not be saved: {e.Message}");
            }
        }

        // [zm op:save] Versions 1 to 3 branch on success; from Version 4
        // the result is stored, 1 for success and 0 for failure.
        if (Header.Version <= ZMachineVersion.V3)
        {
            Branch(instruction, saved);
        }
        else
        {
            Store(instruction, saved ? (ushort)1 : (ushort)0);
        }
    }

    private void Restore(Instruction instruction, ushort[] a)
    {
        if (a.Length > 0)
        {
            RestoreAuxiliary(instruction, a);
            return;
        }

        if (Files.OpenRestoreFile() is { } file)
        {
            SavedState? state = null;
            try
            {
                using (file)
                {
                    // [zm 6.1.2.1] The file must have been saved from
                    // this story, which the reader checks.
                    state = Quetzal.Read(file, Header, State.OriginalDynamicMemory);
                }
            }
            catch (InvalidDataException e)
            {
                ReportRuntimeError(instruction, $"the saved game could not be restored: {e.Message}");
            }
            catch (IOException e)
            {
                ReportRuntimeError(instruction, $"the saved game could not be read: {e.Message}");
            }

            if (state is not null)
            {
                ResumeFrom(state);
                return;
            }
        }

        // [zm op:restore] Failure returns 0 from Version 4, and in
        // Versions 1 to 3 the branch is never made at all.
        if (Header.Version >= ZMachineVersion.V4)
        {
            Store(instruction, 0);
        }
    }

    private void SaveUndo(Instruction instruction)
    {
        if (_interruptDepth > 0)
        {
            // [zm 6.1.1.3]
            ReportRuntimeError(instruction, "the game cannot be saved inside an interrupt routine");
            Store(instruction, 0);
            return;
        }

        // [zm op:save_undo] Into the interpreter's own memory, with the
        // same result as save: 1 now, and 2 when the state comes back.
        Undo.Push(State.Snapshot() with { ProgramCounter = SavePoint(instruction) });
        Store(instruction, 1);
    }

    private void RestoreUndo(Instruction instruction)
    {
        // [zm op:restore_undo] Unspecified when nothing was saved, and
        // an interpreter may simply ignore it, which here is a failed
        // restore, 0.
        if (Undo.TryPop(out var state))
        {
            ResumeFrom(state);
        }
        else
        {
            Store(instruction, 0);
        }
    }

    /// <summary>
    /// [quetzal 5.8] Where a saved state resumes: at the store byte of
    /// the save instruction from Version 4, or its branch data before
    /// that, so that the save can be completed with a result of 2 when
    /// the state comes back.
    /// </summary>
    private int SavePoint(Instruction instruction) =>
        Header.Version <= ZMachineVersion.V3 ? instruction.Address + 1 : instruction.NextAddress - 1;

    /// <summary>
    /// Puts a saved state back and carries on from it as if the save
    /// that made it had just returned 2.
    /// </summary>
    /// <remarks>
    /// [zm 6.1.2] Everything is written back except Flags 2, which the
    /// state does itself. [zm 6.1.2.2] The header fields marked Rst are
    /// set again, since the game may have been saved by another
    /// interpreter on another screen, and [zm 8.6.1.3] in Version 3 the
    /// upper window collapses. [zm op:save] The result of 2 means "the
    /// game is being restored and is resuming execution again from
    /// here, the point where it was saved", which is done by finishing
    /// the save instruction's store or branch by hand.
    /// </remarks>
    private void ResumeFrom(SavedState state)
    {
        State.Restore(state);
        DescribeInterpreterInHeader();

        if (Header.Version == ZMachineVersion.V3)
        {
            Screen.SplitWindow(0);
        }

        var pc = State.ProgramCounter;
        if (Header.Version <= ZMachineVersion.V3)
        {
            var branch = InstructionDecoder.ReadBranch(Memory, ref pc);
            State.ProgramCounter = pc;
            if (branch.OnTrue)
            {
                if (branch.ReturnsFalse)
                {
                    State.Return(0);
                }
                else if (branch.ReturnsTrue)
                {
                    State.Return(1);
                }
                else
                {
                    State.ProgramCounter = branch.Target(pc);
                }
            }
        }
        else
        {
            var variable = Memory.ReadByte(pc);
            State.ProgramCounter = pc + 1;
            State.WriteVariable(variable, 2);
        }
    }

    private void SaveAuxiliary(Instruction instruction, ushort[] a)
    {
        // [zm op:save] table bytes name prompt: a region of memory, its
        // length, a suggested name, and whether to confirm it.
        var table = a[0];
        var length = a.Length > 1 ? a[1] : (ushort)0;
        var name = AuxiliaryFileName.Sanitize(a.Length > 2 ? ReadStringWithLength(a[2]) : "");
        var prompt = a.Length <= 3 || a[3] != 0;
        var saved = false;

        if (table + length > Memory.Length)
        {
            ReportRuntimeError(instruction, $"{length} bytes from {table:X4} run past the end of memory");
        }
        else if (Files.OpenAuxiliaryFile(name, forWriting: true, prompt) is { } file)
        {
            try
            {
                using (file)
                {
                    file.Write(Memory.Slice(table, length));
                }

                saved = true;
            }
            catch (IOException e)
            {
                ReportRuntimeError(instruction, $"the file {name} could not be written: {e.Message}");
            }
        }

        Store(instruction, saved ? (ushort)1 : (ushort)0);
    }

    private void RestoreAuxiliary(Instruction instruction, ushort[] a)
    {
        // [zm op:restore] With operands, returns the number of bytes
        // loaded into the table, or 0 on failure.
        var table = a[0];
        var length = a.Length > 1 ? a[1] : (ushort)0;
        var name = AuxiliaryFileName.Sanitize(a.Length > 2 ? ReadStringWithLength(a[2]) : "");
        var prompt = a.Length <= 3 || a[3] != 0;
        var loaded = 0;

        if (Files.OpenAuxiliaryFile(name, forWriting: false, prompt) is { } file)
        {
            try
            {
                using (file)
                {
                    var buffer = new byte[length];
                    loaded = file.ReadAtLeast(buffer, length, throwOnEndOfStream: false);
                    for (var i = 0; i < loaded; i++)
                    {
                        State.WriteByte(table + i, buffer[i]);
                    }
                }
            }
            catch (IOException e)
            {
                ReportRuntimeError(instruction, $"the file {name} could not be read: {e.Message}");
                loaded = 0;
            }
        }

        Store(instruction, (ushort)loaded);
    }

    // [zm op:save] A name is an array of ASCII characters preceded by a
    // byte giving their number.
    private string ReadStringWithLength(int address)
    {
        int length = Memory.ReadByte(address);
        var characters = new char[length];
        for (var i = 0; i < length; i++)
        {
            characters[i] = (char)Memory.ReadByte(address + 1 + i);
        }

        return new string(characters);
    }

    // [zm 10.2] From the file of commands while there is one, and from
    // the keyboard when it runs out, so that a script can hand the game
    // back to the player.
    private LineInput ReadLine(LineInputRequest request)
    {
        LineInput line;

        if (_commandFile is not null && _commandFile.ReadLine(request) is { } replayed)
        {
            // [zm 7.1.1.1] Input is echoed to the screen. The keyboard
            // does that as the player types, but nobody typed this, so
            // the interpreter shows what the file said.
            foreach (var code in replayed.Text)
            {
                Screen.Print(code);
            }

            if (replayed.Terminator == Zscii.Newline)
            {
                Screen.Print(Zscii.Newline);
            }

            line = replayed;
        }
        else
        {
            CloseCommandFile();
            line = Input.ReadLine(request);

            // [zm 7.1.2.3] Stream 4 records what the player typed, and
            // nothing that was played back to them.
            Streams.RecordCommand(line.Text, line.Terminator);
        }

        // [zm 7.1.1.1] The transcript gets the command either way.
        Streams.EchoInput(line.Text, line.Terminator);
        return line;
    }

    private ushort ReadKey(InputTimer? timer)
    {
        if (_commandFile is not null && _commandFile.ReadKey() is { } replayed)
        {
            return replayed;
        }

        CloseCommandFile();
        var key = Input.ReadKey(timer);
        Streams.RecordKey(key);
        return key;
    }

    private void SelectOutputStream(Instruction instruction, ushort[] a)
    {
        // [zm op:output_stream] A stream number, and for stream 3 the
        // table to write into. The Version 6 width operand is not used.
        var number = Signed(a[0]);
        var table = a.Length > 1 ? a[1] : (ushort)0;

        switch (Streams.Select(number, table))
        {
            case StreamSelection.Unknown:
                ReportRuntimeError(instruction, $"there is no output stream {Math.Abs(number)}");
                break;
            case StreamSelection.TooDeep:
                // [zm 7.1.2.1.1] A seventeenth nesting of stream 3 halts
                // the interpreter with an error message.
                throw Fail(instruction, $"Output stream 3 is already nested {OutputStreams.MaxMemoryDepth} deep.");
            case StreamSelection.Unavailable:
                // [zm 7.6.5.2] A warning to the player, and otherwise
                // nothing.
                ReportRuntimeError(instruction, number == 2 ? "no transcript file is available" : "no file is available to record commands");
                break;
            default:
                break;
        }
    }

    private void SelectInputStream(Instruction instruction, ushort number)
    {
        // [zm op:input_stream] and [zm 10.2] There are two: 0 for the
        // keyboard and 1 for a file of commands.
        switch (number)
        {
            case 0:
                CloseCommandFile();
                break;
            case 1:
                // [zm 10.2.3] The frontend chooses the file however it
                // likes, and if it chooses none the keyboard stays.
                if (_commandFile is null && Files.OpenCommandFile() is { } commands)
                {
                    PlayCommands(commands);
                }

                break;
            default:
                ReportRuntimeError(instruction, $"there is no input stream {number}");
                break;
        }
    }

    private void CloseCommandFile()
    {
        _commandFile?.Close();
        _commandFile = null;
    }

    private void Tokenise(ushort[] a)
    {
        var text = a[0];
        var parse = a[1];
        var dictionaryAddress = a.Length > 2 ? a[2] : 0;
        var keepUnknownSlots = a.Length > 3 && a[3] != 0;

        // [zm op:tokenise] Lexical analysis of a text buffer as the read
        // opcode lays it out, which is the Version 5 layout, since the
        // opcode exists only from Version 5: a count in byte 1 and the
        // characters from byte 2.
        int length = Memory.ReadByte(text + 1);
        var characters = Memory.Slice(text + 2, length).ToArray();

        // [zm op:tokenise] The game's own dictionary unless another is
        // named. A user dictionary is read afresh each time, since
        // [zm 13.6] the point of one is that the game can alter it in
        // play, for instance when the player names things.
        var dictionary = dictionaryAddress == 0
            ? Dictionary
            : new DictionaryTable(Memory, dictionaryAddress, Text, Encoder);

        WriteParseTable(parse, characters, 2, dictionary, keepUnknownSlots);
    }

    /// <summary>
    /// [zm 13.6.3] and [zm op:read] Writes the parse table for a text.
    /// </summary>
    /// <param name="parse">The parse buffer's address.</param>
    /// <param name="text">The characters to analyze.</param>
    /// <param name="textOffset">
    /// Where the first character sits in the text buffer, since the table
    /// records positions relative to the buffer's start.
    /// </param>
    /// <param name="dictionary">The dictionary to look words up in.</param>
    /// <param name="keepUnknownSlots">
    /// [zm op:tokenise] Whether a word not in the dictionary leaves its
    /// slot untouched instead of writing 0, so that several tokenise
    /// calls can fill in a table between them.
    /// </param>
    /// <remarks>
    /// [zm op:read] Byte 0 of the parse buffer holds the most words it
    /// can take. The count found goes in byte 1, and then a 4-byte block
    /// for each word: the byte address of its dictionary entry or 0, its
    /// length in letters, and its position in the text buffer. Words past
    /// the maximum are dropped rather than written beyond the buffer. A
    /// word whose slot is kept still counts, as Frotz has it, so that the
    /// slots line up with the words.
    /// </remarks>
    private void WriteParseTable(ushort parse, byte[] text, int textOffset, DictionaryTable dictionary, bool keepUnknownSlots)
    {
        var maxWords = Memory.ReadByte(parse);
        var tokens = Lexer.Analyze(text, dictionary);
        var count = Math.Min(tokens.Count, maxWords);

        for (var i = 0; i < count; i++)
        {
            var token = tokens[i];
            if (token.DictionaryAddress == 0 && keepUnknownSlots)
            {
                continue;
            }

            var block = parse + 2 + (4 * i);
            State.WriteWord(block, (ushort)token.DictionaryAddress);
            State.WriteByte(block + 2, (byte)token.Length);
            State.WriteByte(block + 3, (byte)(textOffset + token.Start));
        }

        State.WriteByte(parse + 1, (byte)count);
    }

    /// <summary>
    /// Sets the header fields that describe this interpreter, which the
    /// game reads to learn what it can ask for. Done at the start and
    /// [zm 6.1.3] again after a restart, since these are the fields
    /// [zm 11.1] marks Rst.
    /// </summary>
    private void DescribeInterpreterInHeader()
    {
        var screen = Screen.Screen;
        var can = screen.Capabilities;

        if (Header.Version <= ZMachineVersion.V3)
        {
            // [zm 8.2] Bit 4 is set when no status line can be shown,
            // [zm 8.6.1.2] bit 5 when an upper window can be, and
            // [zm 11.1.4] bit 6 when the default font is proportional.
            var flags = Header.Flags1Versions1To3;
            flags = Set(flags, Flags1Versions1To3.StatusLineUnavailable, !can.HasFlag(ScreenCapabilities.StatusLine));
            flags = Set(flags, Flags1Versions1To3.ScreenSplittingAvailable, can.HasFlag(ScreenCapabilities.UpperWindow));
            flags = Set(flags, Flags1Versions1To3.VariablePitchFontDefault, can.HasFlag(ScreenCapabilities.ProportionalFont));
            Header.Flags1Versions1To3 = flags;
        }
        else
        {
            // [zm 8.3.2] and [zm 8.3.3] Bit 0 for colors, [zm 8.7.1.1]
            // bits 2 and 3 for bold and italic, bit 4 for fixed pitch,
            // and [zm 10.5.3] bit 7 for timed input, which depends on
            // the input source rather than the screen.
            var flags = Header.Flags1FromVersion4;
            flags = Set(flags, Flags1FromVersion4.ColorsAvailable, Header.Version >= ZMachineVersion.V5 && can.HasFlag(ScreenCapabilities.Colors));
            flags = Set(flags, Flags1FromVersion4.BoldfaceAvailable, can.HasFlag(ScreenCapabilities.Bold));
            flags = Set(flags, Flags1FromVersion4.ItalicAvailable, can.HasFlag(ScreenCapabilities.Italic));
            flags = Set(flags, Flags1FromVersion4.FixedSpaceAvailable, can.HasFlag(ScreenCapabilities.FixedPitch));

            // [zm 9.1.1] In Version 6, bit 5 says whether sound effects
            // beyond a bleep can be played.
            if (Header.Version == ZMachineVersion.V6)
            {
                flags = Set(flags, Flags1FromVersion4.SoundEffectsAvailable, Sound.CanPlaySounds);
            }
            flags = Set(flags, Flags1FromVersion4.TimedInputAvailable, Input.SupportsTimedInput);
            Header.Flags1FromVersion4 = flags;

            // [zm 11.1.3] The interpreter number most suitable for the
            // machine, which for anything modern is the IBM PC, as
            // Frotz also says; and [zm 11.1.3.1] a letter for the
            // interpreter version.
            Header.InterpreterNumber = InterpreterNumber.IbmPc;
            Header.InterpreterVersion = (byte)'R';

            // [zm 8.4] The screen's height and width.
            Header.ScreenHeightLines = (byte)Math.Min(screen.Height, 254);
            Header.ScreenWidthCharacters = (byte)Math.Min(screen.Width, 255);
        }

        if (Header.Version >= ZMachineVersion.V5)
        {
            // [zm 8.4.2] Units are characters here, as the remarks on
            // section 8 recommend, so [zm 8.4.3] the size in units is
            // the size in characters and [zm 8.1.1] a font is 1 by 1.
            Header.ScreenWidthUnits = (ushort)Math.Min(screen.Width, 255);
            Header.ScreenHeightUnits = (ushort)Math.Min(screen.Height, 254);
            Header.FontWidthUnits = 1;
            Header.FontHeightUnits = 1;

            // [zm 8.3.3] The default colors, whether or not [zm 8.3.2]
            // colors can be shown, since black and white are always a
            // truthful pair.
            Header.DefaultBackgroundColor = (byte)screen.DefaultBackground;
            Header.DefaultForegroundColor = (byte)screen.DefaultForeground;
        }

        // [zm 11.1.5] The revision of the standard obeyed. Stories
        // check this before using the opcodes that Standards 1.0 and
        // 1.1 added, all of which exist here, and that is what the
        // number decides in practice. Saved games and sound are still
        // to come, and the roadmap knows it.
        Header.StandardRevisionMajor = 1;
        Header.StandardRevisionMinor = 1;

        // [zm 11.1.2] The interpreter clears the Flags 2 bits for what
        // it cannot give: [zm 8.1.5.1] pictures and the character
        // graphics font in Version 5 (bit 3), [zm 10.3.1.1] the mouse
        // (bit 5), [zm 9.1.2] sound effects beyond a bleep (bit 7), and
        // [zm 10.4.1.1] menus (bit 8). [zm 6.1.4] Undo is provided, so
        // bit 4 stays as the game set it.
        var flags2 = Header.Flags2 & ~(Flags2.WantsMouse | Flags2.WantsMenus);
        if (!can.HasFlag(ScreenCapabilities.CharacterGraphicsFont))
        {
            flags2 &= ~Flags2.WantsPictures;
        }

        if (!Sound.CanPlaySounds)
        {
            flags2 &= ~Flags2.WantsSoundEffects;
        }

        Header.Flags2 = flags2;
    }

    private static Flags1Versions1To3 Set(Flags1Versions1To3 flags, Flags1Versions1To3 bit, bool on) =>
        on ? flags | bit : flags & ~bit;

    private static Flags1FromVersion4 Set(Flags1FromVersion4 flags, Flags1FromVersion4 bit, bool on) =>
        on ? flags | bit : flags & ~bit;

    private void EncodeText(ushort text, ushort length, ushort from, ushort codedText)
    {
        // [zm op:encode_text] Encode length characters of the buffer,
        // starting at from, as a dictionary word at coded-text.
        var encoded = Encoder.EncodeWord(Memory.Slice(text + from, length));

        for (var i = 0; i < encoded.Length; i++)
        {
            State.WriteByte(codedText + i, encoded[i]);
        }
    }

    private void RandomNumber(Instruction instruction, ushort rangeWord)
    {
        // [zm op:random] Positive: a value from 1 to range. Negative: seed
        // with that value and return 0. Zero: reseed as randomly as
        // possible, and return 0.
        var range = Signed(rangeWord);

        if (range > 0)
        {
            Store(instruction, Random.Next(range));
        }
        else
        {
            if (range < 0)
            {
                Random.Seed(-range);
            }
            else
            {
                Random.SeedRandomly();
            }

            Store(instruction, 0);
        }
    }

    private void PrintString(int address)
    {
        var zscii = new List<ushort>();
        Text.DecodeZscii(address, zscii);

        foreach (var code in zscii)
        {
            Output.Print(code);
        }
    }

    private void PrintObjectName(Instruction instruction, ushort obj)
    {
        // [zm op:print_obj] The short name from the object's property
        // table. An invalid object should halt the interpreter, and does
        // at the fatal error level; below that, object 0 prints nothing.
        if (!ObjectExists(instruction, obj))
        {
            return;
        }

        // [zm 12.4] A length byte, then the name; no name at all if the
        // length is 0.
        var table = Objects.PropertyTableAddress(obj);
        if (Memory.ReadByte(table) != 0)
        {
            PrintString(table + 1);
        }
    }

    // [zm 15.3] An object operand of 0 is undefined behavior, and [zm A]
    // the most common bug in released games. Here it is an error at the
    // current level, and when that is not fatal the operation is skipped
    // and the caller stores or branches as if the object had nothing:
    // no parent, no children, no attributes, no properties.
    private bool ObjectExists(Instruction instruction, ushort obj)
    {
        if (obj != 0)
        {
            return true;
        }

        ReportRuntimeError(instruction, "object 0 is not an object");
        return false;
    }

    private void ReportRuntimeError(Instruction instruction, string message)
    {
        switch (ErrorLevel)
        {
            case ErrorLevel.Fatal:
                throw Fail(instruction, message);
            case ErrorLevel.Never:
                return;
            case ErrorLevel.ReportOnce when !_reportedKinds.Add(instruction.Name + ": " + message):
                return;
        }

        _runtimeErrors.Add($"At {instruction.Address:X4}, {instruction}: {message}");
    }

    private void PrintNumber(short value)
    {
        foreach (var digit in value.ToString(CultureInfo.InvariantCulture))
        {
            Output.Print(digit);
        }
    }

    private static void RequireNonZeroDivisor(Instruction instruction, ushort divisor)
    {
        if (divisor == 0)
        {
            throw Fail(instruction, "Division by zero.");
        }
    }

    private static InvalidOperationException Fail(Instruction instruction, string message) =>
        new($"At {instruction.Address:X4}, {instruction}: {message}");

    // [zm 2.2] The top bit is the sign bit.
    private static short Signed(ushort value) => (short)value;

    // [zm 2.3.2] Out-of-range results reduced modulo $10000.
    private static ushort Unsigned(int value) => (ushort)value;
}

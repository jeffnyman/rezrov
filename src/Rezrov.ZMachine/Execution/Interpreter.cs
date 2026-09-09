using System.Globalization;
using Rezrov.ZMachine.Instructions;
using Rezrov.ZMachine.Objects;
using Rezrov.ZMachine.Text;

namespace Rezrov.ZMachine.Execution;

/// <summary>
/// Runs a story file: fetches the instruction at the program counter,
/// evaluates its operands, carries it out, and repeats.
/// </summary>
/// <remarks>
/// Everything before this was a data structure. This is the loop that
/// uses them, and the opcodes of section 15 are implemented here, each
/// one cited. Opcodes that need input, the screen model, sound, or
/// saved games are not implemented yet and say so when reached, rather
/// than doing something approximate.
///
/// The loop sets the program counter to the next instruction before
/// carrying out the current one. The remarks on section 4 explain why
/// that is the natural arrangement: a branch or jump is then simply an
/// adjustment to where execution was already going to continue.
/// </remarks>
public sealed class Interpreter
{
    public Interpreter(ZMemory memory, IOutput output, RandomGenerator? random = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(output);

        Memory = memory;
        Output = output;
        Header = new StoryHeader(memory);
        Text = new ZTextDecoder(memory, Header);
        Encoder = ZTextEncoder.ForStory(Header, memory);
        Objects = new ObjectTable(memory, Header, Text);
        Decoder = new InstructionDecoder(memory, Header, Text);
        State = new GameState(memory, Header);
        Random = random ?? new RandomGenerator();
    }

    public ZMemory Memory { get; }

    public IOutput Output { get; }

    public StoryHeader Header { get; }

    public ZTextDecoder Text { get; }

    public ZTextEncoder Encoder { get; }

    public ObjectTable Objects { get; }

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

    /// <summary>
    /// Carries out the instruction at the program counter and returns it.
    /// </summary>
    public Instruction Step()
    {
        if (HasQuit)
        {
            throw new InvalidOperationException("The game has quit.");
        }

        var instruction = Decoder.Decode(State.ProgramCounter);
        State.ProgramCounter = instruction.NextAddress;

        // [zm 4.5.2] Operands are evaluated first to last, which matters
        // when more than one of them is the stack.
        var arguments = new ushort[instruction.Operands.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = Evaluate(instruction.Operands[i]);
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
    private ushort Evaluate(Operand operand) =>
        operand.Type == OperandType.Variable ? State.ReadVariable(operand.Value) : operand.Value;

    private void Execute(Instruction instruction, ushort[] a)
    {
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
                State.WriteVariableInPlace(a[0], Unsigned(Signed(State.ReadVariableInPlace(a[0])) + 1));
                break;
            case Opcode.Dec:
                State.WriteVariableInPlace(a[0], Unsigned(Signed(State.ReadVariableInPlace(a[0])) - 1));
                break;
            case Opcode.IncChk:
            {
                // [zm op:inc_chk] Increment, then branch if now greater.
                var value = Unsigned(Signed(State.ReadVariableInPlace(a[0])) + 1);
                State.WriteVariableInPlace(a[0], value);
                Branch(instruction, Signed(value) > Signed(a[1]));
                break;
            }
            case Opcode.DecChk:
            {
                // [zm op:dec_chk] Decrement, then branch if now less.
                var value = Unsigned(Signed(State.ReadVariableInPlace(a[0])) - 1);
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
                Store(instruction, State.ReadVariableInPlace(a[0]));
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
                        : State.Pop());
                }
                else
                {
                    // [zm op:pull] Elsewhere the operand names the variable
                    // to pull into, in place if it is the stack pointer.
                    var value = State.Pop();
                    State.WriteVariableInPlace(a[0], value);
                }

                break;
            case Opcode.Pop:
                // [zm op:pop] Throw away the top of the stack.
                State.Pop();
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
                State.Return(State.Pop());
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
                // starting routine cannot return.
                HasQuit = true;
                break;
            case Opcode.Restart:
                // [zm op:restart] Everything back to the start except the
                // transcript and fixed-pitch bits of Flags 2, which the
                // state preserves.
                State.Restart();
                break;
            case Opcode.Nop:
                // [zm op:nop] Never used by any Infocom game, apparently.
                break;
            case Opcode.ShowStatus:
                // [zm op:show_status] Redraws the Version 3 status line,
                // and is to be treated as a nop where it appears by
                // accident in later versions. There is no status line
                // until the screen model exists, so for now it is a nop
                // everywhere.
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
                        State.Pop();
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
            case Opcode.Sread:
            case Opcode.Aread:
            case Opcode.ReadChar:
            case Opcode.Tokenise:
            case Opcode.InputStream:
                throw new NotSupportedException($"{instruction.Name} needs the input side of section 10, which is not implemented yet.");
            case Opcode.SplitWindow:
            case Opcode.SetWindow:
            case Opcode.EraseWindow:
            case Opcode.EraseLine:
            case Opcode.SetCursor:
            case Opcode.GetCursor:
            case Opcode.SetTextStyle:
            case Opcode.BufferMode:
            case Opcode.SetColour:
            case Opcode.SetTrueColour:
            case Opcode.SetFont:
            case Opcode.PrintTable:
            case Opcode.PrintUnicode:
            case Opcode.CheckUnicode:
                throw new NotSupportedException($"{instruction.Name} needs the screen model of section 8, which is not implemented yet.");
            case Opcode.OutputStream:
                throw new NotSupportedException($"{instruction.Name} needs the output streams of section 7, which are not implemented yet.");
            case Opcode.SoundEffect:
                throw new NotSupportedException($"{instruction.Name} needs the sound effects of section 9, which are not implemented yet.");
            case Opcode.Save:
            case Opcode.Restore:
            case Opcode.SaveUndo:
            case Opcode.RestoreUndo:
                throw new NotSupportedException($"{instruction.Name} needs saved games, which are not implemented yet.");
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

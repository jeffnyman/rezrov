using Rezrov.AaMachine.Instructions;

namespace Rezrov.AaMachine.Execution;

/// <summary>
/// The execution loop, and everything that is not output or input.
/// </summary>
public sealed partial class Machine
{
    // Runs until the story stops or wants something. A runtime error
    // does not stop it: the machine starts again with the number of
    // what went wrong in R00, and the story can say so itself.
    private AaStatus Run()
    {
        while (true)
        {
            try
            {
                return Step();
            }
            catch (RuntimeError error)
            {
                if (_spc < Spacing.Line)
                {
                    _output.Newline();
                }

                _runtimeErrors.Add($"aam: {Explain(error.Code)}");

                ClearOutputState();
                Reset(AaValue.Number(error.Code));
            }
        }
    }

    private AaStatus Step()
    {
        while (true)
        {
            var instruction = _decoded[_inst]
                ?? throw new AaMachineException($"There is no instruction at {_inst:x4}.");

            var ops = instruction.OperandSpan;

            _inst = instruction.NextAddress;

            switch (instruction.Opcode)
            {
                case Opcode.Nop:
                    break;

                case Opcode.Fail:
                    Fail();
                    break;

                case Opcode.SetCont:
                    _cont = ops[0].Number;
                    break;

                case Opcode.Proceed:
                    // [aam opcode] A simple cutting choice point means
                    // the query it belongs to wanted one answer only.
                    if (_sim < 0x8000)
                    {
                        _cho = _sim;
                    }

                    _inst = _cont;
                    break;

                case Opcode.Jmp:
                    _inst = ops[0].Number;
                    break;

                case Opcode.JmpMulti:
                    _sim = 0xffff;
                    _inst = ops[0].Number;
                    break;

                case Opcode.JmplMulti:
                    _cont = _inst;
                    _sim = 0xffff;
                    _inst = ops[0].Number;
                    break;

                case Opcode.JmpSimple:
                    _sim = _cho;
                    _inst = ops[0].Number;
                    break;

                case Opcode.JmplSimple:
                    _cont = _inst;
                    _sim = _cho;
                    _inst = ops[0].Number;
                    break;

                case Opcode.JmpTail:
                    if (_sim >= 0x8000)
                    {
                        _sim = _cho;
                    }

                    _inst = ops[0].Number;
                    break;

                case Opcode.Tail:
                    if (_sim >= 0x8000)
                    {
                        _sim = _cho;
                    }

                    break;

                case Opcode.PushEnv:
                    PushEnvironment(ops.Length > 0 ? Value(ops[0]) : 0);
                    break;

                case Opcode.PopEnv:
                    _cont = (_heap[_env + 2] << 16) | _heap[_env + 3];
                    _sim = _heap[_env + 1];
                    _env = _heap[_env];
                    break;

                case Opcode.PopEnvProceed:
                    _inst = (_heap[_env + 2] << 16) | _heap[_env + 3];

                    if (_heap[_env + 1] < 0x8000)
                    {
                        _cho = _heap[_env + 1];
                    }

                    _env = _heap[_env];
                    break;

                case Opcode.PushChoice:
                    PushChoice(ops.Length > 1 ? Value(ops[0]) : 0, ops[^1].Number);
                    break;

                case Opcode.PopChoice:
                    PopChoice(ops.Length > 0 ? Value(ops[0]) : 0);
                    _cho = _heap[_cho + 6];
                    break;

                case Opcode.PopPushChoice:
                    // [aam opcode] The frame stays where it is and is
                    // given a new answer to try next.
                    _heap[_cho + 4] = (ushort)(ops[^1].Number >> 16);
                    _heap[_cho + 5] = (ushort)(ops[^1].Number & 0xffff);
                    PopChoice(ops.Length > 1 ? Value(ops[0]) : 0);
                    break;

                case Opcode.CutChoice:
                    _cho = _heap[_cho + 6];
                    break;

                case Opcode.GetCho:
                    Store(ops[0], (ushort)_cho);
                    break;

                case Opcode.SetCho:
                    _cho = Value(ops[0]);
                    break;

                case Opcode.Assign:
                    Store(ops[1], Value(ops[0]));
                    break;

                case Opcode.MakeVar:
                {
                    var address = Allocate(1);

                    _heap[address] = AaValue.Null;
                    Store(ops[0], AaValue.Reference(address));
                    break;
                }

                case Opcode.MakePair:
                    Pair(ops);
                    break;

                case Opcode.AuxPushVal:
                    PushSerialized(Value(ops[0]));
                    break;

                case Opcode.AuxPushRaw:
                    PushAux(ops.Length > 0 ? Value(ops[0]) : AaValue.Null);
                    break;

                case Opcode.AuxPopVal:
                    Store(ops[0], PopSerialized());
                    break;

                case Opcode.AuxPopList:
                    Store(ops[0], PopSerializedList());
                    break;

                case Opcode.AuxPopListChk:
                    if (!PopListContains(Deref(Value(ops[0]))))
                    {
                        Fail();
                    }

                    break;

                case Opcode.AuxPopListMatch:
                    if (!PopListMatches(Deref(Value(ops[0]))))
                    {
                        Fail();
                    }

                    break;

                case Opcode.SplitList:
                    Store(ops[2], SplitList(Value(ops[0]), Value(ops[1])));
                    break;

                case Opcode.Stop:
                    _cho = _stoppableChoice;
                    Fail();
                    break;

                case Opcode.PushStop:
                    if (_auxTop + 2 > _trail)
                    {
                        throw new RuntimeError(2);
                    }

                    _aux[_auxTop++] = (ushort)_stoppableChoice;
                    _aux[_auxTop++] = (ushort)_stoppableAux;
                    _stoppableAux = _auxTop;
                    PushChoice(0, ops[0].Number);
                    _stoppableChoice = _cho;
                    break;

                case Opcode.PopStop:
                    _auxTop = _stoppableAux;
                    _stoppableAux = _aux[--_auxTop];
                    _stoppableChoice = _aux[--_auxTop];
                    break;

                case Opcode.SplitWord:
                    if (SplitWord(Deref(Value(ops[0]))) is { } split)
                    {
                        Store(ops[1], split);
                    }
                    else
                    {
                        Fail();
                    }

                    break;

                case Opcode.JoinWords:
                    if (JoinWords(Deref(Value(ops[0]))) is { } joined)
                    {
                        Store(ops[1], joined);
                    }
                    else
                    {
                        Fail();
                    }

                    break;

                case Opcode.LoadWord:
                    Store(ops[^1], ReadField(Value(ops[^2]), Subject(ops)));
                    break;

                case Opcode.LoadByte:
                {
                    // [aam opcode] Two bytes to a word, the higher one
                    // first, so an even index is the top half.
                    var index = Value(ops[^2]);
                    var word = ReadField(index / 2, Subject(ops));

                    Store(ops[^1], (ushort)((word >> ((index & 1) != 0 ? 0 : 8)) & 0xff));
                    break;
                }

                case Opcode.LoadVal:
                {
                    var value = LongTerm(ReadField(Value(ops[^2]), Subject(ops)));

                    if (value == AaValue.Null)
                    {
                        Fail();
                    }
                    else
                    {
                        Store(ops[^1], value);
                    }

                    break;
                }

                case Opcode.StoreWord:
                    _ram[FieldAddress(Value(ops[^2]), Subject(ops))] = Value(ops[^1]);
                    break;

                case Opcode.StoreByte:
                {
                    var index = Value(ops[^2]);
                    var address = FieldAddress(index / 2, Subject(ops));
                    var value = Value(ops[^1]);

                    _ram[address] = (index & 1) != 0
                        ? (ushort)((_ram[address] & 0xff00) | (value & 0xff))
                        : (ushort)((_ram[address] & 0x00ff) | (value << 8));
                    break;
                }

                case Opcode.StoreVal:
                    StoreLongTermField(ops);
                    break;

                case Opcode.SetFlag:
                {
                    var flag = Value(ops[^1]);

                    _ram[FieldAddress(flag / BitsPerWord, Subject(ops))] |= FlagBit(flag);
                    break;
                }

                case Opcode.ResetFlag:
                {
                    var flag = Value(ops[^1]);
                    var subject = Subject(ops);

                    // Only an object, or the globals, has flags to
                    // reset; anything else is quietly left alone.
                    if (subject == AaValue.Null || AaValue.Tag(subject) == AaTag.Thing)
                    {
                        _ram[FieldAddress(flag / BitsPerWord, subject)] &= (ushort)~FlagBit(flag);
                    }

                    break;
                }

                case Opcode.Unlink:
                    Unlink(FieldAddress(Value(ops[^3]), Subject(ops)), Value(ops[^2]), Value(ops[^1]));
                    break;

                case Opcode.SetParent:
                    SetParent(Deref(Value(ops[0])), Deref(Value(ops[1])));
                    break;

                case Opcode.IfRawEq:
                    Branch((ops.Length > 2 ? Value(ops[0]) : AaValue.Null) == Value(ops[^2]), ops[^1]);
                    break;

                case Opcode.IfnRawEq:
                    Branch((ops.Length > 2 ? Value(ops[0]) : AaValue.Null) != Value(ops[^2]), ops[^1]);
                    break;

                case Opcode.IfBound:
                    Branch(!AaValue.IsReference(Deref(Value(ops[0]))), ops[1]);
                    break;

                case Opcode.IfnBound:
                    Branch(AaValue.IsReference(Deref(Value(ops[0]))), ops[1]);
                    break;

                case Opcode.IfEmpty:
                    Branch(Deref(Value(ops[0])) == AaValue.Empty, ops[1]);
                    break;

                case Opcode.IfnEmpty:
                    Branch(Deref(Value(ops[0])) != AaValue.Empty, ops[1]);
                    break;

                case Opcode.IfNum:
                    Branch(AaValue.Tag(Deref(Value(ops[0]))) == AaTag.Number, ops[1]);
                    break;

                case Opcode.IfnNum:
                    Branch(AaValue.Tag(Deref(Value(ops[0]))) != AaTag.Number, ops[1]);
                    break;

                case Opcode.IfPair:
                    Branch(AaValue.IsPair(Deref(Value(ops[0]))), ops[1]);
                    break;

                case Opcode.IfnPair:
                    Branch(!AaValue.IsPair(Deref(Value(ops[0]))), ops[1]);
                    break;

                case Opcode.IfObj:
                    Branch(AaValue.Tag(Deref(Value(ops[0]))) == AaTag.Thing, ops[1]);
                    break;

                case Opcode.IfnObj:
                    Branch(AaValue.Tag(Deref(Value(ops[0]))) != AaTag.Thing, ops[1]);
                    break;

                case Opcode.IfWord:
                    Branch(IsWord(Deref(Value(ops[0]))), ops[1]);
                    break;

                case Opcode.IfnWord:
                    Branch(!IsWord(Deref(Value(ops[0]))), ops[1]);
                    break;

                case Opcode.IfUword:
                    Branch(IsUnknownWord(Deref(Value(ops[0]))), ops[1]);
                    break;

                case Opcode.IfnUword:
                    Branch(!IsUnknownWord(Deref(Value(ops[0]))), ops[1]);
                    break;

                case Opcode.IfUnify:
                    Branch(WouldUnify(Value(ops[0]), Value(ops[1])), ops[2]);
                    break;

                case Opcode.IfnUnify:
                    Branch(!WouldUnify(Value(ops[0]), Value(ops[1])), ops[2]);
                    break;

                case Opcode.IfGt:
                    Branch(GreaterThan(Value(ops[0]), Value(ops[1])), ops[2]);
                    break;

                case Opcode.IfnGt:
                    Branch(!GreaterThan(Value(ops[0]), Value(ops[1])), ops[2]);
                    break;

                case Opcode.IfEq:
                    Branch(Value(ops[0]) == Deref(Value(ops[1])), ops[2]);
                    break;

                case Opcode.IfnEq:
                    Branch(Value(ops[0]) != Deref(Value(ops[1])), ops[2]);
                    break;

                case Opcode.IfMemEq:
                    Branch(ReadField(Value(ops[^3]), Subject(ops)) == Value(ops[^2]), ops[^1]);
                    break;

                case Opcode.IfnMemEq:
                    Branch(ReadField(Value(ops[^3]), Subject(ops)) != Value(ops[^2]), ops[^1]);
                    break;

                case Opcode.IfFlag:
                {
                    var flag = Value(ops[^2]);

                    Branch((ReadField(flag / BitsPerWord, Subject(ops)) & FlagBit(flag)) != 0, ops[^1]);
                    break;
                }

                case Opcode.IfnFlag:
                {
                    var flag = Value(ops[^2]);

                    Branch((ReadField(flag / BitsPerWord, Subject(ops)) & FlagBit(flag)) == 0, ops[^1]);
                    break;
                }

                case Opcode.IfCwl:
                    Branch(_cwl != 0, ops[0]);
                    break;

                case Opcode.IfnCwl:
                    Branch(_cwl == 0, ops[0]);
                    break;

                case Opcode.AddRaw:
                    Store(ops[2], (ushort)(Value(ops[0]) + Value(ops[1])));
                    break;

                case Opcode.SubRaw:
                    Store(ops[2], (ushort)(Value(ops[0]) - Value(ops[1])));
                    break;

                case Opcode.IncRaw:
                    Store(ops[1], (ushort)(Value(ops[0]) + 1));
                    break;

                case Opcode.DecRaw:
                    Store(ops[1], (ushort)(Value(ops[0]) - 1));
                    break;

                case Opcode.RandRaw:
                    Store(ops[1], (ushort)(_random.Next() % (Value(ops[0]) + 1)));
                    break;

                case Opcode.AddNum:
                    Arithmetic(ops, static (a, b) => a + b);
                    break;

                case Opcode.SubNum:
                    Arithmetic(ops, static (a, b) => a - b);
                    break;

                case Opcode.MulNum:
                    // [aam opcode] Multiplication alone wraps rather
                    // than failing, since it is the one that overflows
                    // by accident.
                    Arithmetic(ops, static (a, b) => (a * b) & AaValue.MaxNumber);
                    break;

                case Opcode.DivNum:
                    Arithmetic(ops, static (a, b) => b == 0 ? null : a / b);
                    break;

                case Opcode.ModNum:
                    Arithmetic(ops, static (a, b) => b == 0 ? null : a % b);
                    break;

                case Opcode.IncNum:
                    Increment(ops, 1);
                    break;

                case Opcode.DecNum:
                    Increment(ops, -1);
                    break;

                case Opcode.RandNum:
                    RandomNumber(ops);
                    break;

                case Opcode.SetIdx:
                {
                    var index = Deref(Value(ops[0]));

                    // [aam opcode] An unrecognized word is indexed by
                    // the part of it that is recognized.
                    _reg[0x3f] = AaValue.IsExtDict(index) ? _heap[index & 0x1fff] : index;
                    break;
                }

                case Opcode.CheckEq:
                    Branch(_reg[0x3f] == Value(ops[0]), ops[1]);
                    break;

                case Opcode.CheckEq2:
                    Branch(_reg[0x3f] == Value(ops[0]) || _reg[0x3f] == Value(ops[1]), ops[2]);
                    break;

                case Opcode.CheckGt:
                    Branch(_reg[0x3f] > Value(ops[0]), ops[1]);
                    break;

                case Opcode.CheckGtEq:
                    if (_reg[0x3f] > Value(ops[0]))
                    {
                        _inst = ops[1].Number;
                    }
                    else if (_reg[0x3f] == Value(ops[0]))
                    {
                        _inst = ops[2].Number;
                    }

                    break;

                case Opcode.CheckWordmap:
                    CheckWordMap(Value(ops[0]), ops[1]);
                    break;

                case Opcode.Save:
                case Opcode.SaveUndo:
                    if (!SaveState(instruction.Opcode == Opcode.SaveUndo, ops[0].Number))
                    {
                        Fail();
                    }

                    break;

                case Opcode.GetInput:
                    _waitingFor = ops[0];
                    BeforeInput();
                    return AaStatus.GetInput;

                case Opcode.GetKey:
                    _waitingFor = ops[0];
                    BeforeInput();
                    return AaStatus.GetKey;

                case Opcode.Ext0:
                    if (Extended(Value(ops[0])) is { } status)
                    {
                        return status;
                    }

                    break;

                case Opcode.VmInfo:
                    Store(ops[1], Information(Value(ops[0])));
                    break;

                case Opcode.Tracepoint:
                    // [aam opcode] Where the compiler marked a rule,
                    // for a tracing interpreter. This one does not
                    // trace, so there is nothing to do.
                    break;

                default:
                    Output(instruction.Opcode, ops);
                    break;
            }
        }
    }

    // [aam runtime] The numbered conditions, in the story's own
    // words, so that a play that hits one says what happened rather
    // than quietly starting over.
    private static string Explain(int code) => code switch
    {
        1 => "the heap ran out",
        2 => "the auxiliary heap ran out",
        3 => "something that was not an object was used as one",
        4 => "an unbound value was stored where a bound one was needed",
        6 => "the long-term heap ran out",
        7 => "the output was left in a state it cannot be left in",
        _ => $"runtime error {code}",
    };

    // [aam opcode] The object an instruction is about, which is the
    // globals when the opcode leaves it out.
    private ushort Subject(ReadOnlySpan<Operand> ops) =>
        ops[0].Kind == OperandKind.Value ? Deref(Value(ops[0])) : AaValue.Null;

    // [aam runtime] Flags are numbered from the most significant bit
    // down, which lets an interpreter work in bytes or in words and
    // get the same answer.
    private static ushort FlagBit(int flag) => (ushort)(0x8000 >> (flag % BitsPerWord));

    private static bool IsWord(ushort value) =>
        AaValue.Tag(value) is AaTag.Word or AaTag.Character or AaTag.ExtDict;

    private bool IsUnknownWord(ushort value) =>
        AaValue.IsExtDict(value) && AaValue.IsPair(_heap[value & 0x1fff]);

    private bool GreaterThan(ushort a, ushort b)
    {
        a = Deref(a);
        b = Deref(b);

        return AaValue.Tag(a) == AaTag.Number
            && AaValue.Tag(b) == AaTag.Number
            && AaValue.Value(a) > AaValue.Value(b);
    }

    private void Branch(bool taken, Operand target)
    {
        if (taken)
        {
            _inst = target.Number;
        }
    }

    private void Arithmetic(ReadOnlySpan<Operand> ops, Func<int, int, int?> operation)
    {
        var dest = ops[2];

        if (Number(Value(ops[0])) is not { } a || Number(Value(ops[1])) is not { } b)
        {
            Fail();
            return;
        }

        StoreNumber(dest, operation(a, b));
    }

    private void Increment(ReadOnlySpan<Operand> ops, int by)
    {
        if (Number(Value(ops[0])) is not { } value)
        {
            Fail();
            return;
        }

        StoreNumber(ops[1], value + by);
    }

    private void RandomNumber(ReadOnlySpan<Operand> ops)
    {
        if (Number(Value(ops[0])) is not { } start || Number(Value(ops[1])) is not { } end)
        {
            Fail();
            return;
        }

        var range = end - start + 1;

        if (range < 1)
        {
            Fail();
            return;
        }

        StoreNumber(ops[2], start + (_random.Next() % range));
    }

    // [aam opcode] A number that will not fit is a failure rather than
    // an error, so a story can ask for one and be told no.
    private void StoreNumber(Operand dest, int? value)
    {
        if (value is not { } number || number < 0 || number > AaValue.MaxNumber)
        {
            Fail();
            return;
        }

        Store(dest, AaValue.Number(number));
    }

    private int? Number(ushort value)
    {
        value = Deref(value);

        return AaValue.Tag(value) == AaTag.Number ? AaValue.Value(value) : null;
    }

    private void PushEnvironment(int locals)
    {
        var address = Math.Min(_env, _cho) - EnvFrame - locals;

        if (address < _top)
        {
            throw new RuntimeError(1);
        }

        _heap[address] = (ushort)_env;
        _heap[address + 1] = (ushort)_sim;
        _heap[address + 2] = (ushort)(_cont >> 16);
        _heap[address + 3] = (ushort)(_cont & 0xffff);

        _env = address;
    }

    private void PushChoice(int arguments, int next)
    {
        var address = Math.Min(_env, _cho) - ChoiceFrame - arguments;

        if (address < _top)
        {
            throw new RuntimeError(1);
        }

        _heap[address] = (ushort)_env;
        _heap[address + 1] = (ushort)_sim;
        _heap[address + 2] = (ushort)(_cont >> 16);
        _heap[address + 3] = (ushort)(_cont & 0xffff);
        _heap[address + 4] = (ushort)(next >> 16);
        _heap[address + 5] = (ushort)(next & 0xffff);
        _heap[address + 6] = (ushort)_cho;
        _heap[address + 7] = (ushort)_top;
        _heap[address + 8] = (ushort)_trail;

        for (var i = 0; i < arguments; i++)
        {
            _heap[address + ChoiceFrame + i] = _reg[i];
        }

        _cho = address;
    }

    // Goes back to the choice point: the saved arguments come back,
    // everything bound since then is unbound, and the heap is given
    // back to where it was.
    private void PopChoice(int arguments)
    {
        for (var i = 0; i < arguments; i++)
        {
            _reg[i] = _heap[_cho + ChoiceFrame + i];
        }

        while (_trail < _heap[_cho + 8])
        {
            _heap[_aux[_trail++]] = AaValue.Null;
        }

        _top = _heap[_cho + 7];
        _cont = (_heap[_cho + 2] << 16) | _heap[_cho + 3];
        _sim = _heap[_cho + 1];
        _env = _heap[_cho];
    }

    private void CheckWordMap(int map, Operand target)
    {
        var objects = _story.WordMaps.Objects(map, _reg[0x3f]);

        // Nothing at all means the word matches nothing, and the jump
        // is what enforces that. A wildcard matches anything, so it
        // pushes nothing and does not jump.
        if (objects is null)
        {
            _inst = target.Number;
            return;
        }

        if (objects.Length == 0)
        {
            return;
        }

        if (_auxTop + objects.Length > _trail)
        {
            throw new RuntimeError(2);
        }

        foreach (var value in objects)
        {
            _aux[_auxTop++] = value;
        }

        _inst = target.Number;
    }

    private void Unlink(int root, int field, ushort key)
    {
        key = Deref(key);

        if (AaValue.Tag(key) != AaTag.Thing)
        {
            return;
        }

        var tail = _ram[FieldAddress(field, key)];
        var at = root;

        while (_ram[at] != AaValue.Null)
        {
            if (_ram[at] == key)
            {
                _ram[at] = tail;
                return;
            }

            at = FieldAddress(field, _ram[at]);
        }
    }

    // [aam opcode] Moving an object: out of whatever was holding it,
    // and onto the front of its new parent's children.
    private void SetParent(ushort child, ushort parent)
    {
        if (parent != AaValue.Null
            && (AaValue.Tag(child) != AaTag.Thing || AaValue.Tag(parent) != AaTag.Thing))
        {
            throw new RuntimeError(3);
        }

        if (AaValue.Tag(child) != AaTag.Thing)
        {
            return;
        }

        var old = _ram[FieldAddress(FieldParent, child)];

        if (old != AaValue.Null)
        {
            Unlink(FieldAddress(FieldChild, old), FieldSibling, child);
        }

        _ram[FieldAddress(FieldParent, child)] = parent;

        if (AaValue.Tag(parent) == AaTag.Thing)
        {
            _ram[FieldAddress(FieldSibling, child)] = _ram[FieldAddress(FieldChild, parent)];
            _ram[FieldAddress(FieldChild, parent)] = child;
        }
    }
}

using Rezrov.AaMachine.Instructions;

namespace Rezrov.AaMachine.Execution;

/// <summary>
/// [aam opcode] Where a play has got to: it has finished, or it is
/// waiting to be told something.
/// </summary>
public enum AaStatus
{
    /// <summary>The story has quit and will run no further.</summary>
    Quit,

    /// <summary>The story is waiting for a line of input.</summary>
    GetInput,

    /// <summary>The story is waiting for a single keypress.</summary>
    GetKey,
}

/// <summary>
/// The Aa-machine itself: the machine Dialog compiles to when it is
/// not compiling to the Z-Machine.
/// </summary>
/// <remarks>
/// [aam runtime] This is a machine built for a language that asks
/// questions rather than one that gives orders. Instead of calls and
/// returns there are choice points, and when a query fails the machine
/// goes back to the last one and tries the next answer. That is what
/// most of the state here is for.
///
/// The main heap holds three things growing into each other: values
/// from the bottom, environment frames from the top, and choice frames
/// from the top as well. The auxiliary heap holds a stack growing up
/// and the trail growing down. The trail is the record of which
/// variables have been bound since the last choice point, so that
/// going back can unbind them again.
///
/// The random access area is the ordinary mutable memory: the objects
/// and their fields, and the long-term heap where a value too big for
/// a field is kept.
/// </remarks>
public sealed partial class Machine
{
    // [aam runtime] Sixteen bits to a word, so a flag number picks its
    // bit out of a word of sixteen.
    private const int BitsPerWord = 16;

    // [aam runtime] A choice frame is nine words and then the saved
    // arguments; an environment frame is four and then the locals.
    private const int ChoiceFrame = 9;
    private const int EnvFrame = 4;

    // [aam runtime] The fields every object has, in the order the
    // machine knows them by.
    private const int FieldParent = 0;
    private const int FieldChild = 1;
    private const int FieldSibling = 2;

    private readonly AaStory _story;
    private readonly IAaOutput _output;
    private readonly AaRandom _random;
    private readonly Instruction?[] _decoded;

    private readonly ushort[] _reg = new ushort[64];
    private readonly ushort[] _heap;
    private readonly ushort[] _aux;
    private readonly ushort[] _ram;

    private readonly List<int> _divs = [];
    private readonly List<byte> _characters = [];

    private int _inst;
    private int _cont;
    private int _top;
    private int _env;
    private int _cho;
    private int _sim;
    private int _auxTop;
    private int _trail;
    private int _stoppableAux;
    private int _stoppableChoice;
    private int _cwl;
    private Spacing _spc;

    private ushort _objectCount;
    private ushort _longTermBottom;
    private ushort _longTermTop;
    private int _tmp;

    private bool _upper;
    private bool _inStatus;
    private int _spans;
    private int _links;

    private Operand _waitingFor;
    private byte[]? _undo;

    public Machine(AaStory story, IAaOutput output, int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(output);

        _story = story;
        _output = output;
        _random = new AaRandom(seed);

        _heap = new ushort[story.HeapSize];
        _aux = new ushort[story.AuxSize];
        _ram = new ushort[story.RamSize];

        // [aam opcode] The bytecode never changes, so every
        // instruction is read once here rather than on every step.
        _decoded = new Instruction?[story.Instructions.Length];

        foreach (var instruction in story.Instructions.All())
        {
            _decoded[instruction.Address] = instruction;
        }

        // [aam story] The sweep starts at the entry point, so address
        // 0 is read separately. It holds FAIL, and a great deal of
        // ordinary backtracking goes through it.
        _decoded[0] = story.Instructions.Decode(0);
    }

    /// <summary>
    /// [aam opcode] The whitespace the machine owes the output before
    /// it prints anything more. Nothing is written until something
    /// follows it, which is how a story can ask for a paragraph break
    /// and then finish without leaving a blank line behind.
    /// </summary>
    private enum Spacing
    {
        Auto = 0,
        NoSpace = 1,
        NoBreakSpace = 2,
        PendingSpace = 3,
        Space = 4,
        Line = 5,
        Paragraph = 6,
    }

    /// <summary>Whether the story is waiting to be saved into.</summary>
    public bool CanUndo => _undo is not null;

    /// <summary>
    /// Where a file is saved to and restored from, if anywhere. A
    /// frontend that can ask the player sets this when it is asked.
    /// </summary>
    public Func<bool, string?>? SaveFileName { get; set; }

    /// <summary>
    /// Starts the story and runs until it wants something or stops.
    /// </summary>
    public AaStatus Start()
    {
        Reinitialize();
        Reset(AaValue.Null);

        return Run();
    }

    /// <summary>
    /// Hands the story a line the player typed and runs on.
    /// </summary>
    public AaStatus ProceedWithInput(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Store(_waitingFor, ParseInput(line));

        _spc = Spacing.Line;

        return Run();
    }

    /// <summary>
    /// Hands the story a single keypress and runs on.
    /// </summary>
    /// <remarks>
    /// [aam opcode] A digit comes back as a number and anything else
    /// as a character, which is why a game can ask "press 1, 2 or 3"
    /// and compare the answer with a number.
    /// </remarks>
    public AaStatus ProceedWithKey(int key)
    {
        Store(_waitingFor, key is >= '0' and <= '9'
            ? AaValue.Number(key - '0')
            : AaValue.Character(key));

        return Run();
    }

    // [aam runtime] The state a story file starts from: the objects
    // and their fields out of the INIT chunk, and both heaps marked
    // unused so that peak memory can be measured.
    private void Reinitialize()
    {
        var init = _story.Contents("INIT");

        _objectCount = Read16(init, 0);
        _longTermBottom = Read16(init, 2);
        _longTermTop = Read16(init, 4);

        Array.Fill(_heap, AaValue.Unused);
        Array.Fill(_aux, AaValue.Unused);
        Array.Fill(_ram, AaValue.Unused);

        for (var at = 6; at + 1 < init.Length && ((at - 6) >> 1) < _ram.Length; at += 2)
        {
            _ram[(at - 6) >> 1] = Read16(init, at);
        }
    }

    // [aam runtime] Everything but the random access area, which a
    // runtime error leaves alone so that the story can say what went
    // wrong before it starts again.
    private void Reset(ushort first)
    {
        Array.Clear(_reg);

        _reg[0] = first;

        _inst = InstructionDecoder.Entry;
        _cont = 0;
        _top = 0;
        _env = _heap.Length;
        _cho = _heap.Length;
        _sim = 0xffff;
        _auxTop = 0;
        _trail = _aux.Length;
        _stoppableAux = 0;
        _stoppableChoice = 0;
        _cwl = 0;
        _spc = Spacing.Line;

        _upper = false;
        _inStatus = false;
        _spans = 0;
        _links = 0;

        _divs.Clear();
        _random.Reset();
    }

    private static ushort Read16(ReadOnlySpan<byte> bytes, int at) =>
        at + 1 < bytes.Length ? (ushort)((bytes[at] << 8) | bytes[at + 1]) : AaValue.Unused;

    // [aam opcode] A failure goes back to the last choice point and
    // takes its other branch, which is the failure handler saved in
    // the frame.
    private void Fail() => _inst = (_heap[_cho + 4] << 16) | _heap[_cho + 5];

    // [aam runtime] Following a chain of bound variables to whatever
    // is at the end of it, which may be another unbound variable.
    private ushort Deref(ushort value)
    {
        while (AaValue.IsReference(value))
        {
            var next = _heap[value & 0x1fff];

            if (next == AaValue.Null)
            {
                return value;
            }

            value = next;
        }

        return value;
    }

    /// <summary>
    /// [aam opcode] Makes two values the same, binding whatever is
    /// unbound in either of them, and says whether that was possible.
    /// </summary>
    /// <remarks>
    /// Every binding is written to the trail, so that going back to a
    /// choice point can undo it. That is the whole of backtracking:
    /// the trail is the list of what to forget.
    /// </remarks>
    private bool Unify(ushort a, ushort b)
    {
        while (true)
        {
            a = Deref(a);
            b = Deref(b);

            var left = AaValue.IsReference(a);
            var right = AaValue.IsReference(b);

            if (left && right)
            {
                // Binding the younger to the older keeps the trail in
                // order. Two references that are already the same
                // need no binding at all.
                if (a != b)
                {
                    Bind(a < b ? b : a, a < b ? a : b);
                }

                return true;
            }

            if (left)
            {
                Bind(a, b);
                return true;
            }

            if (right)
            {
                Bind(b, a);
                return true;
            }

            // [aam runtime] An unrecognized word is kept as a known
            // part and the characters left over, and it unifies as
            // its known part does.
            if (AaValue.IsExtDict(a) && AaValue.IsExtDict(b))
            {
                a = _heap[a & 0x1fff];
                b = _heap[b & 0x1fff];
                continue;
            }

            if (AaValue.IsExtDict(a))
            {
                a = _heap[a & 0x1fff];
                continue;
            }

            if (AaValue.IsExtDict(b))
            {
                b = _heap[b & 0x1fff];
                continue;
            }

            if (a == b)
            {
                return true;
            }

            if (AaValue.IsPair(a) && AaValue.IsPair(b))
            {
                // The heads have to match, and then the tails do, and
                // a list is only as long as the loop goes round.
                if (!Unify(AaValue.Reference(a & 0x1fff), AaValue.Reference(b & 0x1fff)))
                {
                    return false;
                }

                a = AaValue.Reference((a & 0x1fff) + 1);
                b = AaValue.Reference((b & 0x1fff) + 1);
                continue;
            }

            return false;
        }
    }

    private void Bind(ushort variable, ushort value)
    {
        if (_trail <= _auxTop)
        {
            throw new RuntimeError(2);
        }

        _aux[--_trail] = (ushort)(variable & 0x1fff);
        _heap[variable & 0x1fff] = value;
    }

    /// <summary>
    /// [aam opcode] Whether two values could be made the same, without
    /// binding anything to find out.
    /// </summary>
    private bool WouldUnify(ushort a, ushort b)
    {
        while (true)
        {
            a = Deref(a);
            b = Deref(b);

            if (AaValue.IsReference(a) || AaValue.IsReference(b))
            {
                return true;
            }

            if (AaValue.IsExtDict(a) && AaValue.IsExtDict(b))
            {
                a = _heap[a & 0x1fff];
                b = _heap[b & 0x1fff];
                continue;
            }

            if (AaValue.IsExtDict(a))
            {
                a = _heap[a & 0x1fff];
                continue;
            }

            if (AaValue.IsExtDict(b))
            {
                b = _heap[b & 0x1fff];
                continue;
            }

            if (a == b)
            {
                return true;
            }

            if (AaValue.IsPair(a) && AaValue.IsPair(b))
            {
                if (!WouldUnify(AaValue.Reference(a & 0x1fff), AaValue.Reference(b & 0x1fff)))
                {
                    return false;
                }

                a = AaValue.Reference((a & 0x1fff) + 1);
                b = AaValue.Reference((b & 0x1fff) + 1);
                continue;
            }

            return false;
        }
    }

    // The word an operand names, taken as it stands. A raw number and
    // a live value are fetched the same way and only differ in what
    // the opcode does with them afterwards.
    private ushort Value(Operand operand) => operand.Source switch
    {
        OperandSource.Register => _reg[operand.Number],
        OperandSource.EnvSlot => _heap[_env + EnvFrame + operand.Number],
        _ => (ushort)operand.Number,
    };

    // [aam opcode] Putting a result where the instruction asked: over
    // what is there, or unified with it, in which case a mismatch is
    // a failure rather than an error.
    private void Store(Operand dest, ushort value)
    {
        if (dest.Unify)
        {
            var existing = dest.Source == OperandSource.EnvSlot
                ? _heap[_env + EnvFrame + dest.Number]
                : _reg[dest.Number];

            if (!Unify(existing, value))
            {
                Fail();
            }

            return;
        }

        if (dest.Source == OperandSource.EnvSlot)
        {
            _heap[_env + EnvFrame + dest.Number] = value;
        }
        else
        {
            _reg[dest.Number] = value;
        }
    }

    // [aam runtime] Where an object's field lives. Object zero is the
    // globals, which belong to no object.
    private int FieldAddress(int field, ushort obj)
    {
        if (obj > _objectCount)
        {
            throw new RuntimeError(3);
        }

        return _ram[obj] + field;
    }

    // Reading is gentler than writing: something that is not an object
    // reads as nothing rather than as an error.
    private ushort ReadField(int field, ushort obj) =>
        obj > _objectCount ? AaValue.Null : _ram[_ram[obj] + field];

    private int Allocate(int words)
    {
        var address = _top;

        _top += words;

        if (_top > Math.Min(_env, _cho))
        {
            throw new RuntimeError(1);
        }

        return address;
    }

    private ushort MakePair(ushort head, ushort tail)
    {
        var address = Allocate(2);

        _heap[address] = head;
        _heap[address + 1] = tail;

        return AaValue.Pair(address);
    }

    private void PushAux(ushort value)
    {
        if (_auxTop >= _trail)
        {
            throw new RuntimeError(2);
        }

        _aux[_auxTop++] = value;
    }

    /// <summary>
    /// [aam runtime] One of the numbered conditions the machine can
    /// get into, which restarts the story with the number in R00
    /// rather than stopping it.
    /// </summary>
    private sealed class RuntimeError(int code) : Exception
    {
        public int Code { get; } = code;
    }
}

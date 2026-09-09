using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Streams;
using Rezrov.ZMachine.Text;
using static Rezrov.Tests.Assembler;

namespace Rezrov.Tests;

public partial class InterpreterTests
{
    private const int Globals = 0x100;
    private const int Objects = 0x200;
    private const int Code = 0x1000;
    private const int RoutineA = 0x1100;
    private const int RoutineB = 0x1200;
    private const int Strings = 0x1300;

    // Variable numbers for the first three globals.
    private const int G0 = 0x10;
    private const int G1 = 0x11;
    private const int G2 = 0x12;

    /// <summary>
    /// A story whose code starts at $1000, with dynamic memory ending at
    /// $400, a globals table at $100, and an object table at $200 holding
    /// a room with a lamp inside it. Routines A and B can be filled in
    /// per test. The dictionary at $380 is empty until a test fills it
    /// with <see cref="Words"/>, and the abbreviation table points at
    /// harmless empty space.
    /// </summary>
    private sealed class Story
    {
        public const int DictionaryAddress = 0x380;

        public byte[] Bytes { get; } = new byte[8192];

        public ZMachineVersion Version { get; }

        public Story(ZMachineVersion version)
        {
            Version = version;
            Bytes[0x00] = (byte)version;
            PutWord(0x04, 0x0400);
            PutWord(0x06, version == ZMachineVersion.V6 ? Packed(RoutineB) : Code);
            PutWord(0x08, DictionaryAddress);
            PutWord(0x0A, Objects);
            PutWord(0x0C, Globals);
            PutWord(0x0E, 0x0400);
            PutWord(0x18, 0x0300);

            // An empty dictionary: no separators, six-byte entries, none.
            Bytes[DictionaryAddress + 1] = 6;

            BuildObjects();
        }

        // [zm 1.2.3] Packed addresses, with zero offsets in the header.
        public ushort Packed(int address) => Version switch
        {
            <= ZMachineVersion.V3 => (ushort)(address / 2),
            ZMachineVersion.V8 => (ushort)(address / 8),
            _ => (ushort)(address / 4),
        };

        public void PutWord(int address, int value)
        {
            Bytes[address] = (byte)(value >> 8);
            Bytes[address + 1] = (byte)value;
        }

        public void Put(int address, byte[] data) => data.CopyTo(Bytes, address);

        /// <summary>
        /// Fills the game's dictionary with words, sorted as [zm 13.2]
        /// requires, with comma and period as the word separators.
        /// </summary>
        public void Words(params string[] words) => WriteDictionary(DictionaryAddress, ",."u8.ToArray(), sorted: true, words);

        /// <summary>
        /// Writes a user dictionary for tokenise: no separators, and
        /// [zm op:tokenise] a negative count meaning that many entries,
        /// unsorted, kept in the order given.
        /// </summary>
        public void UserWords(int address, params string[] words) => WriteDictionary(address, [], sorted: false, words);

        private void WriteDictionary(int address, byte[] separators, bool sorted, string[] words)
        {
            var encoder = new ZTextEncoder(Version, Version == ZMachineVersion.V1 ? AlphabetTable.Version1 : AlphabetTable.Default);
            var entries = words.Select(encoder.EncodeWord).ToList();
            if (sorted)
            {
                entries.Sort((a, b) => a.AsSpan().SequenceCompareTo(b));
            }

            // [zm 13.2] Separator count and separators, the entry length,
            // the count, then the entries: the encoded word plus one byte
            // of data each.
            var at = address;
            Bytes[at++] = (byte)separators.Length;
            Put(at, separators);
            at += separators.Length;
            Bytes[at++] = (byte)(encoder.EncodedLength + 1);
            PutWord(at, sorted ? entries.Count : -entries.Count & 0xFFFF);
            at += 2;

            foreach (var entry in entries)
            {
                Put(at, entry);
                at += encoder.EncodedLength + 1;
            }
        }

        /// <summary>
        /// Writes a routine: the local count, defaults in versions that
        /// have them, then the code.
        /// </summary>
        public void Routine(int address, int locals, byte[] code)
        {
            Bytes[address] = (byte)locals;
            var at = address + 1;
            if (Version <= ZMachineVersion.V4)
            {
                at += locals * 2;
            }

            Put(at, code);
        }

        // Object 1 is a room named "room" with attribute 3 set and a
        // property 5 of two bytes and a property 2 of one byte. Object 2
        // is a lamp named "lamp" inside the room.
        private void BuildObjects()
        {
            var early = Version <= ZMachineVersion.V3;
            var defaults = early ? 31 : 63;
            var entry = early ? 9 : 14;
            var first = Objects + (defaults * 2);
            var tables = first + (2 * entry);

            PutWord(Objects + ((5 - 1) * 2), 0x0505);

            WriteObject(first, parent: 0, sibling: 0, child: 2, properties: tables, attribute: 3);
            WriteObject(first + entry, parent: 1, sibling: 0, child: 0, properties: tables + 0x20, attribute: -1);

            var at = tables;
            var name = ZChars.Text("room");
            Bytes[at++] = (byte)(name.Length / 2);
            Put(at, name);
            at += name.Length;

            if (early)
            {
                Bytes[at++] = (32 * 1) + 5;
            }
            else
            {
                Bytes[at++] = 0x40 | 5;
            }

            PutWord(at, 0x1234);
            at += 2;

            // A one-byte property 2 has the same size byte in both
            // layouts: [zm 12.4.1] 32 times 0 plus 2, and [zm 12.4.2.2]
            // number 2 with bit 6 clear.
            Bytes[at++] = 2;
            Bytes[at++] = 0x77;
            Bytes[at] = 0;

            at = tables + 0x20;
            name = ZChars.Text("lamp");
            Bytes[at++] = (byte)(name.Length / 2);
            Put(at, name);
            at += name.Length;
            Bytes[at] = 0;

            void WriteObject(int address, int parent, int sibling, int child, int properties, int attribute)
            {
                if (attribute >= 0)
                {
                    Bytes[address + (attribute / 8)] |= (byte)(0x80 >> (attribute % 8));
                }

                if (early)
                {
                    Bytes[address + 4] = (byte)parent;
                    Bytes[address + 5] = (byte)sibling;
                    Bytes[address + 6] = (byte)child;
                    PutWord(address + 7, properties);
                }
                else
                {
                    PutWord(address + 6, parent);
                    PutWord(address + 8, sibling);
                    PutWord(address + 10, child);
                    PutWord(address + 12, properties);
                }
            }
        }
    }

    private sealed class Run
    {
        public Run(Story story, string output, Interpreter interpreter)
        {
            Story = story;
            Output = output;
            Interpreter = interpreter;
        }

        public Story Story { get; }

        public string Output { get; }

        public Interpreter Interpreter { get; }

        public ushort Global(int number) => Interpreter.State.ReadGlobal(number);
    }

    /// <summary>
    /// Runs code placed at the start address until it quits, with a
    /// generous instruction limit so a broken loop fails rather than
    /// hangs.
    /// </summary>
    private static Run Execute(
        Assembler code,
        Action<Story>? setup = null,
        ZMachineVersion version = ZMachineVersion.V5,
        IInput? input = null,
        IScreen? screen = null,
        IFileChooser? files = null)
    {
        var story = new Story(version);
        story.Put(Code, code.ToArray());
        setup?.Invoke(story);

        var memory = new ZMemory(story.Bytes);
        var writer = new StringWriter();
        var interpreter = new Interpreter(memory, screen ?? new TextWriterScreen(writer), input ?? new ScriptedInput(), files: files);

        interpreter.Run(10000);
        Assert.True(interpreter.HasQuit, "The program did not quit within 10000 instructions.");

        return new Run(story, writer.ToString(), interpreter);
    }

    // A branch that skips a three-byte instruction, such as a long-form
    // store of a small constant, needs an offset of 5: [zm 4.7.2] the
    // target is the address after the branch data plus the offset minus 2.
    private const int SkipStore = 5;

    [Fact]
    public void AddsSubtractsAndMultipliesModulo65536()
    {
        var run = Execute(new Assembler()
            .Variable(Op.Add, false, Large(30000), Large(30000)).Store(G0)
            .Variable(Op.Sub, false, Large(5), Large(10)).Store(G1)
            .Variable(Op.Mul, false, Large(300), Large(300)).Store(G2)
            .Quit());

        // [zm 2.3.2] 60000 is out of the signed range and comes back as
        // 60000 - 65536; [zm 2.2] -5 is stored as 65536 - 5.
        Assert.Equal(60000, run.Global(G0));
        Assert.Equal(0xFFFB, run.Global(G1));
        Assert.Equal(90000 & 0xFFFF, run.Global(G2));
    }

    [Fact]
    public void DividesWithSignsAsTheStandardsExamplesSay()
    {
        // [zm 2] The remarks: -11 / 2 = -5, -11 / -2 = 5, 11 / -2 = -5,
        // -13 % 5 = -3, 13 % -5 = 3, -13 % -5 = -3.
        var run = Execute(new Assembler()
            .Variable(Op.Div, false, Large(-11 & 0xFFFF), Large(2)).Store(G0)
            .Variable(Op.Div, false, Large(-11 & 0xFFFF), Large(-2 & 0xFFFF)).Store(G1)
            .Variable(Op.Mod, false, Large(-13 & 0xFFFF), Large(5)).Store(G2)
            .Variable(Op.Mod, false, Large(13), Large(-5 & 0xFFFF)).Store(0x13)
            .Quit());

        Assert.Equal(-5, (short)run.Global(G0));
        Assert.Equal(5, (short)run.Global(G1));
        Assert.Equal(-3, (short)run.Global(G2));
        Assert.Equal(3, (short)run.Global(0x13));
    }

    [Fact]
    public void DivisionByZeroHaltsTheInterpreter()
    {
        // [zm 2.3.1]
        var code = new Assembler().Variable(Op.Div, false, Large(1), Large(0)).Store(G0).Quit();

        Assert.Throws<InvalidOperationException>(() => Execute(code));
    }

    [Fact]
    public void BitwiseOperationsAreUnsigned()
    {
        var run = Execute(new Assembler()
            .Variable(Op.And, false, Large(0xF0F0), Large(0xFF00)).Store(G0)
            .Variable(Op.Or, false, Large(0xF0F0), Large(0x000F)).Store(G1)
            .Short1(Op.Not, Large(0x00FF)).Store(G2)
            .Quit(), version: ZMachineVersion.V4);

        // [zm 2.2.1] and [zm op:not]
        Assert.Equal(0xF000, run.Global(G0));
        Assert.Equal(0xF0FF, run.Global(G1));
        Assert.Equal(0xFF00, run.Global(G2));
    }

    [Fact]
    public void ShiftsDifferInHowTheyTreatTheSign()
    {
        var run = Execute(new Assembler()
            .Ext(Op.LogShift, Large(0x8000), Large(-4 & 0xFFFF)).Store(G0)
            .Ext(Op.ArtShift, Large(0x8000), Large(-4 & 0xFFFF)).Store(G1)
            .Ext(Op.LogShift, Large(1), Large(3)).Store(G2)
            .Quit());

        // [zm op:log_shift] zeros come in from the left; [zm op:art_shift]
        // the sign bit does.
        Assert.Equal(0x0800, run.Global(G0));
        Assert.Equal(0xF800, run.Global(G1));
        Assert.Equal(8, run.Global(G2));
    }

    [Fact]
    public void BranchesFollowTheSenseAndOffset()
    {
        // jz 0 branches on true with offset 4, skipping the store of 1;
        // then jz 1 does not branch, so 2 is stored.
        var run = Execute(new Assembler()
            .Short1(Op.Jz, Small(0)).Branch(true, SkipStore)
            .Long(Op.Store, Small(G0), Small(1))
            .Short1(Op.Jz, Small(1)).Branch(true, SkipStore)
            .Long(Op.Store, Small(G1), Small(2))
            .Quit());

        // [zm 4.7.2] Offset 4 from the byte after the branch skips the
        // two-byte store... plus the two the formula subtracts.
        Assert.Equal(0, run.Global(G0));
        Assert.Equal(2, run.Global(G1));
    }

    [Fact]
    public void BranchOnFalseAndLongOffsetsWork()
    {
        // jz 5 is false; the branch is on false, so it is taken, with a
        // long-form offset of 6 skipping the two stores.
        var run = Execute(new Assembler()
            .Short1(Op.Jz, Small(5)).Branch(false, 200)
            .Quit(), setup: story =>
            {
                // The target, 200 - 2 bytes past the branch data: store 9
                // and quit. The branch data ends at Code + 4.
                story.Put(Code + 4 + 198, new Assembler().Long(Op.Store, Small(G0), Small(9)).Quit().ToArray());
            });

        Assert.Equal(9, run.Global(G0));
    }

    [Fact]
    public void JeComparesAgainstEveryLaterOperand()
    {
        var run = Execute(new Assembler()
            .Variable(Op.Je, false, Small(7), Small(1), Small(7), Small(3)).Branch(true, SkipStore)
            .Long(Op.Store, Small(G0), Small(1))
            .Variable(Op.Je, false, Small(7), Small(1), Small(2)).Branch(true, SkipStore)
            .Long(Op.Store, Small(G1), Small(1))
            .Quit());

        // [zm op:je] 7 matches the second of three, so the first store is
        // skipped; nothing matches in the second, so it is not.
        Assert.Equal(0, run.Global(G0));
        Assert.Equal(1, run.Global(G1));
    }

    [Fact]
    public void JeWithOneOperandIsAnError()
    {
        // [zm op:je]
        var code = new Assembler().Variable(Op.Je, false, Small(7)).Branch(true, 2).Quit();

        Assert.Throws<InvalidOperationException>(() => Execute(code));
    }

    [Fact]
    public void ComparisonsAreSigned()
    {
        // [zm 2.2.1] -1 is less than 1 even though $FFFF is more than 1.
        var run = Execute(new Assembler()
            .Variable(Op.Jl, false, Large(0xFFFF), Large(1)).Branch(true, SkipStore)
            .Long(Op.Store, Small(G0), Small(1))
            .Variable(Op.Jg, false, Large(0xFFFF), Large(1)).Branch(true, SkipStore)
            .Long(Op.Store, Small(G1), Small(1))
            .Quit());

        Assert.Equal(0, run.Global(G0));
        Assert.Equal(1, run.Global(G1));
    }

    [Fact]
    public void TestBranchesWhenAllFlagsAreSet()
    {
        // [zm op:test] bitmap & flags == flags.
        var run = Execute(new Assembler()
            .Variable(Op.Test, false, Large(0x0F0F), Large(0x0101)).Branch(true, SkipStore)
            .Long(Op.Store, Small(G0), Small(1))
            .Variable(Op.Test, false, Large(0x0F0F), Large(0x0110)).Branch(true, SkipStore)
            .Long(Op.Store, Small(G1), Small(1))
            .Quit());

        Assert.Equal(0, run.Global(G0));
        Assert.Equal(1, run.Global(G1));
    }

    [Fact]
    public void JumpAppliesASignedOffset()
    {
        // jump forward over a store, then the target jumps back over
        // nothing useful, so: jump +5 lands on the quit after a 3-byte
        // store.
        var run = Execute(new Assembler()
            .Short1(Op.Jump, Large(3 + 2))
            .Long(Op.Store, Small(G0), Small(1))
            .Quit());

        // [zm op:jump] Address after the instruction, plus 5, minus 2 is
        // three bytes on, past the store.
        Assert.Equal(0, run.Global(G0));
    }

    [Fact]
    public void CallsARoutineAndStoresItsResult()
    {
        var run = Execute(new Assembler()
            .Variable(Op.Call, true, Large(0), Small(0)).Store(G0)
            .Long(Op.Store, Small(G1), Small(1))
            .Quit(), setup: story =>
            {
                // Routine A adds its two arguments and returns the sum.
                story.Routine(RoutineA, 2, new Assembler()
                    .Long(Op.Add, Var(1), Var(2)).Store(0)
                    .Short1(Op.Ret, Var(0))
                    .ToArray());
                story.PutWord(Code + 2, story.Packed(RoutineA));
            });

        Assert.Equal(1, run.Global(G1));
    }

    [Fact]
    public void ArgumentsReachTheRoutineAndTheResultComesBack()
    {
        var run = Execute(new Assembler()
            .Variable(Op.Call, true, Large(RoutineA / 4), Small(20), Small(22)).Store(G0)
            .Quit(), setup: story => story.Routine(RoutineA, 2, new Assembler()
                .Long(Op.Add, Var(1), Var(2)).Store(0)
                .Short1(Op.Ret, Var(0))
                .ToArray()));

        // [zm 6.4.4] Locals 1 and 2 are the arguments, and [zm op:call]
        // the result is stored.
        Assert.Equal(42, run.Global(G0));
    }

    [Fact]
    public void CallVnDiscardsTheResultAndCallToZeroReturnsFalse()
    {
        var run = Execute(new Assembler()
            .Long(Op.Store, Small(G0), Small(9))
            .Variable(Op.CallVn, true, Large(RoutineA / 4))
            .Variable(Op.Call, true, Large(0)).Store(G1)
            .Quit(), setup: story => story.Routine(RoutineA, 0, new Assembler()
                .Short1(Op.Ret, Small(5))
                .ToArray()));

        // [zm op:call_vn] Nothing stored, so G0 keeps its 9. [zm 6.4.3]
        // Calling 0 stores false.
        Assert.Equal(9, run.Global(G0));
        Assert.Equal(0, run.Global(G1));
    }

    [Fact]
    public void ReturnValueCanGoOnTheCallersStack()
    {
        var run = Execute(new Assembler()
            .Variable(Op.Call, true, Large(RoutineA / 4)).Store(0)
            .Variable(Op.Pull, true, Small(G0))
            .Quit(), setup: story => story.Routine(RoutineA, 0, new Assembler()
                .Short0(Op.Rtrue)
                .ToArray()));

        // [zm op:rtrue] and [zm 6.4.2] The 1 lands on the caller's stack,
        // and pull moves it into G0.
        Assert.Equal(1, run.Global(G0));
    }

    [Fact]
    public void IncChkOnTheStackWorksInPlace()
    {
        // Push 5 and 7, then inc_chk sp 7: the top becomes 8, which is
        // greater than 7, so the branch is taken past the store. Then pull
        // twice: 8, then the 5 that must still be underneath.
        var run = Execute(new Assembler()
            .Variable(Op.Push, true, Small(5))
            .Variable(Op.Push, true, Small(7))
            .Long(Op.IncChk, Small(0), Small(7)).Branch(true, SkipStore)
            .Long(Op.Store, Small(G2), Small(1))
            .Variable(Op.Pull, true, Small(G0))
            .Variable(Op.Pull, true, Small(G1))
            .Quit());

        // [zm 6.3.4] The editor's note's test, through the real opcode.
        Assert.Equal(0, run.Global(G2));
        Assert.Equal(8, run.Global(G0));
        Assert.Equal(5, run.Global(G1));
    }

    [Fact]
    public void LoadAndStoreTakeVariableReferences()
    {
        var run = Execute(new Assembler()
            .Variable(Op.Store, false, Small(G0), Large(0x4321))
            .Short1(Op.Load, Small(G0)).Store(G1)
            .Short1(Op.Inc, Small(G1))
            .Short1(Op.Dec, Small(G0))
            .Quit());

        // [zm op:store], [zm op:load], [zm op:inc], [zm op:dec]
        Assert.Equal(0x4320, run.Global(G0));
        Assert.Equal(0x4322, run.Global(G1));
    }

    [Fact]
    public void IncrementIsSignedSoMinusOneBecomesZero()
    {
        var run = Execute(new Assembler()
            .Variable(Op.Store, false, Small(G0), Large(0xFFFF))
            .Short1(Op.Inc, Small(G0))
            .Long(Op.Store, Small(G1), Small(0))
            .Short1(Op.Dec, Small(G1))
            .Quit());

        // [zm op:inc] and [zm op:dec]
        Assert.Equal(0, run.Global(G0));
        Assert.Equal(0xFFFF, run.Global(G1));
    }

    [Fact]
    public void CatchAndThrowReturnFromTheCatchingRoutine()
    {
        var run = Execute(new Assembler()
            .Variable(Op.Call, true, Large(RoutineA / 4)).Store(G0)
            .Quit(), setup: story =>
            {
                // A catches its frame into G1, calls B, then would store 1
                // into G2, which the throw skips.
                story.Routine(RoutineA, 0, new Assembler()
                    .Short0(Op.Catch).Store(G1)
                    .Variable(Op.Call, true, Large(RoutineB / 4)).Store(0)
                    .Long(Op.Store, Small(G2), Small(1))
                    .Short1(Op.Ret, Small(7))
                    .ToArray());

                // B throws 99 to A's frame.
                story.Routine(RoutineB, 0, new Assembler()
                    .Long(Op.Throw, Small(99), Var(G1))
                    .ToArray());
            });

        // [zm op:throw] A returns 99 as though it had done so itself.
        Assert.Equal(99, run.Global(G0));
        Assert.Equal(0, run.Global(G2));
    }

    [Fact]
    public void CheckArgCountKnowsWhatWasPassed()
    {
        var run = Execute(new Assembler()
            .Variable(Op.Call, true, Large(RoutineA / 4), Small(1)).Store(G0)
            .Quit(), setup: story => story.Routine(RoutineA, 2, new Assembler()
                .Variable(Op.CheckArgCount, true, Small(2)).Branch(true, 4)
                .Short1(Op.Ret, Small(1))
                .Short1(Op.Ret, Small(2))
                .ToArray()));

        // [zm op:check_arg_count] One argument to a routine with two
        // locals, so argument 2 was not supplied and the routine returns 1.
        Assert.Equal(1, run.Global(G0));
    }

    [Fact]
    public void ReadsAndWritesMemoryByWordAndByte()
    {
        var run = Execute(new Assembler()
            .Variable(Op.Storew, true, Large(0x0300), Small(2), Large(0xBEEF))
            .Variable(Op.Storeb, true, Large(0x0300), Small(1), Small(0x42))
            .Variable(Op.Loadw, false, Large(0x0300), Small(2)).Store(G0)
            .Quit());

        Assert.Equal(0xBEEF, run.Global(G0));
        Assert.Equal(0x42, run.Story.Bytes[0x0301]);
        Assert.Equal(0xBE, run.Story.Bytes[0x0304]);
    }

    [Fact]
    public void WritingToStaticMemoryIsAnError()
    {
        // [zm op:storew] The address must lie in dynamic memory, which
        // ends at $400 here.
        var code = new Assembler().Variable(Op.Storew, true, Large(0x0400), Small(0), Small(1)).Quit();

        Assert.Throws<InvalidOperationException>(() => Execute(code));
    }

    [Fact]
    public void WalksTheObjectTree()
    {
        var run = Execute(new Assembler()
            .Short1(Op.GetChild, Small(1)).Store(G0).Branch(true, SkipStore)
            .Long(Op.Store, Small(G2), Small(1))
            .Short1(Op.GetParent, Small(2)).Store(G1)
            .Short1(Op.GetSibling, Small(2)).Store(0x13).Branch(false, SkipStore)
            .Long(Op.Store, Small(0x14), Small(1))
            .Long(Op.Jin, Small(2), Small(1)).Branch(true, SkipStore)
            .Long(Op.Store, Small(0x15), Small(1))
            .Quit());

        // [zm op:get_child] The lamp, and it exists so the branch skips.
        // [zm op:get_parent] The room. [zm op:get_sibling] Nothing, so the
        // branch on false is taken. [zm op:jin] The lamp is in the room.
        Assert.Equal(2, run.Global(G0));
        Assert.Equal(0, run.Global(G2));
        Assert.Equal(1, run.Global(G1));
        Assert.Equal(0, run.Global(0x13));
        Assert.Equal(0, run.Global(0x14));
        Assert.Equal(0, run.Global(0x15));
    }

    [Fact]
    public void MovesObjectsAndChangesAttributes()
    {
        var run = Execute(new Assembler()
            .Short1(Op.RemoveObj, Small(2))
            .Short1(Op.GetParent, Small(2)).Store(G0)
            .Long(Op.InsertObj, Small(2), Small(1))
            .Short1(Op.GetChild, Small(1)).Store(G1).Branch(true, 2)
            .Long(Op.SetAttr, Small(2), Small(7))
            .Long(Op.ClearAttr, Small(1), Small(3))
            .Long(Op.TestAttr, Small(2), Small(7)).Branch(true, SkipStore)
            .Long(Op.Store, Small(G2), Small(1))
            .Long(Op.TestAttr, Small(1), Small(3)).Branch(true, SkipStore)
            .Long(Op.Store, Small(0x13), Small(1))
            .Quit());

        Assert.Equal(0, run.Global(G0));
        Assert.Equal(2, run.Global(G1));
        Assert.Equal(0, run.Global(G2));
        Assert.Equal(1, run.Global(0x13));
    }

    [Fact]
    public void ReadsAndWritesProperties()
    {
        var run = Execute(new Assembler()
            .Long(Op.GetProp, Small(1), Small(5)).Store(G0)
            .Long(Op.GetProp, Small(1), Small(2)).Store(G1)
            .Long(Op.GetProp, Small(2), Small(5)).Store(G2)
            .Variable(Op.PutProp, true, Small(1), Small(5), Large(0xABCD))
            .Variable(Op.PutProp, true, Small(1), Small(2), Large(0xFFFF))
            .Long(Op.GetProp, Small(1), Small(5)).Store(0x13)
            .Long(Op.GetProp, Small(1), Small(2)).Store(0x14)
            .Long(Op.GetPropAddr, Small(1), Small(5)).Store(0x15)
            .Long(Op.GetPropAddr, Small(2), Small(5)).Store(0x16)
            .Short1(Op.GetPropLen, Var(0x15)).Store(0x17)
            .Short1(Op.GetPropLen, Small(0)).Store(0x18)
            .Long(Op.GetNextProp, Small(1), Small(0)).Store(0x19)
            .Long(Op.GetNextProp, Small(1), Small(5)).Store(0x1A)
            .Long(Op.GetNextProp, Small(1), Small(2)).Store(0x1B)
            .Quit());

        // [zm op:get_prop] Two bytes as a word, one byte as itself, and
        // the default of $0505 for the lamp, which lacks property 5.
        Assert.Equal(0x1234, run.Global(G0));
        Assert.Equal(0x77, run.Global(G1));
        Assert.Equal(0x0505, run.Global(G2));

        // [zm op:put_prop] A word, and only the low byte of -1 into the
        // one-byte property.
        Assert.Equal(0xABCD, run.Global(0x13));
        Assert.Equal(0xFF, run.Global(0x14));

        // [zm op:get_prop_addr] and [zm op:get_prop_len], including 0.
        Assert.NotEqual(0, run.Global(0x15));
        Assert.Equal(0, run.Global(0x16));
        Assert.Equal(2, run.Global(0x17));
        Assert.Equal(0, run.Global(0x18));

        // [zm op:get_next_prop] First is 5, after 5 is 2, after 2 is none.
        Assert.Equal(5, run.Global(0x19));
        Assert.Equal(2, run.Global(0x1A));
        Assert.Equal(0, run.Global(0x1B));
    }

    [Fact]
    public void ObjectZeroIsReportedOnceAndTreatedAsNothing()
    {
        // [zm A] The default level reports the first instance of each
        // kind of error and carries on. get_child 0 twice is one kind.
        var run = Execute(new Assembler()
            .Short1(Op.GetChild, Small(0)).Store(G0).Branch(true, SkipStore)
            .Long(Op.Store, Small(G1), Small(1))
            .Short1(Op.GetChild, Small(0)).Store(G2).Branch(true, 2)
            .Long(Op.Jin, Small(0), Small(1)).Branch(false, SkipStore)
            .Long(Op.Store, Small(0x13), Small(1))
            .Quit());

        Assert.Equal(0, run.Global(G0));
        Assert.Equal(1, run.Global(G1));
        Assert.Equal(0, run.Global(0x13));

        var errors = run.Interpreter.RuntimeErrors;
        Assert.Equal(2, errors.Count);
        Assert.Contains("get_child", errors[0]);
        Assert.Contains("jin", errors[1]);
    }

    [Fact]
    public void PullingFromAnEmptyStackIsReportedAndGivesZero()
    {
        // [zm 6.3.2] and [zm op:pull deviates] Zork I release 2 does this,
        // so it is a runtime error and the missing value is 0, whether
        // pulled by the opcode, read as an operand, or seen in place.
        var run = Execute(new Assembler()
            .Long(Op.Store, Small(G0), Small(7))
            .Variable(Op.Pull, true, Small(G0))
            .Long(Op.Add, Var(0), Small(5)).Store(G1)
            .Short1(Op.Load, Small(0)).Store(G2)
            .Quit());

        Assert.Equal(0, run.Global(G0));
        Assert.Equal(5, run.Global(G1));
        Assert.Equal(0, run.Global(G2));

        var errors = run.Interpreter.RuntimeErrors;
        Assert.Equal(3, errors.Count);
        Assert.All(errors, e => Assert.Contains("stack is empty", e));
    }

    [Fact]
    public void PullingFromAnEmptyStackIsFatalAtTheStrictestLevel()
    {
        var story = new Story(ZMachineVersion.V5);
        story.Put(Code, new Assembler().Variable(Op.Pull, true, Small(G0)).Quit().ToArray());

        var memory = new ZMemory(story.Bytes);
        var interpreter = new Interpreter(memory, new TextWriterScreen(new StringWriter()), new ScriptedInput())
        {
            ErrorLevel = ErrorLevel.Fatal,
        };

        Assert.Throws<InvalidOperationException>(() => interpreter.Run(10));
    }

    [Fact]
    public void ObjectZeroIsFatalAtTheStrictestLevel()
    {
        // [zm A] Treat every error as fatal and end the game.
        var story = new Story(ZMachineVersion.V5);
        story.Put(Code, new Assembler().Short1(Op.GetParent, Small(0)).Store(G0).Quit().ToArray());

        var memory = new ZMemory(story.Bytes);
        var interpreter = new Interpreter(memory, new TextWriterScreen(new StringWriter()), new ScriptedInput())
        {
            ErrorLevel = ErrorLevel.Fatal,
        };

        Assert.Throws<InvalidOperationException>(() => interpreter.Run(10));
    }

    [Fact]
    public void GetPropAddrAnswersZeroForPropertyNumbersOutOfRange()
    {
        // Inform 6's individual properties are numbered from 64, and its
        // library asks get_prop_addr about them expecting 0.
        var run = Execute(new Assembler()
            .Long(Op.GetPropAddr, Small(1), Small(81)).Store(G0)
            .Quit());

        Assert.Equal(0, run.Global(G0));
        Assert.Empty(run.Interpreter.RuntimeErrors);
    }

    [Fact]
    public void PutPropOnAMissingPropertyIsAnError()
    {
        // [zm op:put_prop]
        var code = new Assembler().Variable(Op.PutProp, true, Small(2), Small(5), Small(1)).Quit();

        Assert.Throws<InvalidOperationException>(() => Execute(code));
    }

    [Fact]
    public void PrintsInlineTextAndNumbersAndCharacters()
    {
        var run = Execute(new Assembler()
            .Short0(Op.Print).Text("hello")
            .Variable(Op.PrintChar, true, Small(' '))
            .Variable(Op.PrintNum, true, Large(-42 & 0xFFFF))
            .Short0(Op.NewLine)
            .Short1(Op.PrintObj, Small(2))
            .Quit());

        // [zm op:print], [zm op:print_char], [zm op:print_num] signed,
        // [zm op:new_line], [zm op:print_obj]
        Assert.Equal("hello -42\nlamp", run.Output);
    }

    [Fact]
    public void PrintsStringsByByteAndPackedAddress()
    {
        var run = Execute(new Assembler()
            .Short1(Op.PrintAddr, Large(Strings))
            .Short1(Op.PrintPaddr, Large(Strings / 4))
            .Quit(), setup: story => story.Put(Strings, ZChars.Text("zork")));

        // [zm op:print_addr] and [zm op:print_paddr]
        Assert.Equal("zorkzork", run.Output);
    }

    [Fact]
    public void PrintRetPrintsANewlineAndReturnsTrue()
    {
        var run = Execute(new Assembler()
            .Variable(Op.Call, true, Large(RoutineA / 4)).Store(G0)
            .Quit(), setup: story => story.Routine(RoutineA, 0, new Assembler()
                .Short0(Op.PrintRet).Text("done")
                .ToArray()));

        // [zm op:print_ret]
        Assert.Equal("done\n", run.Output);
        Assert.Equal(1, run.Global(G0));
    }

    [Fact]
    public void RandomFollowsTheRisingSequenceWhenSeededLow()
    {
        var run = Execute(new Assembler()
            .Variable(Op.Random, true, Large(-3 & 0xFFFF)).Store(G0)
            .Variable(Op.Random, true, Small(100)).Store(G1)
            .Variable(Op.Random, true, Small(100)).Store(G2)
            .Variable(Op.Random, true, Small(100)).Store(0x13)
            .Variable(Op.Random, true, Small(100)).Store(0x14)
            .Variable(Op.Random, true, Small(2)).Store(0x15)
            .Quit());

        // [zm op:random] A negative range seeds and returns 0. [zm 2.4.2]
        // With a seed of 3 the remarks' sequence is 1, 2, 3, 1, ... and
        // then 2 reduced into a range of 2 is 2.
        Assert.Equal(0, run.Global(G0));
        Assert.Equal(1, run.Global(G1));
        Assert.Equal(2, run.Global(G2));
        Assert.Equal(3, run.Global(0x13));
        Assert.Equal(1, run.Global(0x14));
        Assert.Equal(2, run.Global(0x15));
    }

    [Fact]
    public void RandomInRangeStaysInRange()
    {
        var run = Execute(new Assembler()
            .Variable(Op.Random, true, Small(6)).Store(G0)
            .Variable(Op.Random, true, Small(6)).Store(G1)
            .Variable(Op.Random, true, Small(6)).Store(G2)
            .Quit());

        // [zm 2.4.1]
        Assert.All([run.Global(G0), run.Global(G1), run.Global(G2)], v => Assert.InRange(v, 1, 6));
    }

    [Fact]
    public void CopiesTablesForwardBackwardAndZeroes()
    {
        var run = Execute(new Assembler()
            .Variable(Op.CopyTable, true, Large(0x0300), Large(0x0302), Small(4))
            .Variable(Op.CopyTable, true, Large(0x0310), Large(0x030E), Small(4))
            .Variable(Op.CopyTable, true, Large(0x0320), Large(0x0321), Large(-3 & 0xFFFF))
            .Variable(Op.CopyTable, true, Large(0x0330), Small(0), Small(2))
            .Quit(), setup: story =>
            {
                story.Put(0x0300, [1, 2, 3, 4]);
                story.Put(0x0310, [5, 6, 7, 8]);
                story.Put(0x0320, [9, 0, 0, 0]);
                story.Put(0x0330, [0xAA, 0xBB]);
            });

        var bytes = run.Story.Bytes;

        // [zm op:copy_table] Overlapping forward with a positive size is
        // protected, so the destination gets the original 1 2 3 4.
        Assert.Equal([1, 2, 1, 2, 3, 4], bytes[0x0300..0x0306]);

        // Overlapping the other way, also protected.
        Assert.Equal([5, 6, 7, 8, 7, 8], bytes[0x030E..0x0314]);

        // A negative size copies forward regardless, smearing the 9 along:
        // this is the Beyond Zork fill.
        Assert.Equal([9, 9, 9, 9], bytes[0x0320..0x0324]);

        // A second address of 0 zeroes the first.
        Assert.Equal([0, 0], bytes[0x0330..0x0332]);
    }

    [Fact]
    public void ScansTablesForWordsAndBytes()
    {
        var run = Execute(new Assembler()
            .Variable(Op.ScanTable, true, Large(0x0007), Large(0x0300), Small(3)).Store(G0).Branch(true, SkipStore)
            .Long(Op.Store, Small(G2), Small(1))
            .Variable(Op.ScanTable, true, Large(0x0009), Large(0x0300), Small(3)).Store(G1).Branch(false, SkipStore)
            .Long(Op.Store, Small(0x13), Small(1))
            .Variable(Op.ScanTable, true, Small(0x33), Large(0x0310), Small(4), Small(0x02)).Store(0x14).Branch(true, 2)
            .Quit(), setup: story =>
            {
                story.PutWord(0x0300, 5);
                story.PutWord(0x0302, 7);
                story.PutWord(0x0304, 9);
                story.Put(0x0310, [0x11, 0x00, 0x22, 0x00, 0x33, 0x00, 0x44, 0x00]);
            });

        // [zm op:scan_table] Found at the second word, so its address and
        // a branch on true, skipping the store. 9 is the third word, and
        // found means the branch on false is not taken, so that store
        // runs. A byte search with two-byte fields finds $33 in the third
        // field.
        Assert.Equal(0x0302, run.Global(G0));
        Assert.Equal(0, run.Global(G2));
        Assert.Equal(0x0304, run.Global(G1));
        Assert.Equal(1, run.Global(0x13));
        Assert.Equal(0x0314, run.Global(0x14));
    }

    [Fact]
    public void EncodesTextIntoDictionaryForm()
    {
        var run = Execute(new Assembler()
            .Variable(Op.EncodeText, true, Large(0x0300), Small(5), Small(2), Large(0x0310))
            .Quit(), setup: story => story.Put(0x0300, "xxhello"u8.ToArray()));

        // [zm op:encode_text] Five characters starting two in: "hello",
        // encoded as the dictionary would store it.
        Assert.Equal(ZChars.Words(13, 10, 17, 17, 20, 5, 5, 5, 5), run.Story.Bytes[0x0310..0x0316]);
    }

    [Fact]
    public void VerifyAndPiracyBranch()
    {
        var run = Execute(new Assembler()
            .Short0(Op.Piracy).Branch(true, SkipStore)
            .Long(Op.Store, Small(G0), Small(1))
            .Short0(Op.Verify).Branch(false, SkipStore)
            .Long(Op.Store, Small(G1), Small(1))
            .Quit());

        // [zm op:piracy] Gullible, so the store is skipped. [zm op:verify]
        // This story records no length, so verification fails and the
        // branch on false is taken.
        Assert.Equal(0, run.Global(G0));
        Assert.Equal(0, run.Global(G1));
    }

    [Fact]
    public void RestartBeginsAgainWithFreshMemoryExceptFlags2()
    {
        // [zm 6.1.3] Nothing survives a restart except Flags 2, so that is
        // the only way a program can tell it has been here before. First
        // pass: the low byte of Flags 2 is 0, so set G0, set bit 1 of
        // Flags 2, and restart. Second pass: the byte is 2, so the branch
        // on false is taken over all of that to the quit.
        var run = Execute(new Assembler()
            .Variable(Op.Loadb, false, Large(0x10), Small(1)).Store(0)
            .Short1(Op.Jz, Var(0)).Branch(false, 12)
            .Long(Op.Store, Small(G0), Small(1))
            .Variable(Op.Storeb, true, Large(0x10), Small(1), Small(2))
            .Short0(Op.Restart)
            .Quit());

        // [zm op:restart] G0 came back from the story file as 0, while
        // the fixed-pitch bit stayed set.
        Assert.Equal(0, run.Global(G0));
        Assert.Equal(2, run.Story.Bytes[0x11]);
        Assert.True(run.Interpreter.InstructionsExecuted > 6);
    }

    [Fact]
    public void UnknownExtendedOpcodesAreIgnored()
    {
        // [zm 14.2.1] EXT:200 with no operands does nothing.
        var run = Execute(new Assembler()
            .Ext(200)
            .Long(Op.Store, Small(G0), Small(1))
            .Quit());

        Assert.Equal(1, run.Global(G0));
    }

    [Fact]
    public void OpcodesThatAreNotImplementedYetSaySo()
    {
        // [zm op:sound_effect] Sound is not built yet.
        var code = new Assembler().Variable(Op.SoundEffect, true, Small(1)).Quit();

        Assert.Throws<NotSupportedException>(() => Execute(code));
    }

    [Fact]
    public void UndoAnswersAsAnInterpreterWithoutItMust()
    {
        // [zm op:save_undo] -1 when undo cannot be provided, and
        // [zm op:restore_undo] a failed restore, 0, in the same case.
        var run = Execute(new Assembler()
            .Ext(9).Store(G0)
            .Ext(10).Store(G1)
            .Quit());

        Assert.Equal(0xFFFF, run.Global(G0));
        Assert.Equal(0, run.Global(G1));
        Assert.False(run.Interpreter.Header.Flags2.HasFlag(Flags2.WantsUndo));
    }

    [Fact]
    public void OperandsAreEvaluatedFirstToLast()
    {
        // [zm 4.5.2] The standard's example: with 10 pushed then 3, sub sp
        // sp is 3 - 10, the top minus the one beneath.
        var run = Execute(new Assembler()
            .Variable(Op.Push, true, Small(10))
            .Variable(Op.Push, true, Small(3))
            .Long(Op.Sub, Var(0), Var(0)).Store(G0)
            .Quit());

        Assert.Equal(-7, (short)run.Global(G0));
    }

    [Fact]
    public void StepReportsTheInstructionItRan()
    {
        var story = new Story(ZMachineVersion.V5);
        story.Put(Code, new Assembler().Short0(Op.Nop).Quit().ToArray());

        var memory = new ZMemory(story.Bytes);
        var interpreter = new Interpreter(memory, new TextWriterScreen(new StringWriter()), new ScriptedInput());

        Assert.Equal("nop", interpreter.Step().Name);
        Assert.Equal("quit", interpreter.Step().Name);
        Assert.True(interpreter.HasQuit);
        Assert.Throws<InvalidOperationException>(() => interpreter.Step());
    }

}

using Rezrov.ZMachine;
using Rezrov.ZMachine.Instructions;
using Rezrov.ZMachine.Text;

namespace Rezrov.Debugging;

/// <summary>
/// Finds the routines in a story file by trying every address one
/// could be packed to and keeping the ones that decode.
/// </summary>
/// <remarks>
/// [zm 5.1] Nothing in a story file says where the routines are. Three
/// ways of finding out were measured against the whole corpus before
/// this one was written, and the other two lost badly.
///
/// Following calls from the entry point finds about a tenth of high
/// memory, because these games dispatch most of their verbs through
/// action tables and call through a variable operand, so the address
/// is never named anywhere a reader can see it. Watching a game play
/// finds only what was played. Scanning finds everything shaped like a
/// routine, and pays for it by keeping the occasional thing that is
/// not one.
///
/// That price was measured too, against the only authority there is.
/// Every instruction a game really executes must land exactly on an
/// instruction boundary inside some scanned routine. Over the 59
/// walkthroughs in the repository, 101,230 instructions are executed
/// at distinct addresses: 27 of them land inside a scanned routine at
/// the wrong offset, and 942 land where no routine was found at all.
/// So the scan reads 99.97% of executed code correctly and finds 99%
/// of it, which is why the listing is built on it, and why where a
/// listing and a running game disagree, the running game is right.
/// </remarks>
public static class RoutineScan
{
    // A routine longer than this is not a routine, it is the scan
    // having walked off into data and kept going.
    private const int MostInstructions = 20_000;

    // What a byte no routine has claimed is marked with.
    private const int Nobody = -1;

    /// <summary>
    /// Every routine in high memory, in address order.
    /// </summary>
    public static IReadOnlyList<ScannedRoutine> Find(ZMemory memory, StoryHeader header)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(header);

        var decoder = new InstructionDecoder(memory, header, new ZTextDecoder(memory, header));
        var alignment = Alignment(header.Version);
        var found = new List<ScannedRoutine>();
        var body = new List<Instruction>(64);

        var at = Aligned(header.HighMemoryBase, alignment);

        while (at < memory.Length)
        {
            // A body of one instruction is nearly always a coincidence,
            // since any byte that reads as a return ends one. Requiring
            // two costs a handful of real routines, which the recovery
            // pass below then puts back.
            var routine = Read(decoder, memory, header, at, memory.Length, body, least: 2);

            if (routine is null)
            {
                at += alignment;
                continue;
            }

            found.Add(routine);
            at = Aligned(routine.EndAddress, alignment);
        }

        Recover(decoder, memory, header, found, body);

        found.Sort((a, b) => a.Address.CompareTo(b.Address));
        return found;
    }

    /// <summary>
    /// [zm 4] The opcodes that call a routine, whose first operand is
    /// the packed address of the routine called.
    /// </summary>
    internal static bool Calls(Opcode opcode) => opcode
        is Opcode.Call or Opcode.CallVs or Opcode.CallVs2 or Opcode.CallVn or Opcode.CallVn2
        or Opcode.Call1s or Opcode.Call1n or Opcode.Call2s or Opcode.Call2n;

    /// <summary>
    /// [zm 1.2.3] The boundary a routine can be packed to, which is
    /// also the step the scan walks high memory in.
    /// </summary>
    internal static int Alignment(ZMachineVersion version) => version switch
    {
        <= ZMachineVersion.V3 => 2,
        <= ZMachineVersion.V7 => 4,
        _ => 8,
    };

    /// <summary>Rounds an address up to a packing boundary.</summary>
    internal static int Aligned(int address, int alignment) =>
        address + ((alignment - (address % alignment)) % alignment);

    /// <summary>
    /// The routine at an address, or null where there is not one that
    /// decodes.
    /// </summary>
    /// <remarks>
    /// [zm 5.3] A routine has no end marker, so the end has to be
    /// worked out: decode forward from the header, remember the
    /// furthest address any branch or jump reaches, and stop at the
    /// first return or jump that nothing still points past. Anything
    /// that fails to decode, or runs off the end of the file, means
    /// this was not a routine.
    /// </remarks>
    /// <param name="least">
    /// The fewest instructions a body may have and still be believed.
    /// </param>
    private static ScannedRoutine? Read(
        InstructionDecoder decoder,
        ZMemory memory,
        StoryHeader header,
        int at,
        int limit,
        List<Instruction> body,
        int least)
    {
        if (at < 0 || at + 1 >= limit || memory.ReadByte(at) > RoutineHeader.MaxLocals)
        {
            return null;
        }

        RoutineHeader head;

        try
        {
            head = RoutineHeader.Read(memory, header.Version, at);
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidDataException)
        {
            return null;
        }

        body.Clear();

        var next = head.CodeAddress;
        var furthest = next;

        while (next < limit && body.Count < MostInstructions)
        {
            Instruction one;

            try
            {
                one = decoder.Decode(next);
            }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException
                or InvalidDataException or NotSupportedException)
            {
                return null;
            }

            if (one.NextAddress <= next || one.NextAddress > limit)
            {
                return null;
            }

            body.Add(one);

            // [zm 4.7.2] Where a branch goes, unless it returns rather
            // than branching, in which case it goes nowhere.
            if (one.Branch is { } branch && !branch.ReturnsFalse && !branch.ReturnsTrue)
            {
                furthest = Math.Max(furthest, branch.Target(one.NextAddress));
            }

            // [zm op:jump] The offset is measured the same way, and a
            // jump to a variable address tells us nothing.
            if (one.Opcode == Opcode.Jump && one.Operands.Count > 0
                && one.Operands[0].Type != OperandType.Variable)
            {
                furthest = Math.Max(furthest, one.NextAddress + (short)one.Operands[0].Value - 2);
            }

            next = one.NextAddress;

            if (Ends(one.Opcode) && next > furthest)
            {
                return body.Count >= least
                    ? new ScannedRoutine(at, head.InitialLocals, head.CodeAddress, body.ToArray())
                    : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Follows the calls the scan's own findings make, to add the
    /// routines it threw away and to correct the ones it began too
    /// early.
    /// </summary>
    /// <remarks>
    /// Something calling an address is evidence of a different kind
    /// from the address merely decoding, so it is worth taking, and a
    /// recovered routine may call another, which is why this works
    /// from a queue.
    ///
    /// It adds: of the 181 routines a full Zork I walkthrough enters,
    /// the scan alone finds 175, and every one of the six it misses is
    /// a single instruction long.
    ///
    /// It also corrects, and that is worth more. Where a call lands
    /// among the locals of a routine already found, the scan began
    /// that routine too early, because nothing calls into a table of
    /// locals. Zork I has one of these at $8276, where a run of text
    /// reads as a header of twelve locals and swallows the real
    /// routine at $827A. Letting the call win drops the instructions
    /// the corpus executes that the scan reads at the wrong offset
    /// from 43 to 27, at the cost of 15 more it does not read at all,
    /// which is the better of the two ways to be wrong.
    ///
    /// A call landing in the body of a routine is left alone, since a
    /// constant inside something that may itself be a coincidence is
    /// not enough to pull a real routine apart.
    /// </remarks>
    private static void Recover(
        InstructionDecoder decoder,
        ZMemory memory,
        StoryHeader header,
        List<ScannedRoutine> found,
        List<Instruction> body)
    {
        // Which routine owns each byte, by its place in the list, so a
        // routine can be taken back out again when a call proves the
        // scan read it wrongly. A place emptied this way is left null
        // and cleared out at the end.
        var slots = new List<ScannedRoutine?>(found);
        var owner = new int[memory.Length];

        Array.Fill(owner, Nobody);

        for (var i = 0; i < slots.Count; i++)
        {
            Claim(owner, i, slots[i]!);
        }

        var pending = new Queue<int>();
        var seen = new HashSet<int>();

        // [zm 5.5] In Version 6 alone the entry point is a routine to
        // call rather than an address to jump to, so it is named
        // outright. Every other version, Versions 7 and 8 included,
        // gives the byte address of the first instruction instead.
        if (header.Version == ZMachineVersion.V6)
        {
            Want(header.UnpackRoutineAddress(header.MainRoutinePackedAddress));
        }

        foreach (var routine in found)
        {
            foreach (var one in routine.Body)
            {
                Called(one);
            }
        }

        while (pending.Count > 0)
        {
            var target = pending.Dequeue();

            if (target < header.HighMemoryBase || target >= memory.Length)
            {
                continue;
            }

            var holder = owner[target];

            // Already found, and found at exactly this address.
            if (holder != Nobody && slots[holder]!.Address == target)
            {
                continue;
            }

            // Inside the body of a routine already found. A constant in
            // a routine that may itself be a coincidence is not enough
            // to pull a whole routine apart, so leave it.
            if (holder != Nobody && target >= slots[holder]!.CodeAddress)
            {
                continue;
            }

            var routine = Read(decoder, memory, header, target, memory.Length, body, least: 1);

            if (routine is null || Blocked(owner, routine, holder))
            {
                continue;
            }

            // The target fell in the locals of a routine already found,
            // and nothing calls into a table of locals. So the scan
            // began that routine too early, and the call is right.
            if (holder != Nobody)
            {
                Release(owner, slots[holder]!);
                slots[holder] = null;
            }

            slots.Add(routine);
            Claim(owner, slots.Count - 1, routine);

            foreach (var one in routine.Body)
            {
                Called(one);
            }
        }

        found.Clear();
        found.AddRange(slots.Where(one => one is not null)!);

        void Called(Instruction one)
        {
            if (Calls(one.Opcode) && one.Operands.Count > 0
                && one.Operands[0].Type != OperandType.Variable)
            {
                Want(header.UnpackRoutineAddress(one.Operands[0].Value));
            }
        }

        void Want(int address)
        {
            if (seen.Add(address))
            {
                pending.Enqueue(address);
            }
        }
    }

    /// <summary>Marks every byte a routine occupies as its own.</summary>
    private static void Claim(int[] owner, int slot, ScannedRoutine routine)
    {
        for (var i = routine.Address; i < routine.EndAddress && i < owner.Length; i++)
        {
            owner[i] = slot;
        }
    }

    /// <summary>Gives back every byte a routine held.</summary>
    private static void Release(int[] owner, ScannedRoutine routine)
    {
        for (var i = routine.Address; i < routine.EndAddress && i < owner.Length; i++)
        {
            owner[i] = Nobody;
        }
    }

    /// <summary>
    /// Whether a recovered routine would run into one already found,
    /// counting the routine it is about to replace as out of the way.
    /// </summary>
    private static bool Blocked(int[] owner, ScannedRoutine routine, int replacing)
    {
        for (var i = routine.Address; i < routine.EndAddress; i++)
        {
            if (i >= owner.Length || (owner[i] != Nobody && owner[i] != replacing))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// [zm 6.4] The opcodes after which execution does not simply carry
    /// on to the next instruction.
    /// </summary>
    private static bool Ends(Opcode opcode) => opcode
        is Opcode.Ret or Opcode.Rtrue or Opcode.Rfalse or Opcode.RetPopped
        or Opcode.Jump or Opcode.Quit or Opcode.PrintRet or Opcode.Restart or Opcode.Throw;
}

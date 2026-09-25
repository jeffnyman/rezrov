using System.Globalization;
using System.Text;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Instructions;
using Rezrov.ZMachine.Text;

namespace Rezrov.Debugging;

/// <summary>
/// A story file read as a listing: every routine the scan found, in
/// address order, with the stretches between them accounted for.
/// </summary>
/// <remarks>
/// A listing is written to be read, so it says what an address means
/// wherever it can. A branch that stays inside its routine gets a
/// label rather than a number, a call to a constant address says which
/// routine is called, and text that a print opcode names by address is
/// shown beside it. Where an address cannot be resolved, because the
/// operand is a variable or the target is somewhere else entirely, the
/// number is printed as it stands rather than guessed at.
/// </remarks>
public sealed class Disassembly
{
    // Enough of a string to recognize it by, which is all a listing
    // needs. The whole text is in the file for anyone who wants it.
    private const int MostText = 56;

    private readonly ZMemory _memory;
    private readonly StoryHeader _header;
    private readonly ZTextDecoder _text;

    private Disassembly(
        ZMemory memory,
        StoryHeader header,
        ZTextDecoder text,
        IReadOnlyList<ScannedRoutine> routines,
        IReadOnlyList<CodeGap> gaps)
    {
        _memory = memory;
        _header = header;
        _text = text;
        Routines = routines;
        Gaps = gaps;
    }

    /// <summary>Every routine found, in address order.</summary>
    public IReadOnlyList<ScannedRoutine> Routines { get; }

    /// <summary>
    /// Every stretch of high memory no routine covers, in address
    /// order. Alignment padding between routines is not a gap and is
    /// counted in <see cref="Padding"/> instead.
    /// </summary>
    public IReadOnlyList<CodeGap> Gaps { get; }

    /// <summary>
    /// [zm 1.2.3] How many bytes are lost to packing routines onto
    /// their boundaries. These belong to nothing and mean nothing.
    /// </summary>
    public int Padding { get; private init; }

    /// <summary>Reads a story file as a listing.</summary>
    public static Disassembly Of(ZMemory memory, StoryHeader header)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(header);

        var text = new ZTextDecoder(memory, header);
        var routines = RoutineScan.Find(memory, header);
        var alignment = RoutineScan.Alignment(header.Version);
        var gaps = new List<CodeGap>();

        var at = (int)header.HighMemoryBase;
        var padding = 0;

        foreach (var routine in routines)
        {
            // A stretch shorter than the packing boundary is the
            // boundary, not a gap in the listing.
            if (routine.Address - at >= alignment)
            {
                gaps.Add(Gap(text, at, routine.Address, alignment));
            }
            else
            {
                padding += routine.Address - at;
            }

            at = Math.Max(at, routine.EndAddress);
        }

        if (memory.Length - at >= alignment)
        {
            gaps.Add(Gap(text, at, memory.Length, alignment));
        }
        else
        {
            padding += memory.Length - at;
        }

        return new Disassembly(memory, header, text, routines, gaps) { Padding = padding };
    }

    /// <summary>Writes the whole listing.</summary>
    public void Write(TextWriter to)
    {
        ArgumentNullException.ThrowIfNull(to);

        Preamble(to);

        var gaps = new Queue<CodeGap>(Gaps);

        foreach (var routine in Routines)
        {
            while (gaps.Count > 0 && gaps.Peek().Address < routine.Address)
            {
                WriteGap(to, gaps.Dequeue());
            }

            WriteRoutine(to, routine);
        }

        while (gaps.Count > 0)
        {
            WriteGap(to, gaps.Dequeue());
        }
    }

    /// <summary>Writes one routine, header line and body.</summary>
    public void WriteRoutine(TextWriter to, ScannedRoutine routine)
    {
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(routine);

        var labels = Labels(routine);

        to.WriteLine();
        to.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"routine {Hex(routine.Address)}, {Many(routine.LocalCount, "local")}{Initials(routine)}"));

        foreach (var one in routine.Body)
        {
            to.WriteLine(Line(one, labels));
        }
    }

    /// <summary>One instruction as the listing shows it.</summary>
    public string Line(Instruction one, IReadOnlyDictionary<int, string> labels)
    {
        ArgumentNullException.ThrowIfNull(one);
        ArgumentNullException.ThrowIfNull(labels);

        var line = new StringBuilder();

        line.Append((labels.TryGetValue(one.Address, out var label) ? label + ":" : string.Empty).PadRight(5));
        line.Append(Hex(one.Address).PadLeft(6));
        line.Append("  ");
        line.Append(one.Name.PadRight(14));

        var body = new StringBuilder();

        // [zm op:jump] The operand is an offset, so showing it as a
        // number would say nothing. Where it goes is the useful part.
        if (one.Opcode == Opcode.Jump && Constant(one, 0) is { } jump)
        {
            body.Append(Where(one.NextAddress + (short)jump - 2, labels));
        }
        else
        {
            for (var i = 0; i < one.Operands.Count; i++)
            {
                if (i > 0)
                {
                    body.Append(", ");
                }

                body.Append(Shown(one, i));
            }
        }

        // [zm 4.6] Where the result goes.
        if (one.StoreVariable is { } store)
        {
            body.Append(" -> ").Append(Operand.VariableName(store));
        }

        // [zm 4.7] Which way the branch runs, and where to.
        if (one.Branch is { } branch)
        {
            body.Append(branch.OnTrue ? " ?" : " ?~");
            body.Append(
                branch.ReturnsFalse ? "rfalse"
                : branch.ReturnsTrue ? "rtrue"
                : Where(branch.Target(one.NextAddress), labels));
        }

        // [zm 4.8] The text print and print_ret carry inline. Those
        // two opcodes have no operands, so the text is all there is.
        if (one.TextAddress is { } inline)
        {
            if (body.Length > 0)
            {
                body.Append(' ');
            }

            body.Append(Quoted(Read(inline)));
        }

        line.Append(body);

        if (Note(one) is { } note)
        {
            line.Append("  ; ").Append(note);
        }

        return line.ToString().TrimEnd();
    }

    /// <summary>
    /// A name for every branch and jump target inside a routine that
    /// lands on an instruction, so the body can be read without
    /// following addresses by hand.
    /// </summary>
    public static IReadOnlyDictionary<int, string> Labels(ScannedRoutine routine)
    {
        ArgumentNullException.ThrowIfNull(routine);

        var boundaries = new HashSet<int>();

        foreach (var one in routine.Body)
        {
            boundaries.Add(one.Address);
        }

        var targets = new SortedSet<int>();

        foreach (var one in routine.Body)
        {
            if (one.Branch is { } branch && !branch.ReturnsFalse && !branch.ReturnsTrue)
            {
                Consider(branch.Target(one.NextAddress));
            }

            if (one.Opcode == Opcode.Jump && Constant(one, 0) is { } jump)
            {
                Consider(one.NextAddress + (short)jump - 2);
            }
        }

        var labels = new Dictionary<int, string>();
        var number = 1;

        foreach (var target in targets)
        {
            labels[target] = string.Create(CultureInfo.InvariantCulture, $"L{number++}");
        }

        return labels;

        void Consider(int target)
        {
            // A target that is not the start of an instruction is
            // either the scan having misread the routine or a jump into
            // data. Either way there is nothing to hang a name on.
            if (boundaries.Contains(target))
            {
                targets.Add(target);
            }
        }
    }

    /// <summary>
    /// One operand as the listing shows it, which for most of them is
    /// what the operand itself says.
    /// </summary>
    /// <remarks>
    /// [zm 4.2.3] A few opcodes take a variable by reference: the
    /// operand is not a value but the number of the variable to work
    /// on, stored as a small constant. Printing that as a number would
    /// hide what the instruction does, so it is printed as the variable
    /// it names. Where the operand is itself a variable, the number
    /// comes from that variable at run time and cannot be resolved
    /// here, so it is shown in brackets to say the reference is one
    /// step further off.
    /// </remarks>
    private static string Shown(Instruction one, int index)
    {
        var operand = one.Operands[index];

        if (index != 0 || !ByReference(one.Opcode))
        {
            return operand.ToString();
        }

        return operand.Type == OperandType.Variable
            ? string.Concat("[", Operand.VariableName(operand.Value), "]")
            : Operand.VariableName(operand.Value);
    }

    /// <summary>
    /// [zm 4.2.3] The opcodes whose first operand is the number of a
    /// variable rather than a value.
    /// </summary>
    private static bool ByReference(Opcode opcode) => opcode
        is Opcode.Inc or Opcode.Dec or Opcode.IncChk or Opcode.DecChk
        or Opcode.Store or Opcode.Load or Opcode.Pull;

    /// <summary>
    /// The value of an operand where it is a constant, or null where it
    /// is a variable and nothing can be said about it without running
    /// the game.
    /// </summary>
    private static ushort? Constant(Instruction one, int index) =>
        index < one.Operands.Count && one.Operands[index].Type != OperandType.Variable
            ? one.Operands[index].Value
            : null;

    private static string Hex(int value) => value.ToString("X4", CultureInfo.InvariantCulture);

    /// <summary>A count and what it counts, read as English.</summary>
    private static string Many(int number, string noun) => string.Create(
        CultureInfo.InvariantCulture,
        $"{number} {noun}{(number == 1 ? string.Empty : "s")}");

    /// <summary>
    /// How many encoded strings exactly fill a gap, or 0 where they do
    /// not.
    /// </summary>
    private static CodeGap Gap(ZTextDecoder text, int from, int to, int alignment)
    {
        var at = from;
        var strings = 0;

        while (at < to)
        {
            int next;

            try
            {
                next = text.SkipString(at);
            }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException)
            {
                return new CodeGap(from, to - from, 0);
            }

            if (next <= at || next > to)
            {
                return new CodeGap(from, to - from, 0);
            }

            strings++;
            at = RoutineScan.Aligned(next, alignment);
        }

        return new CodeGap(from, to - from, strings);
    }

    private void Preamble(TextWriter to)
    {
        to.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"; Version {(int)_header.Version}, release {_header.Release}, serial {_header.SerialCode}, checksum {Hex(_header.Checksum)}"));

        to.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"; dynamic memory at {Hex(0)}, static at {Hex(_header.StaticMemoryBase)}, high at {Hex(_header.HighMemoryBase)}"));

        to.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"; {_memory.Length} bytes in the file, execution begins at {Hex(Entry())}"));

        to.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"; {Many(Routines.Count, "routine")}, {Many(Gaps.Count, "gap")}, {Many(Padding, "byte")} of padding"));
    }

    /// <summary>
    /// [zm 5.5] Where a game starts: a routine to call in Version 6,
    /// and in every other version, Versions 7 and 8 included, the byte
    /// address of the first instruction.
    /// </summary>
    private int Entry() => _header.Version == ZMachineVersion.V6
        ? _header.UnpackRoutineAddress(_header.MainRoutinePackedAddress)
        : _header.InitialProgramCounter;

    private static void WriteGap(TextWriter to, CodeGap gap)
    {
        to.WriteLine();
        to.WriteLine(gap.Strings > 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"gap {Hex(gap.Address)}, {Many(gap.Length, "byte")}, {Many(gap.Strings, "string")}")
            : string.Create(CultureInfo.InvariantCulture, $"gap {Hex(gap.Address)}, {Many(gap.Length, "byte")}"));
    }

    /// <summary>
    /// [zm 5.2.1] The values the locals start at, which only Versions 1
    /// to 4 carry and which are usually all zero even there.
    /// </summary>
    private static string Initials(ScannedRoutine routine)
    {
        if (routine.InitialLocals.All(value => value == 0))
        {
            return string.Empty;
        }

        return " = " + string.Join(", ", routine.InitialLocals.Select(value => Hex(value)));
    }

    private static string Where(int target, IReadOnlyDictionary<int, string> labels) =>
        labels.TryGetValue(target, out var label) ? label : Hex(target);

    /// <summary>
    /// What an instruction is worth saying about it beyond its
    /// operands: which routine a call reaches, or what a print by
    /// address prints.
    /// </summary>
    private string? Note(Instruction one)
    {
        if (RoutineScan.Calls(one.Opcode) && Constant(one, 0) is { } called)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"routine {Hex(_header.UnpackRoutineAddress(called))}");
        }

        if (one.Opcode == Opcode.PrintPaddr && Constant(one, 0) is { } packed)
        {
            return Quoted(Read(_header.UnpackStringAddress(packed)));
        }

        if (one.Opcode == Opcode.PrintAddr && Constant(one, 0) is { } plain)
        {
            return Quoted(Read(plain));
        }

        return null;
    }

    private string? Read(int address)
    {
        try
        {
            return _text.Decode(address);
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Text as a listing shows it: on one line, with a newline written
    /// the way Inform writes one, and cut short if it runs long.
    /// </summary>
    private static string Quoted(string? text)
    {
        if (text is null)
        {
            return "?";
        }

        var flat = text.Replace('\n', '^').Replace('\r', '^');

        return flat.Length > MostText
            ? string.Concat("\"", flat.AsSpan(0, MostText), "...\"")
            : string.Concat("\"", flat, "\"");
    }
}

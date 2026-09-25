using System.Globalization;
using System.Text;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Instructions;

namespace Rezrov.Debugging;

/// <summary>
/// A debugger for a Z-machine game: something that takes a typed line
/// and gives back what to print.
/// </summary>
/// <remarks>
/// Nothing here reads a keyboard or writes to a screen. A session is
/// handed a line and hands back the text of its answer, which is what
/// lets the whole debugger be tested by typing at it. The game's own
/// output happens underneath, wherever the frontend put it, as the
/// game runs.
///
/// Addresses are written in hexadecimal, with or without a leading $
/// or 0x, because every address the listing shows is in hexadecimal
/// and a debugger that wanted them in decimal would be asking the
/// reader to convert.
/// </remarks>
public sealed class DebugSession
{
    // How much of a routine to show around an address: enough to see
    // how the machine arrived and where it is going, without burying
    // the prompt.
    private const int Before = 4;
    private const int After = 9;

    private static readonly Dictionary<int, string> NoLabels = [];

    private readonly Interpreter _machine;
    private readonly Disassembly _listing;

    public DebugSession(Interpreter machine, Disassembly listing)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(listing);

        _machine = machine;
        _listing = listing;
    }

    /// <summary>Whether the player has asked to stop debugging.</summary>
    public bool Finished { get; private set; }

    /// <summary>
    /// Carries out one typed line and returns what to print, which is
    /// empty when there is nothing to say.
    /// </summary>
    public string Obey(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
        {
            return string.Empty;
        }

        var argument = words.Length > 1 ? words[1] : null;

        try
        {
            return words[0].ToLowerInvariant() switch
            {
                "b" or "break" => Break(argument),
                "d" or "delete" => Delete(argument),
                "breaks" => Breaks(),
                "s" or "step" => Step(),
                "n" or "next" => Next(),
                "f" or "finish" => Finish(),
                "c" or "continue" => Went(_machine.HasQuit ? StopReason.Quit : _machine.Continue()),
                "w" or "where" => Where(),
                "l" or "list" => List(argument),
                "locals" => Locals(),
                "globals" => Globals(argument),
                "stack" => Stack(),
                "read" => Read(argument),
                "q" or "quit" => Quit(),
                "h" or "help" or "?" => Help(),
                _ => $"there is no {words[0]} command. Type help for the ones there are.",
            };
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException
            or NotSupportedException or IndexOutOfRangeException or ArgumentException)
        {
            // A game can stop in the middle of an instruction, and when
            // it does the debugger is the one thing that should still
            // be standing.
            return $"the game stopped: {e.Message}";
        }
    }

    /// <summary>
    /// Where the machine is now, as the one line a prompt is preceded
    /// by.
    /// </summary>
    public string Stopped()
    {
        if (_machine.HasQuit)
        {
            return "the game has quit.";
        }

        var at = _machine.State.ProgramCounter;
        var routine = _listing.RoutineAt(at);

        try
        {
            return _listing.Line(
                _machine.Decoder.Decode(at),
                routine is null ? NoLabels : Disassembly.Labels(routine));
        }
        catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException)
        {
            return Say($"{Hex(at)} does not decode as an instruction");
        }
    }

    private string Break(string? argument)
    {
        // The one place a debugger nearly always wants to stop is
        // wherever the game asks the player what to do next, and the
        // listing knows all of them, so it does not have to be found
        // by hand.
        if (string.Equals(argument, "reads", StringComparison.OrdinalIgnoreCase))
        {
            var reads = Reads().ToList();
            var added = reads.Count(_machine.Breakpoints.Add);

            return reads.Count == 0
                ? Say("the listing found nowhere that the game reads a command")
                : Say($"stopping at {added} of the {reads.Count} places the game reads a command");
        }

        if (Address(argument) is not { } address)
        {
            return Wanted("break", "an address, or reads for wherever the game takes a command");
        }

        // [zm 5.2] A routine begins with its locals, so the address the
        // listing shows for a routine is not an address the program
        // counter ever holds. Someone who types it means the routine.
        if (_listing.RoutineAt(address) is { } routine && routine.Address == address)
        {
            return _machine.Breakpoints.Add(routine.CodeAddress)
                ? Say($"stopping at {Hex(routine.CodeAddress)}, where routine {Hex(address)} begins")
                : Say($"already stopping at {Hex(routine.CodeAddress)}, where routine {Hex(address)} begins");
        }

        var said = _machine.Breakpoints.Add(address)
            ? Say($"stopping at {Hex(address)}{In(address)}")
            : Say($"already stopping at {Hex(address)}");

        // A breakpoint the program counter can never hold would simply
        // never happen, and silence about that would be unkind.
        return Begins(address)
            ? said
            : said + Environment.NewLine + Say("no instruction begins there as far as the listing knows");
    }

    /// <summary>
    /// [zm op:sread] and [zm op:aread] Every instruction that stops to
    /// take a command from the player.
    /// </summary>
    private IEnumerable<int> Reads() => _listing.Routines
        .SelectMany(routine => routine.Body)
        .Where(one => one.Opcode is Opcode.Sread or Opcode.Aread)
        .Select(one => one.Address);

    /// <summary>
    /// Whether the listing says an instruction begins at an address.
    /// </summary>
    private bool Begins(int address) =>
        _listing.RoutineAt(address)?.Body.Any(one => one.Address == address) ?? false;

    private string Delete(string? argument)
    {
        if (argument is null)
        {
            var many = _machine.Breakpoints.Count;
            _machine.Breakpoints.Clear();

            return Say($"{many} {(many == 1 ? "breakpoint" : "breakpoints")} gone");
        }

        if (Address(argument) is not { } address)
        {
            return Wanted("delete", "an address, or nothing at all to clear them");
        }

        return _machine.Breakpoints.Remove(address)
            ? Say($"no longer stopping at {Hex(address)}")
            : Say($"there was no breakpoint at {Hex(address)}");
    }

    private string Breaks()
    {
        if (_machine.Breakpoints.Count == 0)
        {
            return Say("nothing is being stopped at");
        }

        var said = new StringBuilder();

        foreach (var address in _machine.Breakpoints.Order())
        {
            said.AppendLine(Say($"{Hex(address)}{In(address)}"));
        }

        return said.ToString().TrimEnd();
    }

    private string Step()
    {
        if (_machine.HasQuit)
        {
            return "the game has quit.";
        }

        _machine.Step();

        return Stopped();
    }

    private string Next()
    {
        if (_machine.HasQuit)
        {
            return "the game has quit.";
        }

        // Stepping over a call is stepping once and, where that went
        // into a routine, running until it comes back out.
        var depth = _machine.State.FrameNumber;
        _machine.Step();

        return !_machine.HasQuit && _machine.State.FrameNumber > depth
            ? Went(_machine.Continue(unwind: depth))
            : Stopped();
    }

    private string Finish()
    {
        if (_machine.HasQuit)
        {
            return "the game has quit.";
        }

        // [zm 5.4] The outermost routine never returns, so there is
        // nothing to run to the end of.
        return _machine.State.FrameNumber <= 1
            ? Say("this is the routine the game started in, and it does not return")
            : Went(_machine.Continue(unwind: _machine.State.FrameNumber - 1));
    }

    private string Went(StopReason why) => why switch
    {
        StopReason.Quit => "the game has quit.",
        StopReason.Breakpoint => Say("stopped") + Environment.NewLine + Stopped(),
        _ => Stopped(),
    };

    private string Where()
    {
        var said = new StringBuilder();

        foreach (var entry in CallChain.Of(_machine.State, _listing))
        {
            var routine = entry.Routine is { } address
                ? $"routine {Hex(address)}"
                : "no routine the listing knows";

            // Saying which answer this is matters: the machine records
            // the routine it called, and a listing works one out.
            said.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{entry.Depth,3}  {Hex(entry.Address)}  in {routine}{(entry.Exact ? string.Empty : ", from the listing")}"));
        }

        return said.ToString().TrimEnd();
    }

    private string List(string? argument)
    {
        if (argument is not null && Address(argument) is null)
        {
            return Wanted("list", "an address, or nothing at all for wherever the game is");
        }

        var at = Address(argument) ?? _machine.State.ProgramCounter;
        var here = _machine.State.ProgramCounter;
        var routine = _listing.RoutineAt(at);
        var said = new StringBuilder();

        if (routine is null)
        {
            // Nothing claims this address, so read forward from it and
            // say plainly that this is reading rather than listing.
            said.AppendLine(Say($"no routine covers {Hex(at)}, so this is read straight from there"));

            var next = at;

            for (var i = 0; i < After && next < _machine.State.Memory.Length; i++)
            {
                Instruction one;

                try
                {
                    one = _machine.Decoder.Decode(next);
                }
                catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException)
                {
                    said.AppendLine(Say($"{Hex(next)} does not decode as an instruction"));
                    break;
                }

                said.AppendLine(Mark(next == here) + _listing.Line(one, NoLabels));
                next = one.NextAddress;
            }

            return said.ToString().TrimEnd();
        }

        var labels = Disassembly.Labels(routine);
        var body = routine.Body;
        var index = 0;

        while (index < body.Count - 1 && body[index].NextAddress <= at)
        {
            index++;
        }

        said.AppendLine(Say(
            $"routine {Hex(routine.Address)}, {routine.LocalCount} {(routine.LocalCount == 1 ? "local" : "locals")}"));

        for (var i = Math.Max(0, index - Before); i < Math.Min(body.Count, index + After); i++)
        {
            said.AppendLine(Mark(body[i].Address == here) + _listing.Line(body[i], labels));
        }

        return said.ToString().TrimEnd();
    }

    private string Locals()
    {
        var locals = _machine.State.CurrentFrame.Locals;

        if (locals.Length == 0)
        {
            return Say("this routine has no locals");
        }

        return Columns(locals.Select((value, i) => $"{Operand.VariableName(i + 1)} {Hex(value)}"));
    }

    private string Globals(string? argument)
    {
        if (argument is not null)
        {
            // [zm 4.2.2] Globals are variables $10 upward, and the
            // listing names them from 0, so that is how they are asked
            // for here.
            if (Address(argument) is not { } number || number is < 0 or > 239)
            {
                return Wanted("globals", "a global from 0 to EF, or nothing at all");
            }

            return Say($"{Operand.VariableName(number + 16)} {Hex(_machine.State.ReadGlobal(number + 16))}");
        }

        // [zm 6.2] The globals are variables $10 upward, so a global's
        // own number and the variable number it is read by are sixteen
        // apart, which is the one place that has to be remembered.
        //
        // All 240 would be thirty lines of mostly nothing, and a global
        // that is still zero is one the game has not used.
        var set = Enumerable.Range(0, 240)
            .Select(number => (Number: number, Value: _machine.State.ReadGlobal(number + 16)))
            .Where(global => global.Value != 0)
            .Select(global => $"{Operand.VariableName(global.Number + 16)} {Hex(global.Value)}")
            .ToList();

        return set.Count == 0 ? Say("every global is zero") : Columns(set);
    }

    private string Stack()
    {
        var state = _machine.State;
        var mine = state.Stack.Skip(state.CurrentFrame.StackBase).ToList();

        if (mine.Count == 0)
        {
            return Say("this routine has pushed nothing");
        }

        // [zm 6.3.1] A routine sees only its own values, newest first
        // because that is the one the next pop takes.
        return Columns(Enumerable.Reverse(mine).Select(value => Hex(value)));
    }

    private string Read(string? argument)
    {
        if (Address(argument) is not { } address)
        {
            return Wanted("read", "an address in memory");
        }

        var memory = _machine.State.Memory;

        if (address < 0 || address >= memory.Length)
        {
            return Say($"{Hex(address)} is past the end of the {memory.Length} bytes there are");
        }

        var bytes = new StringBuilder();

        for (var i = address; i < Math.Min(address + 16, memory.Length); i++)
        {
            bytes.Append(CultureInfo.InvariantCulture, $"{memory.ReadByte(i):X2} ");
        }

        var word = address + 1 < memory.Length
            ? Hex(memory.ReadWord(address))
            : "half a word";

        return Say($"{Hex(address)}  {bytes.ToString().TrimEnd()}")
            + Environment.NewLine
            + Say($"byte {memory.ReadByte(address):X2}, word {word}");
    }

    private string Quit()
    {
        Finished = true;

        return "leaving the game.";
    }

    private static string Help() => string.Join(
        Environment.NewLine,
        "  break <address>     stop before the instruction there",
        "  break reads         stop wherever the game takes a command",
        "  delete [<address>]  stop doing that, or clear every breakpoint",
        "  breaks              what is being stopped at",
        "  step                carry out one instruction",
        "  next                the same, but over a call rather than into it",
        "  finish              run until this routine returns",
        "  continue            run on",
        "  where               the routines the game is inside, newest first",
        "  list [<address>]    the instructions around there, or around the game",
        "  locals              this routine's local variables",
        "  globals [<number>]  the globals a game has written, or one of them",
        "  stack               what this routine has pushed",
        "  read <address>      sixteen bytes of memory",
        "  quit                leave the game",
        "",
        "  Addresses are hexadecimal. Every command has its first letter",
        "  as a short form, except breaks, locals, globals, stack and read.");

    /// <summary>
    /// [zm 4] An address as the listing writes one, so that what is
    /// typed and what is read are the same thing.
    /// </summary>
    private static string Hex(int value) => value.ToString("X4", CultureInfo.InvariantCulture);

    private static int? Address(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var digits = text.StartsWith('$') ? text[1..]
            : text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..]
            : text;

        return int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    // The debugger's own lines are marked so they cannot be mistaken
    // for a listing line or for the game's own text.
    private static string Say(string text) => "; " + text;

    private static string Wanted(string command, string what) => Say($"{command} wants {what}");

    private static string Mark(bool here) => here ? "=> " : "   ";

    private static string Columns(IEnumerable<string> values)
    {
        var said = new StringBuilder();
        var column = 0;

        foreach (var value in values)
        {
            said.Append(column == 0 ? ";  " : "  ").Append(value);

            if (++column == 6)
            {
                said.AppendLine();
                column = 0;
            }
        }

        return said.ToString().TrimEnd();
    }

    private string In(int address) =>
        _listing.RoutineAt(address) is { } routine ? $", in routine {Hex(routine.Address)}" : string.Empty;
}

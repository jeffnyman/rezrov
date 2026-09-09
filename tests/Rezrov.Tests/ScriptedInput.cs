using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

/// <summary>
/// A keyboard for tests: answers each read with the next scripted line
/// or key, records what the interpreter asked for, and can play the part
/// of a clock by firing a request's timer a set number of times before
/// answering.
/// </summary>
internal sealed class ScriptedInput : IInput
{
    public ScriptedInput(params string[] lines)
    {
        foreach (var line in lines)
        {
            Lines.Enqueue(line);
        }
    }

    /// <summary>The commands still to be handed out, in order.</summary>
    public Queue<string> Lines { get; } = new();

    /// <summary>The keys still to be handed out, in order.</summary>
    public Queue<ushort> Keys { get; } = new();

    /// <summary>Every line request the interpreter made.</summary>
    public List<LineInputRequest> Requests { get; } = [];

    /// <summary>The timer passed with every key request.</summary>
    public List<InputTimer?> KeyTimers { get; } = [];

    public bool SupportsTimedInput { get; set; }

    /// <summary>
    /// How many times to fire a request's timer before answering. If an
    /// interrupt says to stop, the read ends with terminator 0 and
    /// <see cref="PartialText"/> as what was typed so far.
    /// </summary>
    public int InterruptsBeforeAnswering { get; set; }

    public string PartialText { get; set; } = "";

    /// <summary>The key that ends every scripted line.</summary>
    public ushort Terminator { get; set; } = Zscii.Newline;

    /// <summary>
    /// What <see cref="OpenCommandFile"/> hands over, once.
    /// </summary>
    public TextReader? CommandFile { get; set; }

    public LineInput ReadLine(LineInputRequest request)
    {
        Requests.Add(request);

        if (request.Timer is { } timer)
        {
            for (var i = 0; i < InterruptsBeforeAnswering; i++)
            {
                if (timer.Interrupt())
                {
                    return new LineInput(Codes(request.Initial, PartialText), 0);
                }
            }
        }

        if (Lines.Count == 0)
        {
            throw new EndOfStreamException("The script has run out of lines.");
        }

        return new LineInput(Codes(request.Initial, Lines.Dequeue()), Terminator);
    }

    public ushort ReadKey(InputTimer? timer)
    {
        KeyTimers.Add(timer);

        if (timer is not null)
        {
            for (var i = 0; i < InterruptsBeforeAnswering; i++)
            {
                if (timer.Interrupt())
                {
                    return 0;
                }
            }
        }

        if (Keys.Count == 0)
        {
            throw new EndOfStreamException("The script has run out of keys.");
        }

        return Keys.Dequeue();
    }

    public TextReader? OpenCommandFile()
    {
        var file = CommandFile;
        CommandFile = null;
        return file;
    }

    private static List<ushort> Codes(IReadOnlyList<ushort> initial, string typed)
    {
        var codes = new List<ushort>(initial);
        codes.AddRange(typed.Select(c => (ushort)c));
        return codes;
    }
}

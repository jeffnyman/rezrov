using System.Collections.Concurrent;
using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tui;

/// <summary>
/// The keyboard as the interpreter sees it: keys arrive from the UI
/// thread and are taken, one command or one key at a time, on the
/// interpreter's thread.
/// </summary>
/// <remarks>
/// [zm op:read] The line is edited here, at the cursor the game left:
/// printable characters are echoed, delete takes one back, return ends
/// the command, and [zm 10.5.2.1] a function key the story names as a
/// terminator ends it too. [zm 10.5.3] Input is timed with a plain
/// wait on the key queue, and the interrupt routine runs on this same
/// thread, which is the interpreter's, so it is as safe as any other
/// instruction.
/// </remarks>
public sealed class TerminalInput : IInput
{
    private readonly BlockingCollection<ushort> _keys = [];
    private readonly TerminalScreen _screen;

    public TerminalInput(TerminalScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        _screen = screen;
    }

    /// <summary>
    /// [zm 10.5.3] A queue with a timeout is a clock enough.
    /// </summary>
    public bool SupportsTimedInput => true;

    /// <summary>Hands a key from the terminal to the interpreter.</summary>
    public void Enqueue(ushort zscii) => _keys.Add(zscii);

    /// <summary>
    /// Waits for any key at all, for [MORE] and for the end.
    /// </summary>
    public ushort WaitForAnyKey() => _keys.Take();

    public LineInput ReadLine(LineInputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = new List<ushort>(request.Initial);

        while (true)
        {
            if (!TryTake(request.Timer, out var key))
            {
                // [zm op:read] The interrupt said stop: what was typed so
                // far, under terminator 0.
                return new LineInput(text, 0);
            }

            if (key == Zscii.Newline)
            {
                _screen.EchoNewLine();
                return new LineInput(text, Zscii.Newline);
            }

            if (request.Terminators.IsTerminator(key))
            {
                return new LineInput(text, key);
            }

            if (key == Zscii.Delete)
            {
                if (text.Count > request.Initial.Count)
                {
                    text.RemoveAt(text.Count - 1);
                    _screen.EchoBackspace();
                }

                continue;
            }

            if (Zscii.IsDefinedForInputAndOutput(key) && text.Count < request.MaxLength)
            {
                text.Add(key);
                if (Zscii.ToUnicode(key, ZMachine.ZMachineVersion.V5, UnicodeTranslationTable.Default) is { } shown)
                {
                    _screen.Echo(shown);
                }
            }
        }
    }

    public ushort ReadKey(InputTimer? timer) => TryTake(timer, out var key) ? key : (ushort)0;

    // Waits for a key, running the timer's interrupt at each interval
    // until it asks for the wait to end.
    private bool TryTake(InputTimer? timer, out ushort key)
    {
        if (timer is null)
        {
            key = _keys.Take();
            return true;
        }

        while (!_keys.TryTake(out key, timer.Interval))
        {
            if (timer.Interrupt())
            {
                key = 0;
                return false;
            }
        }

        return true;
    }
}

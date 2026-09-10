using System.Collections.Concurrent;
using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tui;

/// <summary>
/// The keyboard and mouse of the terminal, as the interpreter sees
/// them: a queue of keys fed from the UI thread and drained on the
/// interpreter's, with line editing done here and echoed to the
/// screen.
/// </summary>
/// <remarks>
/// [zm 10.5.3] Timed input is offered: a wait for a key gives up at
/// each interval to run the timer's interrupt, as the read opcodes
/// describe. [zm 10.3] Mouse clicks arrive on the same queue as the
/// click characters the standard defines, with their position kept
/// for the interpreter to pass on.
/// </remarks>
public sealed class TerminalInput : IInput
{
    private readonly BlockingCollection<(ushort Zscii, MouseClick? Click)> _keys = [];
    private readonly TerminalScreen _screen;
    private readonly int _unitsPerColumn;
    private readonly int _unitsPerRow;

    /// <param name="screen">The screen typing is echoed to.</param>
    /// <param name="unitsPerColumn">
    /// [zm 10.3.2] How many screen units a cell is wide, since click
    /// positions are reported in units: a character before Version 6,
    /// the frontend's font width in Version 6.
    /// </param>
    /// <param name="unitsPerRow">How many units a cell is high.</param>
    public TerminalInput(TerminalScreen screen, int unitsPerColumn = 1, int unitsPerRow = 1)
    {
        ArgumentNullException.ThrowIfNull(screen);
        _screen = screen;
        _unitsPerColumn = Math.Max(unitsPerColumn, 1);
        _unitsPerRow = Math.Max(unitsPerRow, 1);
    }

    public bool SupportsTimedInput => true;

    /// <summary>[zm 10.3] The terminal reports clicks.</summary>
    public bool SupportsMouse => true;

    /// <summary>The click behind the last click character read.</summary>
    public MouseClick? LastClick { get; private set; }

    /// <summary>Queues a key, from the UI thread.</summary>
    public void Enqueue(ushort zscii) => _keys.Add((zscii, null));

    /// <summary>
    /// Queues a click at a cell, from the UI thread, as [zm 3.8.2]
    /// the single or double click character with its position.
    /// </summary>
    public void EnqueueClick(int column, int row, bool doubleClick, int buttons)
    {
        var click = new MouseClick((column * _unitsPerColumn) + 1, (row * _unitsPerRow) + 1, buttons);
        _keys.Add((doubleClick ? Zscii.DoubleClick : Zscii.SingleClick, click));
    }

    /// <summary>Waits for any key at all, for [MORE] and the ending.</summary>
    public ushort WaitForAnyKey() => Take().Zscii;

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

    private (ushort Zscii, MouseClick? Click) Take()
    {
        var item = _keys.Take();
        LastClick = item.Click;
        return item;
    }

    // Waits for a key, running the timer's interrupt at each interval
    // until it asks for the wait to end.
    private bool TryTake(InputTimer? timer, out ushort key)
    {
        if (timer is null)
        {
            key = Take().Zscii;
            return true;
        }

        (ushort Zscii, MouseClick? Click) item;
        while (!_keys.TryTake(out item, timer.Interval))
        {
            if (timer.Interrupt())
            {
                key = 0;
                return false;
            }
        }

        LastClick = item.Click;
        key = item.Zscii;
        return true;
    }
}

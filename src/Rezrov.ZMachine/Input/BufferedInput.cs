using System.Collections.Concurrent;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Text;

namespace Rezrov.ZMachine.Input;

/// <summary>
/// A frontend's keyboard and mouse, as the interpreter sees them: a
/// queue of keys fed from whatever thread the frontend uses and drained
/// on the interpreter's, with line editing done here and echoed to the
/// screen.
/// </summary>
/// <remarks>
/// [zm 10.5.3] Timed input is offered: a wait for a key gives up at
/// each interval to run the timer's interrupt, as the read opcodes
/// describe. [zm 10.3] Mouse clicks arrive on the same queue as the
/// click characters the standard defines, with their position kept
/// for the interpreter to pass on.
///
/// A whole command can be queued as well, for a frontend that offers
/// the commands a player types most as something to choose instead. It
/// takes the place of whatever the player had begun to type, and a wait
/// for a single key passes over it, since a key is not what it is.
/// </remarks>
public sealed class BufferedInput : IInput
{
    private readonly BlockingCollection<Pressed> _keys = [];
    private readonly BufferedScreen _screen;
    private readonly int _unitsPerColumn;
    private readonly int _unitsPerRow;

    // Set on the interpreter's thread and read on the frontend's.
    private volatile bool _readingLine;

    /// <param name="screen">The screen typing is echoed to.</param>
    /// <param name="unitsPerColumn">
    /// [zm 10.3.2] How many screen units a cell is wide, since click
    /// positions are reported in units: a character before Version 6,
    /// the frontend's font width in Version 6.
    /// </param>
    /// <param name="unitsPerRow">How many units a cell is high.</param>
    public BufferedInput(BufferedScreen screen, int unitsPerColumn = 1, int unitsPerRow = 1)
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

    /// <summary>
    /// Whether the game is waiting for a whole command just now, rather
    /// than for a key or for nothing at all.
    /// </summary>
    public bool IsReadingLine => _readingLine;

    /// <summary>
    /// What to do as the game starts waiting for the player, for a line
    /// or a key, which is the moment a frontend that tells a screen reader
    /// what was printed tells it. Called on the machine's thread.
    /// </summary>
    public Action? Waiting { get; set; }

    /// <summary>Queues a key, from the UI thread.</summary>
    public void Enqueue(ushort zscii) => _keys.Add(new Pressed(zscii, null, null));

    /// <summary>
    /// Queues a whole command, from the UI thread, to be given to the game
    /// as if the player had typed it and pressed enter.
    /// </summary>
    public void EnqueueCommand(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _keys.Add(new Pressed(0, null, command));
    }

    /// <summary>
    /// Queues a click at a cell, from the UI thread, as [zm 3.8.2]
    /// the single or double click character with its position.
    /// </summary>
    public void EnqueueClick(int column, int row, bool doubleClick, int buttons)
    {
        var click = new MouseClick((column * _unitsPerColumn) + 1, (row * _unitsPerRow) + 1, buttons);
        _keys.Add(new Pressed(doubleClick ? Zscii.DoubleClick : Zscii.SingleClick, click, null));
    }

    /// <summary>Waits for any key, for [MORE] and the ending.</summary>
    public ushort WaitForAnyKey()
    {
        Waiting?.Invoke();
        TryTake(null, false, out var item);
        return item.Zscii;
    }

    public LineInput ReadLine(LineInputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Waiting?.Invoke();
        _readingLine = true;
        try
        {
            return Line(request);
        }
        finally
        {
            _readingLine = false;
        }
    }

    private LineInput Line(LineInputRequest request)
    {
        var text = new List<ushort>(request.Initial);

        while (true)
        {
            if (!TryTake(request.Timer, true, out var item))
            {
                // [zm op:read] The interrupt said stop: what was typed so
                // far, under terminator 0.
                return new LineInput(text, 0);
            }

            if (item.Command is { } command)
            {
                return Commanded(request, text, command);
            }

            var key = item.Zscii;

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

    public ushort ReadKey(InputTimer? timer)
    {
        Waiting?.Invoke();
        return TryTake(timer, false, out var item) ? item.Zscii : (ushort)0;
    }

    /// <summary>
    /// A queued command in place of the line: whatever the player had
    /// begun is rubbed out and the command is typed and entered.
    /// </summary>
    /// <remarks>
    /// Only what the player typed is rubbed out. Text the game put on the
    /// line before asking is the game's, as the delete key already treats
    /// it, and at an ordinary prompt there is none.
    /// </remarks>
    private LineInput Commanded(LineInputRequest request, List<ushort> text, string command)
    {
        while (text.Count > request.Initial.Count)
        {
            text.RemoveAt(text.Count - 1);
            _screen.EchoBackspace();
        }

        foreach (var character in command)
        {
            if (Zscii.FromUnicode(character, UnicodeTranslationTable.Default) is { } key
                && Zscii.IsDefinedForInputAndOutput(key)
                && text.Count < request.MaxLength)
            {
                text.Add(key);
                _screen.Echo(character);
            }
        }

        _screen.EchoNewLine();
        return new LineInput(text, Zscii.Newline);
    }

    // Waits for the next thing on the queue, running the timer's
    // interrupt at each interval until it asks for the wait to end. A
    // command is passed over unless a whole line is what is wanted.
    private bool TryTake(InputTimer? timer, bool commands, out Pressed item)
    {
        while (true)
        {
            if (timer is null)
            {
                item = _keys.Take();
            }
            else
            {
                while (!_keys.TryTake(out item, timer.Interval))
                {
                    if (timer.Interrupt())
                    {
                        item = default;
                        return false;
                    }
                }
            }

            if (item.Command is not null && !commands)
            {
                continue;
            }

            LastClick = item.Click;
            return true;
        }
    }

    private readonly record struct Pressed(ushort Zscii, MouseClick? Click, string? Command);
}

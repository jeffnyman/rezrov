using System.Text;

namespace Rezrov.Gui;

/// <summary>
/// The prose a game prints between one wait for the player and the next,
/// gathered so that a screen reader can be told it all at once.
/// </summary>
/// <remarks>
/// A screen reader cannot see text the window draws for itself, so the
/// window tells it, and a turn at a time is the measure that works. A
/// character at a time would be read as letters, a line at a time would
/// cut a room description into pieces, and waiting for the game to ask
/// for something is the one moment every game has when it is done
/// talking for now.
///
/// What is gathered is the prose a player reads: the Z-machine's lower
/// window, a Glk game's text buffers, and a Dialog story's main text.
/// Status lines, text grids, and the status areas are left out, since
/// they are drawn again every turn and would be read every turn. The
/// player's own typing is left out too; they know what they typed.
///
/// The machines write on threads of their own, so everything here is
/// done under a lock, and whatever is told the reader is told on the
/// machine's thread, for the caller to carry to the window's.
/// </remarks>
public sealed class Narration
{
    private readonly StringBuilder _turn = new();
    private readonly Lock _sync = new();

    /// <summary>
    /// Where a turn goes once the game waits for the player, or null for
    /// nowhere, which leaves the gathering to be thrown away.
    /// </summary>
    public Action<string>? Spoken { get; set; }

    /// <summary>Text the game printed.</summary>
    public void Add(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        lock (_sync)
        {
            _turn.Append(text);
        }
    }

    /// <summary>One character the game printed.</summary>
    public void Add(char character)
    {
        lock (_sync)
        {
            _turn.Append(character);
        }
    }

    /// <summary>A line the game ended.</summary>
    public void Break() => Add('\n');

    /// <summary>
    /// The game is waiting for the player, so what it printed since it
    /// last waited is told, if it printed anything.
    /// </summary>
    public void Ready()
    {
        string turn;

        lock (_sync)
        {
            turn = Tidy(_turn.ToString());
            _turn.Clear();
        }

        if (turn.Length > 0)
        {
            Spoken?.Invoke(turn);
        }
    }

    /// <summary>
    /// A turn as it is read: each line with its spaces run together, and
    /// the empty lines that only space paragraphs apart on a screen left
    /// out.
    /// </summary>
    public static string Tidy(string turn)
    {
        ArgumentNullException.ThrowIfNull(turn);

        var lines = turn
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Where(line => line.Length > 0);

        return string.Join('\n', lines);
    }
}

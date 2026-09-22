using Rezrov.ZMachine.Objects;

namespace Rezrov.ZMachine.Execution;

/// <summary>
/// Works out which room the player is standing in, for the games that do
/// not say.
/// </summary>
/// <remarks>
/// [zm 8.2] Versions 1 to 3 keep the room in the first global, because
/// the interpreter draws the status line and has to know what to write
/// on it. From Version 4 the game draws its own, and the room is
/// wherever the compiler felt like putting it. What is still on the
/// screen, though, is the room's name, because that is what the bar is
/// for.
///
/// So the name is read off the bar and turned back into an object, which
/// matters because a name alone is not an identity. Zork has a dozen
/// rooms called "Forest" and a maze of rooms called "Maze", and Trinity
/// names two rooms the same thing on two turns out of three. Mapping by
/// name folds every one of them into a single room.
///
/// The way back to an object is the player. Whatever object the game
/// moves from room to room as the bar changes is the player, and once it
/// is known the room is simply what that object is in, with no names
/// involved at all. Nothing in a story file says which object that is,
/// so it is learned: every object whose parent is named on the bar
/// scores a point, and the one that keeps scoring is the player. Across
/// the corpus the winner is recognizably the player object, and in
/// Zork's case is called "cretin".
///
/// Learning can fail, and when it does this says so rather than
/// guessing. A game whose rooms have no objects behind them at all has
/// nothing here to find.
/// </remarks>
public sealed class RoomSense
{
    /// <summary>
    /// How many turns must have named a room before the scores are worth
    /// reading at all.
    /// </summary>
    private const int Settling = 5;

    /// <summary>
    /// The share of those turns the player has to have been right for,
    /// which is well below what the real player object manages and well
    /// above what anything else does.
    /// </summary>
    private const double Confidence = 0.75;

    /// <summary>How far up a parent chain to look for the room.</summary>
    private const int Ancestors = 8;

    private readonly ObjectTable _objects;
    private readonly Dictionary<int, string> _names = [];
    private readonly Dictionary<string, (int Count, int First)> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<int, int> _score = [];
    private int _named;
    private int _player;

    /// <param name="objects">The story's object tree.</param>
    public RoomSense(ObjectTable objects)
    {
        ArgumentNullException.ThrowIfNull(objects);

        _objects = objects;

        // A short name never changes once the story is compiled, so the
        // whole tree is read once here rather than every turn.
        for (var obj = 1; obj <= objects.Count; obj++)
        {
            var name = objects.ShortName(obj).Trim();
            if (name.Length == 0)
            {
                continue;
            }

            _names[obj] = name;
            _byName[name] = _byName.TryGetValue(name, out var known)
                ? (known.Count + 1, known.First)
                : (1, obj);
        }
    }

    /// <summary>
    /// The object the bar's room name keeps pointing at, once enough
    /// turns agree, or 0 while nothing does.
    /// </summary>
    public int Player => _player;

    /// <summary>
    /// Reads the status line and answers where the player is, or null
    /// when it cannot be told.
    /// </summary>
    /// <param name="bar">The top row of the upper window.</param>
    public (int Room, string Name)? Standing(string? bar)
    {
        if (string.IsNullOrWhiteSpace(bar))
        {
            return null;
        }

        var pieces = Pieces(bar);
        if (pieces.FirstOrDefault(_byName.ContainsKey) is not { } named)
        {
            // Nothing on the bar is the name of anything in the story, so
            // it is a score, a clock, a banner, or a game with no rooms.
            return null;
        }

        Learn(named);

        if (Settled() && Where(_player, pieces) is { } room)
        {
            return (room, _names[room]);
        }

        // No player yet. A name exactly one object answers to is still an
        // answer; one that several answer to is the very ambiguity this
        // class exists to avoid, so it is refused.
        var (count, first) = _byName[named];
        return count == 1 ? (first, _names[first]) : null;
    }

    /// <summary>
    /// What a status line might be naming: the whole of it, each run
    /// between two or more spaces, and the value half of anything shaped
    /// like "Label: value".
    /// </summary>
    /// <remarks>
    /// A bar can label several things at once, and which label means the
    /// room is not something to assume: one game's reads "Year: 2001
    /// Place: Front Lawn". Every piece is offered and the object tree
    /// picks, rather than a rule here about where a room name sits.
    /// </remarks>
    private static List<string> Pieces(string bar)
    {
        var pieces = new List<string>();
        var line = bar.Trim();
        if (line.Length == 0)
        {
            return pieces;
        }

        pieces.Add(line);

        foreach (var run in line.Split(
            "  ",
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            pieces.Add(run);

            var colon = run.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && colon < run.Length - 1)
            {
                pieces.Add(run[(colon + 1)..].Trim());
            }
        }

        return pieces;
    }

    /// <summary>
    /// Gives a point to every object standing in the room the bar names.
    /// </summary>
    private void Learn(string room)
    {
        _named++;

        for (var obj = 1; obj <= _objects.Count; obj++)
        {
            var parent = _objects.Parent(obj);
            if (_names.TryGetValue(parent, out var name)
                && string.Equals(name, room, StringComparison.OrdinalIgnoreCase))
            {
                _score[obj] = _score.GetValueOrDefault(obj) + 1;
            }
        }
    }

    /// <summary>
    /// Whether one object has been in the named room often enough, and by
    /// a wide enough margin over everything else, to be the player.
    /// </summary>
    /// <remarks>
    /// The margin is what keeps a one-room stretch of a game from
    /// settling on the lamp. Where two objects have been in the same
    /// place every turn so far, neither is known to be the player, and
    /// the name is used instead until they part company.
    ///
    /// Recomputed rather than locked, so a game that moves the player
    /// object partway through, which the long ones do, is followed rather
    /// than remembered wrongly for the rest of the session.
    /// </remarks>
    private bool Settled()
    {
        if (_named < Settling)
        {
            return false;
        }

        var leader = 0;
        var best = 0;
        var second = 0;

        foreach (var (obj, score) in _score)
        {
            if (score > best || (score == best && obj == _player))
            {
                second = Math.Max(second, best);
                (leader, best) = (obj, score);
            }
            else if (score > second)
            {
                second = score;
            }
        }

        if (best <= second || best < _named * Confidence)
        {
            return false;
        }

        _player = leader;
        return true;
    }

    /// <summary>
    /// The room an object is in: what it is directly inside, unless it is
    /// inside something that is itself somewhere the bar names.
    /// </summary>
    /// <remarks>
    /// A player in a boat is in the boat, and the bar still says which
    /// stretch of river the boat is on. Taking the parent alone would put
    /// the boat on the map as a room and keep it there the whole way
    /// down. Taking the top of the chain instead is worse and was
    /// measured to be: Infocom parents its rooms under a container of
    /// their own, so the top of every chain is that one object and the
    /// answer is never a room at all.
    ///
    /// So the chain is walked only as far as something the bar agrees
    /// with, and the parent is the answer when nothing does.
    /// </remarks>
    private int? Where(int player, List<string> pieces)
    {
        var parent = _objects.Parent(player);
        if (!_names.ContainsKey(parent))
        {
            return null;
        }

        var at = parent;
        for (var step = 0; step < Ancestors; step++)
        {
            if (_names.TryGetValue(at, out var name)
                && pieces.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return at;
            }

            var above = _objects.Parent(at);
            if (!_names.ContainsKey(above))
            {
                break;
            }

            at = above;
        }

        return parent;
    }
}

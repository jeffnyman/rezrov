namespace Rezrov.Mapping;

/// <summary>
/// The map as it stands: every room found so far, every passage walked
/// between them, and where the player is now.
/// </summary>
/// <remarks>
/// The graph is built by telling it, turn after turn, which room the
/// player is in and which direction they typed to get there. It is told
/// nothing else, and in particular it is never told that the player
/// moved: it works that out by comparing the room it was given against
/// the room it had. That is the whole interface, and it is why the same
/// graph can be built from a Z-Machine game, a Glulx game or an
/// Aa-machine story without knowing which one it is looking at.
/// </remarks>
public sealed class RoomGraph
{
    private readonly List<Room> _rooms = [];
    private readonly Dictionary<RoomKey, Room> _byKey = [];

    /// <summary>Every room found, in the order they were found.</summary>
    public IReadOnlyList<Room> Rooms => _rooms;

    /// <summary>
    /// The room the player is in, or null before the first observation.
    /// </summary>
    public Room? Current { get; private set; }

    /// <summary>
    /// Takes in the player's room at the end of a turn, and the direction
    /// they typed to reach it.
    /// </summary>
    /// <remarks>
    /// The direction is what the player typed, not what the game did with
    /// it, so it is given even when the move failed. A direction that
    /// left the player where they were is recorded as tried; one that did
    /// not is a passage. A move with no direction named at all, which is
    /// how these games handle a trap door or a spell, moves the player
    /// without minting anything: no passage was walked, so none is drawn.
    /// </remarks>
    /// <param name="key">What tells this room from the others.</param>
    /// <param name="name">What the game called it.</param>
    /// <param name="via">The direction typed, if one was.</param>
    /// <returns>The room, new or already known.</returns>
    public Room Observe(RoomKey key, string name, Direction? via)
    {
        ArgumentNullException.ThrowIfNull(name);

        var room = Find(key, name);

        if (Current is null)
        {
            room.Position ??= (0, 0);
            Current = room;
            return room;
        }

        if (ReferenceEquals(room, Current))
        {
            if (via is { } tried && !room.Exits.ContainsKey(tried))
            {
                room.MarkTried(tried);
            }

            return room;
        }

        if (via is { } walked)
        {
            Current.Add(walked, room.Id);
            Placement.Place(this, Current, room, walked);
        }
        else
        {
            Placement.PlaceNear(this, Current, room);
        }

        Current = room;
        return room;
    }

    /// <summary>
    /// Puts back a room that was read from a map written earlier,
    /// exactly where it was, rather than placing it afresh.
    /// </summary>
    /// <remarks>
    /// The whole point of keeping a map is that it is the same map
    /// when it comes back, so nothing here is worked out again: the
    /// cell comes from the file. Rooms are read in the order they were
    /// written, which is the order they were found, so the identifiers
    /// line up with this list without being written down twice.
    /// </remarks>
    internal Room Reopen(RoomKey key, string name, (int X, int Y)? position)
    {
        var room = new Room(_rooms.Count, key, name) { Position = position };

        _rooms.Add(room);
        _byKey[key] = room;

        return room;
    }

    /// <summary>
    /// Puts the player back in a room, for a map being read back.
    /// </summary>
    internal void StandIn(Room room) => Current = room;

    private Room Find(RoomKey key, string name)
    {
        if (_byKey.TryGetValue(key, out var known))
        {
            return known;
        }

        var room = new Room(_rooms.Count, key, name);
        _rooms.Add(room);
        _byKey[key] = room;
        return room;
    }
}

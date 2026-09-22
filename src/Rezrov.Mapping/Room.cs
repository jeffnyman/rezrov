namespace Rezrov.Mapping;

/// <summary>
/// One room on the map: what it is called, where it sits, and what has
/// been learned about the ways out of it.
/// </summary>
public sealed class Room
{
    private readonly Dictionary<Direction, int> _exits = [];
    private readonly HashSet<Direction> _tried = [];

    internal Room(int id, RoomKey key, string name)
    {
        Id = id;
        Key = key;
        Name = name;
    }

    /// <summary>Its place in the graph's own list of rooms.</summary>
    public int Id { get; }

    /// <summary>What tells this room from every other.</summary>
    public RoomKey Key { get; }

    /// <summary>
    /// The name the room was first seen under.
    /// </summary>
    /// <remarks>
    /// The first rather than the latest, because a name can change from
    /// turn to turn without the room changing at all: a status line that
    /// carries the time of day, or a room the game renames once you have
    /// lit it. The first name is at least the same name every time it is
    /// asked for, which is what a drawn map needs.
    /// </remarks>
    public string Name { get; }

    /// <summary>
    /// The cell this room was laid out in, or null before it has been
    /// placed.
    /// </summary>
    public (int X, int Y)? Position { get; internal set; }

    /// <summary>
    /// The passages known to lead out of this room, by the direction
    /// walked, to the room they were walked to.
    /// </summary>
    /// <remarks>
    /// This room's own exits, and nothing more. Walking north from here
    /// into the kitchen says nothing whatever about whether south from
    /// the kitchen comes back, so nothing is written there until someone
    /// walks it. Half of these games turn on a passage that runs one way.
    /// </remarks>
    public IReadOnlyDictionary<Direction, int> Exits => _exits;

    /// <summary>
    /// Directions walked from this room that left the player in it.
    /// </summary>
    /// <remarks>
    /// A wall, a door that would not open, and a passage that loops
    /// straight back into the room it left are indistinguishable from
    /// here, so this records only that the direction was tried and came
    /// to nothing. It is not a claim that there is no way out that way.
    /// </remarks>
    public IReadOnlyCollection<Direction> Tried => _tried;

    internal void Add(Direction direction, int destination)
    {
        _exits[direction] = destination;

        // Whatever was tried here has now been walked, so the record of
        // the attempt is replaced by the passage it turned out to be.
        _tried.Remove(direction);
    }

    internal void MarkTried(Direction direction) => _tried.Add(direction);
}

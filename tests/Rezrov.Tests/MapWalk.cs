using Rezrov.Mapping;

namespace Rezrov.Tests;

/// <summary>
/// A game being played, for the tests that need one: rooms named as the
/// player would see them, and commands typed as the player would type
/// them.
/// </summary>
/// <remarks>
/// Each distinct name gets an object number of its own, the way a
/// Z-Machine game before Version 4 gives every room one, so two rooms
/// that happen to share a name can still be told apart by asking for
/// them under different numbers. The direction is read out of the typed
/// command by the same code a frontend would use, so a test that walks
/// "go north" is testing that path too.
/// </remarks>
internal sealed class MapWalk
{
    private readonly Dictionary<string, int> _numbers = [];

    public RoomGraph Graph { get; } = new();

    /// <summary>
    /// Ends a turn in the named room, having typed
    /// <paramref name="command"/> to get there.
    /// </summary>
    public Room To(string room, string? command = null)
    {
        Direction? via = command is not null && Directions.TryParse(command, out var walked)
            ? walked
            : null;

        return Graph.Observe(RoomKey.ForObject(Number(room)), room, via);
    }

    /// <summary>
    /// Ends a turn in a room given explicitly by number, for the tests
    /// about two rooms that share a name.
    /// </summary>
    public Room ToObject(int number, string room, string? command = null)
    {
        Direction? via = command is not null && Directions.TryParse(command, out var walked)
            ? walked
            : null;

        return Graph.Observe(RoomKey.ForObject(number), room, via);
    }

    /// <summary>The cell a room ended up in.</summary>
    public (int X, int Y) Where(string room) =>
        Graph.Rooms.Single(candidate => candidate.Key == RoomKey.ForObject(Number(room)))
            .Position!.Value;

    private int Number(string room)
    {
        if (!_numbers.TryGetValue(room, out var number))
        {
            number = _numbers.Count + 1;
            _numbers[room] = number;
        }

        return number;
    }
}

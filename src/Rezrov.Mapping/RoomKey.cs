namespace Rezrov.Mapping;

/// <summary>
/// What tells one room from another for as long as a game is played.
/// </summary>
/// <remarks>
/// Whoever is watching the game chooses which of the two forms it can
/// answer with, and the map does not care which. A Z-Machine game before
/// Version 4 keeps the player's room in a global variable, so every room
/// has an object number behind it and two rooms that happen to share a
/// name stay apart. A game that names its rooms only on screen has
/// nothing but the name, and two rooms called "Maze" are one room as far
/// as anything here can tell. That is a real loss and it belongs to the
/// game, not to the map: the alternative is to invent a difference.
/// </remarks>
public readonly record struct RoomKey
{
    /// <summary>The object that is the room, or 0 for a name.</summary>
    public int Number { get; private init; }

    /// <summary>The room's name, where there is no object to use.</summary>
    public string? Name { get; private init; }

    /// <summary>A room known by the game object that is the room.</summary>
    public static RoomKey ForObject(int number) => new() { Number = number };

    /// <summary>A room known only by the name printed for it.</summary>
    public static RoomKey ForName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new() { Name = name };
    }
}

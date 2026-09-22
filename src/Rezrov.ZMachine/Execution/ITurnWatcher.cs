namespace Rezrov.ZMachine.Execution;

/// <summary>
/// Something following a game being played, turn by turn, for whatever
/// it wants to make of it.
/// </summary>
/// <remarks>
/// The interpreter tells a watcher two things and nothing else: which
/// room the player is standing in when it stops for a command, and what
/// they typed. Both come from the machine rather than from the screen,
/// and neither is interpreted here.
///
/// A watcher is told where the player is whenever the room can be
/// worked out. [zm 8.2] Versions 1 to 3 keep it in the first global
/// variable because the interpreter draws the status line and has to
/// know what to write on it. From Version 4 the game draws its own bar,
/// and the room is recovered from it by <see cref="RoomSense"/>, which
/// can fail; a watcher then hears only what was typed.
/// </remarks>
public interface ITurnWatcher
{
    /// <summary>
    /// The room the player is standing in, as the game stops for a
    /// command.
    /// </summary>
    /// <param name="room">The object that is the room.</param>
    /// <param name="name">Its short name.</param>
    void Standing(int room, string name);

    /// <summary>What the player typed at that prompt.</summary>
    void Typed(string command);
}

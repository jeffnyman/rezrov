using Rezrov.Mapping;
using Rezrov.ZMachine.Execution;

namespace Rezrov.Cli;

/// <summary>
/// Turns a game being played into a map of the rooms it was played
/// through.
/// </summary>
/// <remarks>
/// A turn arrives in two halves: where the player is standing, and then
/// what they typed. The direction they typed belongs to the turn after
/// it, not the one it was typed on, since it is only when the next
/// prompt comes round that there is anywhere to say it led. So the
/// direction is held over, spent on the next room seen, and forgotten
/// whether it was used or not.
///
/// A command the map cannot read a direction out of, which is most of
/// them, leaves the hold empty. If the player turns out to have moved
/// anyway, the room is recorded with no passage into it. That is the
/// right answer for a trap door or a spell, and it is also the answer
/// for a "g" that repeated a direction typed a turn ago: the map does
/// not know what was repeated, and a gap beats a guess.
/// </remarks>
public sealed class RoomWatcher : ITurnWatcher
{
    private Direction? _walked;

    /// <summary>The map as far as the game has been played.</summary>
    public RoomGraph Graph { get; } = new();

    public void Standing(int room, string name)
    {
        Graph.Observe(RoomKey.ForObject(room), name, _walked);
        _walked = null;
    }

    public void Typed(string command) =>
        _walked = Directions.TryParse(command, out var direction) ? direction : null;
}

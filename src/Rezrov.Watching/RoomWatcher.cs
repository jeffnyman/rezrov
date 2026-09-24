using Rezrov.Mapping;
using Rezrov.ZMachine.Execution;

namespace Rezrov.Watching;

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
    private readonly Lock _lock = new();

    private Direction? _walked;

    /// <param name="kept">
    /// A map of this game from an earlier session, which the player
    /// goes on adding to, or null to start a fresh one.
    /// </param>
    public RoomWatcher(RoomGraph? kept = null) => Graph = kept ?? new RoomGraph();

    /// <summary>The map as far as the game has been played.</summary>
    /// <remarks>
    /// Safe to reach for directly only where the game and whatever
    /// reads the map are the same thread, which in a terminal they are:
    /// the map is written out once the game has stopped. A program that
    /// draws the map while the game is still being played is two
    /// threads, and has to go through <see cref="Read"/> instead.
    /// </remarks>
    public RoomGraph Graph { get; private set; }

    /// <summary>
    /// Throws the map away and starts another, which the player asks
    /// for and nothing else ever does.
    /// </summary>
    /// <remarks>
    /// Restarting a game does not do this, and neither does restoring
    /// a save. Both of those change where the player is, not what they
    /// have found out, and a game that restarts itself would otherwise
    /// take the map with it without anyone having asked.
    /// </remarks>
    public void Forget()
    {
        lock (_lock)
        {
            Graph = new RoomGraph();
        }

        _walked = null;

        Changed?.Invoke();
    }

    /// <summary>
    /// Raised once the map has taken in another turn.
    /// </summary>
    /// <remarks>
    /// Raised on whichever thread is running the game, which is not
    /// the one a window draws on. A handler that repaints something
    /// has to get itself back onto its own thread first.
    /// </remarks>
    public event Action? Changed;

    /// <summary>
    /// Runs <paramref name="reading"/> over the map with the game held
    /// off it.
    /// </summary>
    /// <remarks>
    /// The graph is a plain object graph with no thread safety of its
    /// own, and a room arriving while the map is being walked would be
    /// a torn read at best. Whatever is done in here should be short
    /// and should copy out what it needs: the game cannot take another
    /// turn until it returns.
    /// </remarks>
    public void Read(Action<RoomGraph> reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        lock (_lock)
        {
            reading(Graph);
        }
    }

    public void Standing(int room, string name) =>
        Arrive(RoomKey.ForObject(room), name);

    /// <summary>
    /// The room the player is standing in, known only by its name.
    /// </summary>
    /// <remarks>
    /// What a Glulx story leaves to go on: there is no object behind the
    /// name, so two rooms a game calls the same thing are one room here.
    /// That loss belongs to the game, and inventing a difference would
    /// be worse than recording the one it admits to.
    /// </remarks>
    public void StandingIn(string name) => Arrive(RoomKey.ForName(name), name);

    public void Typed(string command) =>
        _walked = Directions.TryParse(command, out var direction) ? direction : null;

    private void Arrive(RoomKey key, string name)
    {
        lock (_lock)
        {
            Graph.Observe(key, name, _walked);
        }

        _walked = null;

        // Outside the lock, so that a handler which takes a while, or
        // which waits on another thread to draw, cannot stop the game
        // from taking its next turn.
        Changed?.Invoke();
    }
}

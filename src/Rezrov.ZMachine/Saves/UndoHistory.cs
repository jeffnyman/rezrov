namespace Rezrov.ZMachine.Saves;

/// <summary>
/// The cache of states that save_undo fills and restore_undo drains.
/// </summary>
/// <remarks>
/// [zm op:save_undo] The game is saved into a cache of memory held by
/// the interpreter, typically once per turn, and [zm op:restore_undo]
/// the most recent one is restored. The cache is a bounded stack: the
/// oldest state is dropped when it is full, so a long game does not
/// grow without limit, and a player can still undo a good many turns.
/// [zm 6.1.1.2] None of this is part of the state of play, so a saved
/// game never contains it.
/// </remarks>
public sealed class UndoHistory
{
    /// <summary>
    /// How many turns back a player can go. Each state is a copy of
    /// dynamic memory and the stack, so this is at most a few megabytes
    /// for the largest games and almost nothing for the old ones.
    /// </summary>
    public const int MaxStates = 100;

    private readonly List<SavedState> _states = [];

    /// <summary>How many states are held.</summary>
    public int Count => _states.Count;

    /// <summary>Remembers a state, forgetting the oldest if full.</summary>
    public void Push(SavedState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (_states.Count == MaxStates)
        {
            _states.RemoveAt(0);
        }

        _states.Add(state);
    }

    /// <summary>
    /// Takes back the most recent state, or returns false if there is
    /// none.
    /// </summary>
    public bool TryPop(out SavedState state)
    {
        if (_states.Count == 0)
        {
            state = null!;
            return false;
        }

        state = _states[^1];
        _states.RemoveAt(_states.Count - 1);
        return true;
    }

    /// <summary>Forgets every state.</summary>
    public void Clear() => _states.Clear();
}

namespace Rezrov.Glulx.Saves;

/// <summary>
/// The states that saveundo keeps and restoreundo goes back to.
/// </summary>
/// <remarks>
/// [glulx op:saveundo] The state is saved in a temporary location the
/// interpreter chooses for rapid access, since a game may do it once a
/// turn, and [glulx op:restoreundo] the most recent one is restored.
/// [glulx #game-state] A terp may support several levels of temporary
/// storage and a game may not assume how many, so this is a bounded
/// stack: the oldest state is dropped when it is full, and a player can
/// still take back a good many turns. Each state is a copy of RAM and
/// the stack, so the bound keeps a long game's memory in check.
/// </remarks>
public sealed class GlulxUndoHistory
{
    /// <summary>
    /// How many turns back a player can go, which for a large game is
    /// a few megabytes a state.
    /// </summary>
    public const int MaxStates = 20;

    private readonly List<GlulxSavedState> _states = [];

    /// <summary>How many states are held.</summary>
    public int Count => _states.Count;

    /// <summary>Remembers a state, forgetting the oldest if full.</summary>
    public void Push(GlulxSavedState state)
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
    public bool TryPop(out GlulxSavedState state)
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

    /// <summary>
    /// [glulx op:discardundo] Forgets the most recent state, or does
    /// nothing if there is none.
    /// </summary>
    public void Discard()
    {
        if (_states.Count > 0)
        {
            _states.RemoveAt(_states.Count - 1);
        }
    }
}

using Rezrov.ZMachine.Execution;

namespace Rezrov.Debugging;

/// <summary>
/// One routine of a call chain, as something showing one would put
/// it.
/// </summary>
/// <param name="Depth">
/// [zm 6.5] How many routines deep this one is, the outermost being 1,
/// which is the number catch returns and throw takes.
/// </param>
/// <param name="Address">
/// Where this frame is: the program counter for the routine actually
/// running, and for every older one the address the routine above it
/// will return to.
/// </param>
/// <param name="Routine">
/// Which routine the frame is in, or null where neither the machine
/// nor the listing can say.
/// </param>
/// <param name="Exact">
/// Whether the machine recorded the routine when it called it, as
/// against the routine having been worked out from a disassembly.
/// </param>
public readonly record struct CallChainEntry(int Depth, int Address, int? Routine, bool Exact);

/// <summary>
/// The call chain of a running game, with each frame's routine named.
/// </summary>
/// <remarks>
/// The machine records the routine it called at the moment it called
/// it, which is the truth and is free. Two frames cannot say: [zm 5.5]
/// the one a game outside Version 6 starts in, which is running no
/// routine at all, and [quetzal 4] any frame rebuilt from a saved
/// game, since a saved frame is a return address, its locals, and its
/// stack. Those are filled in from a disassembly where one is offered,
/// and the entry says which of the two answers it gave, because a
/// scan is a very good guess and the machine is not guessing.
/// </remarks>
public static class CallChain
{
    /// <summary>
    /// The call chain, the routine running first and the outermost
    /// last, which is the order such a thing is read in.
    /// </summary>
    public static IReadOnlyList<CallChainEntry> Of(GameState state, Disassembly? listing = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        var frames = state.Frames;
        var entries = new CallChainEntry[frames.Count];

        for (var i = frames.Count - 1; i >= 0; i--)
        {
            var at = i == frames.Count - 1
                ? state.ProgramCounter
                : frames[i + 1].ReturnAddress;

            var routine = frames[i].RoutineAddress;
            var exact = routine is not null;

            entries[frames.Count - 1 - i] = new CallChainEntry(
                i + 1,
                at,
                routine ?? listing?.RoutineAt(at)?.Address,
                exact);
        }

        return entries;
    }
}

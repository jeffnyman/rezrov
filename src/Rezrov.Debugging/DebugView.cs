namespace Rezrov.Debugging;

/// <summary>
/// Everything a debugger shows at once, as text.
/// </summary>
/// <remarks>
/// A window draws several things a terminal only shows when asked for
/// them, and all of them have to be read from a game that is standing
/// still. So they are gathered together, on the thread the game runs
/// on, into plain strings that the thread which draws can hold for as
/// long as it likes. It is the same arrangement the map uses, and for
/// the same reason.
///
/// They are the answers the typed commands give, word for word, so
/// that a window and a terminal never disagree about what the game is
/// doing.
/// </remarks>
/// <param name="Position">The instruction about to be carried out.</param>
/// <param name="Listing">The instructions around it.</param>
/// <param name="Chain">The routines the game is inside.</param>
/// <param name="Locals">The running routine's local variables.</param>
/// <param name="Globals">The globals the game has written to.</param>
/// <param name="Stack">What the running routine has pushed.</param>
/// <param name="Watching">The words being kept an eye on.</param>
/// <param name="Quit">Whether the game has ended.</param>
public sealed record DebugView(
    string Position,
    string Listing,
    string Chain,
    string Locals,
    string Globals,
    string Stack,
    string Watching,
    bool Quit);

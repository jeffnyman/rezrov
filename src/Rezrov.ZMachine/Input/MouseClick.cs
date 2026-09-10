namespace Rezrov.ZMachine.Input;

/// <summary>
/// [zm 10.3] A mouse click, as the keyboard reports it alongside the
/// click character it delivers.
/// </summary>
/// <param name="X">
/// [zm 10.3.2] The x coordinate of the click in screen units, counting
/// from 1 at the left.
/// </param>
/// <param name="Y">The y coordinate in screen units, from 1 at the top.</param>
/// <param name="Buttons">
/// [zm op:read_mouse] The buttons held, the primary button as bit 0,
/// the secondary as bit 1, and so on.
/// </param>
public sealed record MouseClick(int X, int Y, int Buttons);

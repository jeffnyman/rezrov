using Rezrov.ZMachine.Input;

namespace Rezrov.ZMachine.Screen;

/// <summary>
/// What a frontend says when a game ends, and the wait that keeps it
/// on the screen long enough to be read.
/// </summary>
/// <remarks>
/// A game is free to end without asking for anything first: a short
/// one, a test file, or any story whose last words are followed by
/// quit. A program that closed its window the moment that happened
/// would take the ending with it.
///
/// It matters more when a game stops rather than ends. The reason has
/// nowhere else to go: a window program on Windows has no console
/// attached, so anything written to standard error is written to
/// nobody. Said here, it is said where the game's own words were.
///
/// All three frontends that draw a grid want the same thing, so it
/// is written once, here beside the grid they share. A stream of
/// text does not need it: what a console printed stays printed.
/// </remarks>
public static class LastWord
{
    /// <param name="screen">The grid the game was played on.</param>
    /// <param name="input">Where the key to leave by comes from.</param>
    /// <param name="stopped">
    /// Why the game stopped, or null where it simply ended.
    /// </param>
    public static void Show(BufferedScreen screen, BufferedInput input, string? stopped)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(input);

        // Reversed, so that it reads as the program speaking rather
        // than as one more thing the story said.
        var notice = new TextAttributes(
            TextStyle.ReverseVideo,
            screen.DefaultForeground,
            screen.DefaultBackground,
            TextAttributes.NormalFont);

        screen.NewLine();

        if (stopped is not null)
        {
            screen.Print(stopped, notice);
            screen.NewLine();
        }

        screen.Print("[The game has ended. Press a key to leave.]", notice);

        input.WaitForAnyKey();
    }
}

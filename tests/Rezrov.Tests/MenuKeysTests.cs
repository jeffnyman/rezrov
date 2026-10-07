using Avalonia.Input;
using Rezrov.Gui;

namespace Rezrov.Tests;

/// <summary>
/// The keys the menus take for themselves, which are keys no game can
/// be given.
/// </summary>
public class MenuKeysTests
{
    [Fact]
    public void NoKeyTheMenusTakeIsOneAGameIsGiven()
    {
        // [zm 3.8.4] [glk #character_input] A game is given the special
        // keys whatever is held with them, so a menu key built on one of
        // those, the function keys above all, would reach the game as
        // well as the menu.
        foreach (var gesture in MenuKeys.All)
        {
            Assert.Null(GuiKeyMap.ToZscii(gesture.Key));
            Assert.Null(GuiKeyMap.ToGlk(gesture.Key));
            Assert.Null(GuiKeyMap.ToAa(gesture.Key));
        }
    }

    [Fact]
    public void EveryKeyIsControlWithALetter()
    {
        // A letter reaches a game only as the text it types, and Control
        // with a letter types none, while Alt with one opens the menus.
        foreach (var gesture in MenuKeys.All)
        {
            Assert.Equal(KeyModifiers.Control, gesture.KeyModifiers);
            Assert.InRange(gesture.Key, Key.A, Key.Z);
        }
    }

    [Fact]
    public void NoTwoThingsShareAKey()
    {
        var keys = MenuKeys.All.Select(g => g.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void RestartingHasNoKey()
    {
        // One key pressed by mistake would throw a game away.
        Assert.DoesNotContain("restart", MenuKeys.Commands.Keys);
    }
}

using Rezrov.Gui;
using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

/// <summary>
/// The prose a game prints between one wait for the player and the next,
/// as a screen reader is told it.
/// </summary>
public class NarrationTests
{
    [Fact]
    public void ATurnIsToldWhenTheGameWaitsForThePlayer()
    {
        var told = new List<string>();
        var narration = new Narration { Spoken = told.Add };

        narration.Add("West of House");
        narration.Break();
        narration.Add("There is a small mailbox here.");

        Assert.Empty(told);

        narration.Ready();

        Assert.Equal(["West of House\nThere is a small mailbox here."], told);
    }

    [Fact]
    public void AWaitWithNothingNewTellsNothing()
    {
        // A game that waits again without printing, as a timed one does
        // every time its timer runs out, is not read the last turn again.
        var told = new List<string>();
        var narration = new Narration { Spoken = told.Add };

        narration.Add("Time passes.");
        narration.Ready();
        narration.Ready();

        Assert.Equal(["Time passes."], told);
    }

    [Fact]
    public void WhatIsToldIsTheTextAndNotItsLayout()
    {
        // The blank lines that space paragraphs apart and the spaces that
        // line words up are for the eye.
        Assert.Equal("ZORK I\nWest of House", Narration.Tidy("\n  ZORK   I  \n\n\nWest of House\n\n"));
        Assert.Equal(string.Empty, Narration.Tidy(" \n \n"));
    }

    [Fact]
    public void NobodyListeningIsNoTrouble()
    {
        var narration = new Narration();

        narration.Add("Hello.");
        narration.Ready();
    }

    [Fact]
    public void TheZMachineToldIsTheLowerWindowAndNotThePlayersTyping()
    {
        // [zm 7.1.1.1] What the player types is echoed onto the screen,
        // but it is theirs, and reading it back to them is noise.
        var (screen, _) = Screen();
        var heard = new System.Text.StringBuilder();
        screen.Prose = text => heard.Append(text);

        screen.Print("North of House", default);
        screen.NewLine();
        screen.Print("You are facing", default);
        screen.WrapLine();
        screen.Print("the north side.", default);
        screen.Echo('n');
        screen.EchoNewLine();

        Assert.Equal("North of House\nYou are facing the north side.", heard.ToString());
    }

    [Fact]
    public void TheZMachineSaysWhenItBeginsToWait()
    {
        var (screen, input) = Screen();
        var waits = 0;
        input.Waiting = () => waits++;

        input.Enqueue(Zscii.Newline);
        input.ReadKey(null);

        Assert.Equal(1, waits);

        input.Enqueue(Zscii.Newline);
        input.WaitForAnyKey();

        Assert.Equal(2, waits);
    }

    private static (BufferedScreen Screen, BufferedInput Input) Screen()
    {
        var screen = new BufferedScreen(
            80,
            24,
            cursorStartsAtBottom: false,
            repaint: () => { },
            waitForKey: () => Zscii.Newline,
            fontWidth: 1,
            fontHeight: 1,
            capabilities: ScreenCapabilities.FixedGrid);

        return (screen, new BufferedInput(screen));
    }
}

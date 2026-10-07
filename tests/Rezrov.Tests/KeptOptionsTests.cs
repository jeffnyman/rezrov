using Rezrov.Gui;

namespace Rezrov.Tests;

/// <summary>
/// The options the graphical program keeps between sessions, and what a
/// window passes on to a story opened from it.
/// </summary>
public class KeptOptionsTests
{
    [Fact]
    public void AKeptFileIsReadAsTheCommandLineItWouldBe()
    {
        var options = KeptOptions.Read(
            """
            # a heading
            --font Iowan Old Style, Charter, Georgia, serif
            --size 18

            --map
            """);

        Assert.Equal(
            [["--font", "Iowan Old Style, Charter, Georgia, serif"], ["--size", "18"], ["--map"]],
            options);
    }

    [Fact]
    public void WhatCannotBeKeptIsPassedOver()
    {
        // A seed or a commands file belongs to one story, and a file that
        // kept one would do something to every story opened after it. A
        // valued option with no value, and words that are no option at
        // all, cost only themselves.
        var options = KeptOptions.Read(
            """
            --seed 5
            --commands walkthrough.txt
            --debug
            --size
            not an option
            --tandy
            """);

        Assert.Equal([["--tandy"]], options);
    }

    [Fact]
    public void WhatIsWrittenIsReadBackTheSame()
    {
        string[][] options =
        [
            ["--fixed", "Consolas, monospace"],
            ["--smoothing", "grayscale"],
            ["--padding", "12"],
            ["--interpreter", "amiga"],
            ["--tandy"],
        ];

        Assert.Equal(options, KeptOptions.Read(KeptOptions.Write(options)));
    }

    [Fact]
    public void AFileSaysWhatItIsBeforeAnyOption()
    {
        var text = KeptOptions.Write([["--size", "20"]]);

        Assert.StartsWith("#", text, StringComparison.Ordinal);
        Assert.EndsWith("--size 20\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WritingLeavesOutWhatCannotBeKept()
    {
        var text = KeptOptions.Write([["--seed", "5"], ["--map"]]);

        Assert.DoesNotContain("--seed", text, StringComparison.Ordinal);
        Assert.Contains("--map", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AStoryOpenedFromAWindowIsGivenThePlayersChoicesAndNoMore()
    {
        // The story is the first argument and is not passed on. How the
        // program looks and which machine it says it is go with their
        // values, word for word. What belonged to the story being played
        // stays behind, its value included.
        var carried = StoryOpener.Carried(
        [
            "zork1.z3",
            "--size", "18",
            "--seed", "5",
            "--font", "Georgia, serif",
            "--debug",
            "--map",
            "--blorb", "pictures.blb",
            "--interpreter", "amiga",
        ]);

        Assert.Equal(["--size", "18", "--font", "Georgia, serif", "--map", "--interpreter", "amiga"], carried);
    }

    [Fact]
    public void AnOptionLeftWithoutItsValueIsNotPassedOn()
    {
        Assert.Equal(["--tandy"], StoryOpener.Carried(["game.z3", "--tandy", "--size"]));
    }
}

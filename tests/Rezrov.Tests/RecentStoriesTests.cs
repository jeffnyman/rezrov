using Rezrov.Gui;

namespace Rezrov.Tests;

/// <summary>
/// The list of stories played most recently, as the Game menu offers it.
/// </summary>
public class RecentStoriesTests
{
    private static readonly string Zork = Story("zork1.z3");
    private static readonly string Advent = Story("advent.ulx");
    private static readonly string Cloak = Story("cloak.aastory");

    [Fact]
    public void AStoryJustOpenedGoesFirst()
    {
        Assert.Equal([Cloak, Zork, Advent], RecentStories.With([Zork, Advent], Cloak));
    }

    [Fact]
    public void AStoryPlayedAgainMovesUpRatherThanBeingNamedTwice()
    {
        Assert.Equal([Advent, Zork, Cloak], RecentStories.With([Zork, Cloak, Advent], Advent));
    }

    [Fact]
    public void TheOldestFallsOffTheEnd()
    {
        var full = Enumerable.Range(0, RecentStories.Most).Select(i => Story($"{i}.z5")).ToList();

        var after = RecentStories.With(full, Zork);

        Assert.Equal(RecentStories.Most, after.Count);
        Assert.Equal(Zork, after[0]);
        Assert.DoesNotContain(full[^1], after);
    }

    [Fact]
    public void WhatIsWrittenIsReadBackTheSame()
    {
        // The heading is a comment, so it is passed over on the way back.
        var text = RecentStories.Write([Zork, Advent, Cloak]);

        Assert.StartsWith("#", text);
        Assert.Equal([Zork, Advent, Cloak], RecentStories.Read(text));
    }

    [Fact]
    public void AFileEditedByHandCanBeWrongWithoutStoppingAnything()
    {
        // A blank line, a comment, a path relative to nowhere in
        // particular, and a story named twice are all passed over.
        var text = $"\n# a note\nzork1.z3\n  {Zork}  \r\n{Advent}\n{Zork}\n";

        Assert.Equal([Zork, Advent], RecentStories.Read(text));
    }

    [Fact]
    public void AFileWithMoreThanTheListHoldsIsCut()
    {
        var text = string.Join("\n", Enumerable.Range(0, RecentStories.Most + 5).Select(i => Story($"{i}.z5")));

        Assert.Equal(RecentStories.Most, RecentStories.Read(text).Count);
    }

    [Fact]
    public void ASystemThatIgnoresCaseSeesOneStoryWhereTheNamesDifferOnlyInCase()
    {
        // Windows and macOS do not tell zork1.z3 from ZORK1.Z3, so neither
        // may the list; Linux does, and so does the list there.
        var after = RecentStories.With([Zork], Zork.ToUpperInvariant());
        var ignores = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

        Assert.Equal(ignores ? 1 : 2, after.Count);
    }

    /// <summary>
    /// A whole path to a story, on whichever system the tests run on.
    /// </summary>
    private static string Story(string name) => Path.Combine(Path.GetTempPath(), "stories", name);
}

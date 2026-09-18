using Rezrov.Glulx.Glk;
using Rezrov.Gui;

namespace Rezrov.Tests;

/// <summary>
/// [glk #stream_style] What the eleven styles look like: the hints a
/// game sets, which windows each of them reaches, what a frontend makes
/// of them, and what the game is told when it asks.
/// </summary>
public class GlkStyleTests
{
    [Fact]
    public void AWindowKeepsTheHintsThatWereSetBeforeItOpened()
    {
        var glk = new GlkLibrary(new RecordingGlkDisplay { Styled = true });

        glk.SetStyleHint(WindowType.TextBuffer, GlkStyle.User1, StyleHint.Weight, 1);
        var first = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;

        // [glk #stream_style_hints] A hint set afterwards does not reach
        // a window that is already open: it is for the windows the game
        // makes next, which is what lets a library settle a window's
        // whole appearance the moment the window is made.
        glk.SetStyleHint(WindowType.TextBuffer, GlkStyle.User1, StyleHint.Weight, -1);
        var second = glk.OpenWindow(first, WindowMethod.Above | WindowMethod.Fixed, 3, WindowType.TextBuffer, 2)!;

        Assert.Equal(1u, glk.Measure(first, GlkStyle.User1, StyleHint.Weight));
        Assert.Equal(unchecked((uint)-1), glk.Measure(second, GlkStyle.User1, StyleHint.Weight));
    }

    [Fact]
    public void AHintForAllTypesIsTheGroundAndOneForAKindIsLaidOverIt()
    {
        var glk = new GlkLibrary(new RecordingGlkDisplay { Styled = true });

        glk.SetStyleHint(WindowType.AllTypes, GlkStyle.User1, StyleHint.Oblique, 1);
        glk.SetStyleHint(WindowType.AllTypes, GlkStyle.User1, StyleHint.Weight, 1);
        glk.SetStyleHint(WindowType.TextGrid, GlkStyle.User1, StyleHint.Weight, 0);

        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;
        var status = glk.OpenWindow(story, WindowMethod.Above | WindowMethod.Fixed, 1, WindowType.TextGrid, 2)!;

        // Both lean, since that was asked of every kind of window, and
        // only the buffer is heavy, since the grid said otherwise for
        // itself.
        Assert.Equal(1u, glk.Measure(story, GlkStyle.User1, StyleHint.Oblique));
        Assert.Equal(1u, glk.Measure(status, GlkStyle.User1, StyleHint.Oblique));
        Assert.Equal(1u, glk.Measure(story, GlkStyle.User1, StyleHint.Weight));
        Assert.Equal(0u, glk.Measure(status, GlkStyle.User1, StyleHint.Weight));
    }

    [Fact]
    public void ClearingAHintLeavesTheStyleAsItWouldHaveBeen()
    {
        var glk = new GlkLibrary(new RecordingGlkDisplay { Styled = true });

        glk.SetStyleHint(WindowType.AllTypes, GlkStyle.User1, StyleHint.Weight, 1);
        glk.ClearStyleHint(WindowType.AllTypes, GlkStyle.User1, StyleHint.Weight);

        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;

        Assert.Equal(0u, glk.Measure(story, GlkStyle.User1, StyleHint.Weight));
    }

    [Fact]
    public void ADisplayThatCannotSayWhatAStyleLooksLikeAnswersNothing()
    {
        // [glk #stream_style_check] Which is exactly what the
        // specification asks a library to do when it cannot tell.
        var glk = new GlkLibrary(new RecordingGlkDisplay());
        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;

        Assert.Null(glk.Measure(story, GlkStyle.Normal, StyleHint.Weight));
        Assert.False(glk.Distinguish(story, GlkStyle.Normal, GlkStyle.Header));

        // And a hint the library knows nothing about cannot be measured
        // either, however much the display can say.
        var styled = new GlkLibrary(new RecordingGlkDisplay { Styled = true });
        var window = styled.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;

        Assert.Null(styled.Measure(window, GlkStyle.Normal, (StyleHint)99));
    }

    [Fact]
    public void TwoStylesAreToldApartByHowTheyComeOutRatherThanByTheirHints()
    {
        var glk = new GlkLibrary(new RecordingGlkDisplay { Styled = true });

        glk.SetStyleHint(WindowType.AllTypes, GlkStyle.User1, StyleHint.Weight, 1);
        glk.SetStyleHint(WindowType.AllTypes, GlkStyle.User2, StyleHint.TextColor, 0x00FFFFFF);
        glk.SetStyleHint(WindowType.AllTypes, GlkStyle.User2, StyleHint.BackColor, 0x00000000);
        glk.SetStyleHint(WindowType.AllTypes, GlkStyle.User2, StyleHint.ReverseColor, 1);

        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;

        Assert.True(glk.Distinguish(story, GlkStyle.Normal, GlkStyle.User1));

        // [glk op:style_distinguish] The second names its colors the
        // other way round and then asks for them reversed, so it comes
        // out looking exactly like ordinary text however different its
        // hints are, and the game is told what it would see.
        Assert.False(glk.Distinguish(story, GlkStyle.Normal, GlkStyle.User2));
    }

    [Fact]
    public void TheStylesHaveTheLookAGameCanCountOnWithoutAsking()
    {
        // [glk #stream_style_hints] The specification says a game need
        // not ask for these: emphasis leans, a heading is heavy and
        // larger, and preformatted is fixed, which is the whole of what
        // makes it preformatted.
        var look = new GlkLook(WindowType.TextBuffer, GlkStyles.None, 16, 8);

        Assert.True(look.Of(GlkStyle.Emphasized).Oblique);
        Assert.Equal(1, look.Of(GlkStyle.Header).Weight);
        Assert.True(look.Of(GlkStyle.Header).Size > look.Of(GlkStyle.Normal).Size);
        Assert.False(look.Of(GlkStyle.Preformatted).Proportional);
        Assert.True(look.Of(GlkStyle.Normal).Proportional);
        Assert.Equal(16, look.Of(GlkStyle.Normal).Size);
        Assert.Equal((GlkLook.Ink, GlkLook.Paper), look.Of(GlkStyle.Normal).Colors);
        Assert.Equal(0, look.Of(GlkStyle.Normal).Indentation);
        Assert.Equal(Justification.LeftFlush, look.Of(GlkStyle.Normal).Justification);
    }

    [Fact]
    public void EachHintChangesTheStyleTheWayTheSpecificationSays()
    {
        var look = Look(
            WindowType.TextBuffer,
            (GlkStyle.User1, StyleHint.Weight, 1),
            (GlkStyle.User1, StyleHint.Oblique, 1),
            (GlkStyle.User1, StyleHint.Proportional, 0),
            (GlkStyle.User1, StyleHint.TextColor, 0x00FF0000),
            (GlkStyle.User1, StyleHint.BackColor, 0x000000FF),
            (GlkStyle.User2, StyleHint.Size, 1),
            (GlkStyle.User2, StyleHint.Indentation, 2),
            (GlkStyle.User2, StyleHint.ParaIndentation, -1));

        var one = look.Of(GlkStyle.User1);

        Assert.Equal(1, one.Weight);
        Assert.True(one.Oblique);
        Assert.False(one.Proportional);
        Assert.Equal((0x00FF0000u, 0x000000FFu), one.Colors);

        // The size hint is relative, and the indentation is counted in
        // whatever the display calls a step, which here is a character
        // of eight pixels. Both may go the other way.
        var two = look.Of(GlkStyle.User2);

        Assert.True(two.Size > 16);
        Assert.Equal(16, two.Indentation);
        Assert.Equal(-8, two.ParaIndentation);
    }

    [Fact]
    public void ReversingAStyleSwapsTheColorsWithoutNamingEither()
    {
        // [glk #stream_style_hints] Which the specification notes is
        // how a game asks for reverse video from a library that may
        // support no colors at all.
        var look = Look(WindowType.TextBuffer, (GlkStyle.Normal, StyleHint.ReverseColor, 1));

        Assert.Equal((GlkLook.Paper, GlkLook.Ink), look.Of(GlkStyle.Normal).Colors);
    }

    [Fact]
    public void AGridDeclinesTheHintsThatWouldBreakItsColumns()
    {
        // [glk #window_textgrid] A grid is a grid of cells. A
        // proportional face or a larger size in one would put the
        // characters out of their columns, and a game that asked for it
        // would be worse off than if it had not asked at all.
        var grid = Look(
            WindowType.TextGrid,
            (GlkStyle.Normal, StyleHint.Proportional, 1),
            (GlkStyle.Normal, StyleHint.Size, 3));

        Assert.False(grid.Of(GlkStyle.Normal).Proportional);
        Assert.Equal(16, grid.Of(GlkStyle.Normal).Size);

        // The colors of a grid are not a grid's business to refuse, and
        // reversing them is how a game asks for the status line every
        // player expects.
        var reversed = Look(WindowType.TextGrid, (GlkStyle.Normal, StyleHint.ReverseColor, 1));

        Assert.Equal((GlkLook.Paper, GlkLook.Ink), reversed.Of(GlkStyle.Normal).Colors);
    }

    [Fact]
    public void FullJustificationIsDeclinedAndSaidToBeDeclined()
    {
        // Setting a line against both edges means stretching the spaces
        // of a proportional line, which the layout does not do. Saying
        // so is the point: the game is not told one thing and shown
        // another.
        var look = Look(
            WindowType.TextBuffer,
            (GlkStyle.User1, StyleHint.Justification, (int)Justification.LeftRight),
            (GlkStyle.User2, StyleHint.Justification, (int)Justification.Centered));

        Assert.Equal(Justification.LeftFlush, look.Of(GlkStyle.User1).Justification);
        Assert.Equal(Justification.Centered, look.Of(GlkStyle.User2).Justification);
    }

    /// <summary>
    /// A frontend's styles for a window opened with the given hints,
    /// which are set and frozen the way a game would set and freeze
    /// them.
    /// </summary>
    private static GlkLook Look(WindowType type, params (GlkStyle Style, StyleHint Hint, int Value)[] hints)
    {
        var glk = new GlkLibrary(new RecordingGlkDisplay());

        foreach (var (style, hint, value) in hints)
        {
            glk.SetStyleHint(WindowType.AllTypes, style, hint, value);
        }

        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;
        var window = type == WindowType.TextBuffer
            ? story
            : glk.OpenWindow(story, WindowMethod.Above | WindowMethod.Fixed, 1, type, 2)!;

        return new GlkLook(type, window.Styles, 16, 8);
    }
}

using Rezrov.ZMachine;
using Rezrov.ZMachine.Screen;

namespace Rezrov.Tests;

/// <summary>
/// The screen model on its own: windows, cursors, buffering, paging,
/// styles, colors, fonts, and the status line, driven directly.
/// </summary>
public class ScreenModelTests
{
    [Fact]
    public void BufferedTextWrapsAtTheLastWordThatFits()
    {
        // [zm 7.2] and the example in [zm 8.8.3.1.2.2]: "Here is an
        // abacus" in a window 14 wide breaks before "abacus".
        var (screen, model) = Make(width: 14);

        Print(model, "Here is an abacus");
        model.Flush();

        Assert.Equal(["Here is an ", "abacus"], screen.Lines);
    }

    [Fact]
    public void UnbufferedTextWrapsAtTheLastCharacterThatFits()
    {
        // [zm 8.8.3.1.2.2] With buffering off, the break falls wherever
        // the width runs out.
        var (screen, model) = Make(width: 14);
        model.SetBuffering(false);

        Print(model, "Here is an abacus");

        Assert.Equal(["Here is an aba", "cus"], screen.Lines);
    }

    [Fact]
    public void AWordWiderThanTheScreenBreaksWhereItMust()
    {
        // [zm 7.2] The promise is only for words shorter than the width.
        var (screen, model) = Make(width: 10);

        Print(model, "ab supercalifragilistic");
        model.Flush();

        Assert.Equal(["ab superca", "lifragilis", "tic"], screen.Lines);
    }

    [Fact]
    public void ASpaceAtTheRightEdgeIsSwallowed()
    {
        var (screen, model) = Make(width: 5);

        Print(model, "abcde fgh");
        model.Flush();

        Assert.Equal(["abcde", "fgh"], screen.Lines);
    }

    [Fact]
    public void AStreamScreenIsNeverWrapped()
    {
        // Without a fixed grid the frontend wraps for itself.
        var (screen, model) = Make(width: 5, capabilities: ScreenCapabilities.None);

        Print(model, "abcdefgh ijk");
        model.Flush();

        Assert.Equal(["abcdefgh ijk"], screen.Lines);
    }

    [Fact]
    public void TurningBufferingOffFlushesTheWord()
    {
        var (screen, model) = Make();

        Print(model, "hello");
        Assert.Empty(screen.Runs);

        model.SetBuffering(false);

        Assert.Equal("hello", screen.Text);
    }

    [Fact]
    public void StyleChangesInsideAWordKeepItWhole()
    {
        // [zm 8.7.1.2] The style can change mid-word, and [zm 7.2] the
        // word still wraps as one.
        var (screen, model) = Make(width: 12);

        Print(model, "Here is an a");
        model.SetTextStyle((int)TextStyle.Bold);
        Print(model, "bacus");
        model.Flush();

        Assert.Equal(["Here is an ", "abacus"], screen.Lines);
        Assert.Equal(TextStyle.Roman, screen.Runs[^2].Attributes.Style);
        Assert.Equal(("bacus", TextStyle.Bold), (screen.Runs[^1].Text, screen.Runs[^1].Attributes.Style));
    }

    [Fact]
    public void MorePromptComesAfterAScreenful()
    {
        // [zm 8.4.1] A screen 5 high with a 1 line upper window has 4
        // lines for text, so the pause comes every 3 lines.
        var (screen, model) = Make(height: 5, version: ZMachineVersion.V5);
        model.SplitWindow(1);

        for (var i = 0; i < 6; i++)
        {
            Print(model, "line");
            model.NewLine();
        }

        Assert.Equal(2, screen.MorePrompts);

        // Input resets the count, and a command file suppresses it.
        model.PrepareForInput(suppressPaging: false);
        Print(model, "x");
        model.NewLine();
        Assert.Equal(2, screen.MorePrompts);

        model.PrepareForInput(suppressPaging: true);
        for (var i = 0; i < 10; i++)
        {
            model.NewLine();
        }

        Assert.Equal(2, screen.MorePrompts);
    }

    [Fact]
    public void TheUpperWindowIsAGridThatOverlaysAndNeverScrolls()
    {
        var (screen, model) = Make(width: 10, height: 6, version: ZMachineVersion.V5);
        model.SplitWindow(2);
        model.SetWindow(ScreenModel.Upper);

        // [zm 8.7.2] Selecting the upper window puts its cursor top left.
        Assert.Equal((1, 1), model.GetCursor());

        Print(model, "abcdefghijKLM");
        model.NewLine();
        Print(model, "second");
        model.NewLine();
        Print(model, "third");
        model.SetCursor(1, 3);
        Print(model, "XY");

        // [zm 8.7.3.1] The bottom right character is printed and the
        // rest dropped; [zm 8.6.1.1.1] printing overlays.
        Assert.Equal("abXYefghij", model.UpperWindow.RowText(1));
        Assert.Equal("thirdd    ", model.UpperWindow.RowText(2));
        Assert.Equal((1, 5), model.GetCursor());
        Assert.Empty(screen.Text);
    }

    [Fact]
    public void SplittingClearsTheUpperWindowOnlyInVersion3()
    {
        foreach (var version in new[] { ZMachineVersion.V3, ZMachineVersion.V5 })
        {
            var (_, model) = Make(version: version);
            model.SplitWindow(2);
            model.SetWindow(ScreenModel.Upper);
            Print(model, "kept");
            model.SetWindow(ScreenModel.Lower);

            model.SplitWindow(3);

            // [zm 8.6.1.1.2] against [zm 8.6.1]
            var expected = version == ZMachineVersion.V3 ? "    " : "kept";
            Assert.Equal(expected, model.UpperWindow.RowText(1)[..4]);
        }
    }

    [Fact]
    public void SplittingKeepsTheCursorIfItStillFits()
    {
        // [zm 8.7.2.1.1]
        var (_, model) = Make(version: ZMachineVersion.V5);
        model.SplitWindow(4);
        model.SetWindow(ScreenModel.Upper);
        model.SetCursor(3, 5);

        model.SplitWindow(3);
        Assert.Equal((3, 5), model.GetCursor());

        model.SplitWindow(2);
        Assert.Equal((1, 1), model.GetCursor());
    }

    [Fact]
    public void TheUpperWindowCannotBeTallerThanTheScreenLessTheStatusLine()
    {
        // [zm 8.6.1.1] In Version 3 the top line is the status line.
        var (_, v3) = Make(height: 10, version: ZMachineVersion.V3);
        v3.SplitWindow(50);
        Assert.Equal(9, v3.UpperWindow.Lines);

        var (_, v5) = Make(height: 10, version: ZMachineVersion.V5);
        v5.SplitWindow(50);
        Assert.Equal(10, v5.UpperWindow.Lines);
    }

    [Fact]
    public void ErasingMinusOneUnsplitsAndSelectsTheLowerWindow()
    {
        // [zm 8.7.3.3]
        var (screen, model) = Make(version: ZMachineVersion.V5);
        Print(model, "old text");
        model.NewLine();
        model.SplitWindow(3);
        model.SetWindow(ScreenModel.Upper);
        Print(model, "status");
        var erasures = screen.LowerErasures;

        Assert.True(model.EraseWindow(-1));

        Assert.Equal(0, model.UpperWindow.Lines);
        Assert.Equal(ScreenModel.Lower, model.CurrentWindow);
        Assert.Equal("      ", model.UpperWindow.RowText(1)[..6]);
        Assert.Equal(erasures + 1, screen.LowerErasures);
        Assert.Empty(screen.Text);
    }

    [Fact]
    public void ErasingMinusTwoClearsWithoutUnsplitting()
    {
        // [zm 8.8.5.3.2] as Frotz applies it to Version 5 too.
        var (_, model) = Make(version: ZMachineVersion.V5);
        model.SplitWindow(3);
        model.SetWindow(ScreenModel.Upper);
        model.SetCursor(2, 2);

        Assert.True(model.EraseWindow(-2));

        Assert.Equal(3, model.UpperWindow.Lines);
        Assert.Equal(ScreenModel.Upper, model.CurrentWindow);
        Assert.Equal((2, 2), model.GetCursor());
    }

    [Fact]
    public void ErasingAWindowPutsItsCursorAtTheTopLeftAndRejectsOtherNumbers()
    {
        // [zm 8.7.3.2.1]
        var (screen, model) = Make(version: ZMachineVersion.V5);
        model.SplitWindow(3);
        model.SetWindow(ScreenModel.Upper);
        Print(model, "abc");

        Assert.True(model.EraseWindow(ScreenModel.Upper));
        Assert.Equal((1, 1), model.GetCursor());
        Assert.Equal("   ", model.UpperWindow.RowText(1)[..3]);

        Assert.True(model.EraseWindow(ScreenModel.Lower));
        Assert.Equal(2, screen.LowerErasures);

        Assert.False(model.EraseWindow(2));
        Assert.False(model.EraseWindow(-3));
    }

    [Fact]
    public void EraseLineClearsToTheRightInTheUpperWindow()
    {
        // [zm 8.7.3.4]
        var (screen, model) = Make(width: 8, version: ZMachineVersion.V5);
        model.SplitWindow(1);
        model.SetWindow(ScreenModel.Upper);
        Print(model, "abcdefgh");
        model.SetCursor(1, 4);

        model.EraseLine();

        Assert.Equal("abc     ", model.UpperWindow.RowText(1));
        Assert.Equal(0, screen.LineErasures);

        model.SetWindow(ScreenModel.Lower);
        model.EraseLine();
        Assert.Equal(1, screen.LineErasures);
    }

    [Fact]
    public void SetCursorOnlyWorksInTheUpperWindowAndSplitsToFit()
    {
        var (_, model) = Make(height: 10, version: ZMachineVersion.V5);
        model.SplitWindow(2);

        // [zm 8.7.2.3.1] No effect in the lower window.
        Assert.True(model.SetCursor(2, 2));
        Assert.Equal((1, 1), model.GetCursor());

        model.SetWindow(ScreenModel.Upper);
        Assert.True(model.SetCursor(2, 5));
        Assert.Equal((2, 5), model.GetCursor());

        // The remarks on section 8: a row below the split gets an
        // implicit split, with a diagnostic.
        Assert.False(model.SetCursor(4, 1));
        Assert.Equal(4, model.UpperWindow.Lines);
        Assert.Equal((4, 1), model.GetCursor());

        // Off the edge: column 1, as Frotz has it, and a diagnostic.
        Assert.False(model.SetCursor(1, 500));
        Assert.Equal((1, 1), model.GetCursor());
        Assert.False(model.SetCursor(-1, 1));
    }

    [Fact]
    public void StylesCombineAndRomanClearsThem()
    {
        // [zm op:set_text_style] Standard 1.1 combinations.
        var (_, model) = Make(version: ZMachineVersion.V5);

        model.SetTextStyle(2);
        model.SetTextStyle(4);
        Assert.Equal(TextStyle.Bold | TextStyle.Italic, model.Style);

        model.SetTextStyle(9);
        Assert.Equal(TextStyle.Bold | TextStyle.Italic | TextStyle.ReverseVideo | TextStyle.FixedPitch, model.Style);

        model.SetTextStyle(0);
        Assert.Equal(TextStyle.Roman, model.Style);
    }

    [Fact]
    public void FixedPitchIsForcedByTheHeaderBitFont4AndTheUpperWindow()
    {
        var (_, model, memory) = MakeWithMemory(version: ZMachineVersion.V5);
        Assert.False(model.Attributes.IsFixedPitch);

        // [zm 8.1] Bit 1 of Flags 2.
        memory.WriteWord(0x10, 0x0002);
        Assert.True(model.Attributes.Style.HasFlag(TextStyle.FixedPitch));
        memory.WriteWord(0x10, 0);

        // [zm 8.1] Font 4.
        Assert.Equal(1, model.SetFont(4));
        Assert.True(model.Attributes.IsFixedPitch);
        Assert.Equal(4, model.SetFont(1));

        // [zm 8.7.2.4] The upper window.
        model.SplitWindow(1);
        model.SetWindow(ScreenModel.Upper);
        Assert.True(model.Attributes.Style.HasFlag(TextStyle.FixedPitch));
    }

    [Fact]
    public void FontsAreAvailableAsTheScreenDeclares()
    {
        // [zm op:set_font] and [zm 8.1.2] to [zm 8.1.5]
        var (_, model) = Make(version: ZMachineVersion.V5, capabilities: ScreenCapabilities.FixedGrid | ScreenCapabilities.CharacterGraphicsFont);

        Assert.Equal(1, model.SetFont(0));
        Assert.Equal(0, model.SetFont(2));
        Assert.Equal(0, model.SetFont(4));
        Assert.Equal(1, model.SetFont(3));
        Assert.Equal(3, model.Font);
        Assert.Equal(3, model.SetFont(1));
        Assert.Equal(0, model.SetFont(7));
    }

    [Fact]
    public void ColorsResolveCurrentAndDefaultAndRejectVersion6Colors()
    {
        // [zm 8.3.1]
        var (screen, model) = Make(version: ZMachineVersion.V5);
        Assert.Equal((ScreenColor.White, ScreenColor.Blue), (model.Foreground, model.Background));

        Assert.True(model.SetColors(ScreenColor.Red, ScreenColor.Current));
        Assert.Equal((ScreenColor.Red, ScreenColor.Blue), (model.Foreground, model.Background));

        Assert.True(model.SetColors(ScreenColor.Current, ScreenColor.Default));
        Assert.Equal((ScreenColor.Red, screen.DefaultBackground), (model.Foreground, model.Background));

        Assert.False(model.SetColors(ScreenColor.LightGray, ScreenColor.Black));
        Assert.False(model.SetColors(ScreenColor.Black, ScreenColor.Transparent));
        Assert.False(model.SetColors(ScreenColor.UnderCursor, ScreenColor.Black));
        Assert.Equal(ScreenColor.Red, model.Foreground);
    }

    [Fact]
    public void SettingColorsFlushesBufferedTextInTheOldColors()
    {
        // [zm op:set_colour]
        var (screen, model) = Make(version: ZMachineVersion.V5);
        Print(model, "old");
        model.SetColors(ScreenColor.Green, ScreenColor.Black);
        Print(model, "new");
        model.Flush();

        Assert.Equal(ScreenColor.White, screen.Runs[0].Attributes.Foreground);
        Assert.Equal(ScreenColor.Green, screen.Runs[1].Attributes.Foreground);
    }

    [Fact]
    public void TrueColorsMapToTheNearestStandardColor()
    {
        // [zm 8.3.7] and [zm 8.3.7.1]
        var (_, model) = Make(version: ZMachineVersion.V5);

        Assert.True(model.SetTrueColors(0x001F, 0x7FFF));
        Assert.Equal((ScreenColor.Red, ScreenColor.White), (model.Foreground, model.Background));

        Assert.True(model.SetTrueColors(ScreenColors.TrueCurrent, ScreenColors.TrueDefault));
        Assert.Equal((ScreenColor.Red, ScreenColor.Blue), (model.Foreground, model.Background));

        Assert.False(model.SetTrueColors(ScreenColors.TrueUnderCursor, 0));
        Assert.False(model.SetTrueColors(0, ScreenColors.TrueTransparent));

        // [zm 8.3.1] The recommended equivalences round trip.
        for (var color = ScreenColor.Black; color <= ScreenColor.White; color++)
        {
            Assert.Equal(color, ScreenColors.Nearest(ScreenColors.ToTrueColor(color), ZMachineVersion.V5));
        }

        Assert.Equal(ScreenColor.DarkGray, ScreenColors.Nearest(0x2D6B, ZMachineVersion.V6));
        Assert.NotEqual(ScreenColor.DarkGray, ScreenColors.Nearest(0x2D6B, ZMachineVersion.V5));
    }

    [Fact]
    public void TheStatusLineShowsScoreAndTurnsOrTheTime()
    {
        // [zm 8.2] in the format the standard's author prefers.
        var (screen, model) = Make(width: 40, version: ZMachineVersion.V3);
        var updates = screen.UpperWindowUpdates;

        model.ShowStatusLine("Hall of Mists", timeGame: false, 80, 733);
        Assert.Equal(" Hall of Mists".PadRight(33) + "80/733 ", model.StatusLineText);
        Assert.True(model.StatusLine![0].Attributes.Style.HasFlag(TextStyle.ReverseVideo));
        Assert.Equal(updates + 1, screen.UpperWindowUpdates);

        // [zm 8.2.3.2] Twelve-hour with AM and PM; midnight is 12 AM.
        model.ShowStatusLine("Lincoln Memorial", timeGame: true, 12, 3);
        Assert.Equal(" Lincoln Memorial".PadRight(31) + "12:03 PM ", model.StatusLineText);
        model.ShowStatusLine("Lincoln Memorial", timeGame: true, 0, 7);
        Assert.EndsWith("12:07 AM ", model.StatusLineText);
        model.ShowStatusLine("Lincoln Memorial", timeGame: true, 16, 30);
        Assert.EndsWith(" 4:30 PM ", model.StatusLineText);
    }

    [Fact]
    public void ALongLocationNameIsBrokenAtASpaceWithAnEllipsis()
    {
        // [zm 8.2.2.2]
        var (_, model) = Make(width: 30, version: ZMachineVersion.V3);

        model.ShowStatusLine("The Very Long Hall of Many Mists", timeGame: false, 0, 0);

        Assert.Equal(" The Very Long Hall...".PadRight(26) + "0/0 ", model.StatusLineText);
    }

    [Fact]
    public void TheStatusLineExistsOnlyUpToVersion3()
    {
        var (_, model) = Make(version: ZMachineVersion.V5);

        model.ShowStatusLine("Nowhere", timeGame: false, 0, 0);

        Assert.Null(model.StatusLine);
        Assert.Equal(0, model.StatusLineRows);
    }

    [Fact]
    public void UnprintableUnicodeBecomesAQuestionMark()
    {
        // [zm 3.8.5.4.3]
        var (screen, model) = Make();
        screen.Unprintable.Add('☺');

        model.PrintUnicode('é');
        model.PrintUnicode('☺');
        model.PrintUnicode((char)0x01);
        model.Flush();

        Assert.Equal("é??", screen.Text);
        Assert.True(model.CanPrint('a'));
        Assert.False(model.CanPrint('☺'));
    }

    [Fact]
    public void TheFrontendIsToldToRepaintAtInputAndWhenLeavingTheUpperWindow()
    {
        var (screen, model) = Make(version: ZMachineVersion.V5);
        var updates = screen.UpperWindowUpdates;

        model.SplitWindow(1);
        Assert.Equal(updates + 1, screen.UpperWindowUpdates);

        model.SetWindow(ScreenModel.Upper);
        Print(model, "abc");
        Assert.Equal(updates + 1, screen.UpperWindowUpdates);

        model.SetWindow(ScreenModel.Lower);
        Assert.Equal(updates + 2, screen.UpperWindowUpdates);

        model.PrepareForInput(false);
        Assert.Equal(updates + 3, screen.UpperWindowUpdates);
        Assert.Same(model, screen.LastModel);
    }

    [Fact]
    public void ResetPutsEverythingBackAsAtTheStartOfAGame()
    {
        // [zm 8.7.3.3] and [zm 7.2.1]
        var (screen, model) = Make(version: ZMachineVersion.V5);
        model.SplitWindow(2);
        model.SetWindow(ScreenModel.Upper);
        model.SetTextStyle(2);
        model.SetColors(ScreenColor.Red, ScreenColor.Green);
        model.SetFont(4);
        model.SetBuffering(false);

        model.Reset();

        Assert.Equal(0, model.UpperWindow.Lines);
        Assert.Equal(ScreenModel.Lower, model.CurrentWindow);
        Assert.Equal(TextStyle.Roman, model.Style);
        Assert.Equal((ScreenColor.White, ScreenColor.Blue), (model.Foreground, model.Background));
        Assert.Equal(1, model.Font);
        Assert.True(model.IsBuffering);
        Assert.Equal(2, screen.LowerErasures);
    }

    private static (RecordingScreen Screen, ScreenModel Model) Make(
        int width = 80, int height = 24, ZMachineVersion version = ZMachineVersion.V5, ScreenCapabilities? capabilities = null)
    {
        var (screen, model, _) = MakeWithMemory(width, height, version, capabilities);
        return (screen, model);
    }

    private static (RecordingScreen Screen, ScreenModel Model, ZMemory Memory) MakeWithMemory(
        int width = 80, int height = 24, ZMachineVersion version = ZMachineVersion.V5, ScreenCapabilities? capabilities = null)
    {
        var bytes = new byte[2048];
        bytes[0] = (byte)version;
        PutWord(bytes, 0x04, 0x0400);
        PutWord(bytes, 0x06, 0x0400);
        PutWord(bytes, 0x08, 0x0380);
        PutWord(bytes, 0x0A, 0x0200);
        PutWord(bytes, 0x0C, 0x0100);
        PutWord(bytes, 0x0E, 0x0400);
        bytes[0x0381] = 6;

        var memory = new ZMemory(bytes);
        var screen = new RecordingScreen(width, height, capabilities);
        return (screen, new ScreenModel(screen, new StoryHeader(memory), memory), memory);
    }

    private static void PutWord(byte[] bytes, int address, int value)
    {
        bytes[address] = (byte)(value >> 8);
        bytes[address + 1] = (byte)value;
    }

    private static void Print(ScreenModel model, string text)
    {
        foreach (var c in text)
        {
            model.Print(c);
        }
    }
}

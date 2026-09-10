using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Screen;
using static Rezrov.Tests.Assembler;

namespace Rezrov.Tests;

/// <summary>
/// The screen opcodes of section 8, run through the interpreter with a
/// recording screen so that both the model's state and what reached the
/// frontend can be checked.
/// </summary>
public partial class InterpreterTests
{
    private const int Table = 0x300;

    [Fact]
    public void TextGoesToWhicheverWindowIsSelected()
    {
        var screen = new RecordingScreen(20, 10);
        var run = Execute(
            new Assembler()
                .Variable(Op.SplitWindow, true, Small(2))
                .Variable(Op.SetWindow, true, Small(1))
                .Variable(Op.SetCursor, true, Small(1), Small(3))
                .Short0(Op.Print).Text("hi")
                .Variable(Op.SetWindow, true, Small(0))
                .Short0(Op.Print).Text("lo")
                .Quit(),
            screen: screen);

        // [zm op:split_window], [zm op:set_window], [zm op:set_cursor]
        var model = run.Interpreter.Screen!;
        Assert.Equal(2, model.UpperWindow.Lines);
        Assert.Equal("  hi", model.UpperWindow.RowText(1)[..4]);
        Assert.Equal("lo", screen.Text);
        Assert.Equal(ScreenModel.Lower, model.CurrentWindow);
    }

    [Fact]
    public void GetCursorWritesTheUpperWindowsPositionEvenFromTheLowerWindow()
    {
        var run = Execute(
            new Assembler()
                .Variable(Op.SplitWindow, true, Small(3))
                .Variable(Op.SetWindow, true, Small(1))
                .Variable(Op.SetCursor, true, Small(2), Small(4))
                .Variable(Op.GetCursor, true, Large(Table))
                .Variable(Op.SetWindow, true, Small(0))
                .Variable(Op.GetCursor, true, Large(Table + 4))
                .Quit(),
            screen: new RecordingScreen());

        // [zm op:get_cursor] and [zm 8.7.2.3.2]
        var memory = run.Interpreter.Memory;
        Assert.Equal((2, 4), (memory.ReadWord(Table), memory.ReadWord(Table + 2)));
        Assert.Equal((2, 4), (memory.ReadWord(Table + 4), memory.ReadWord(Table + 6)));
    }

    [Fact]
    public void EraseWindowReportsNumbersThatNameNoWindow()
    {
        var run = Execute(
            new Assembler()
                .Variable(Op.SplitWindow, true, Small(3))
                .Variable(Op.EraseWindow, true, Large(0xFFFF))
                .Variable(Op.EraseWindow, true, Small(5))
                .Variable(Op.SetWindow, true, Small(7))
                .Quit(),
            screen: new RecordingScreen());

        // [zm op:erase_window] -1 unsplits; 5 and window 7 are not
        // windows before Version 6.
        Assert.Equal(0, run.Interpreter.Screen!.UpperWindow.Lines);
        Assert.Equal(2, run.Interpreter.RuntimeErrors.Count);
        Assert.Contains("erase_window", run.Interpreter.RuntimeErrors[0]);
        Assert.Contains("set_window", run.Interpreter.RuntimeErrors[1]);
    }

    [Fact]
    public void EraseLineOnlyActsOnAValueOfOne()
    {
        var screen = new RecordingScreen();
        Execute(
            new Assembler()
                .Variable(Op.EraseLine, true, Small(2))
                .Variable(Op.EraseLine, true, Small(1))
                .Quit(),
            screen: screen);

        // [zm op:erase_line]
        Assert.Equal(1, screen.LineErasures);
    }

    [Fact]
    public void SetTextStyleChangesTheRunsAndRejectsUnknownBits()
    {
        var screen = new RecordingScreen();
        var run = Execute(
            new Assembler()
                .Variable(Op.SetTextStyle, true, Small(2))
                .Short0(Op.Print).Text("bold")
                .Variable(Op.SetTextStyle, true, Small(0))
                .Short0(Op.Print).Text("plain")
                .Variable(Op.SetTextStyle, true, Small(16))
                .Quit(),
            screen: screen);

        // [zm op:set_text_style]
        Assert.Equal(("bold", TextStyle.Bold), (screen.Runs[0].Text, screen.Runs[0].Attributes.Style));
        Assert.Equal(("plain", TextStyle.Roman), (screen.Runs[1].Text, screen.Runs[1].Attributes.Style));
        Assert.Contains("set_text_style", Assert.Single(run.Interpreter.RuntimeErrors));
    }

    [Fact]
    public void BufferModeSwitchesWordBufferingOff()
    {
        var screen = new RecordingScreen();
        var run = Execute(
            new Assembler()
                .Variable(Op.BufferMode, true, Small(0))
                .Short0(Op.Print).Text("abc")
                .Quit(),
            screen: screen);

        // [zm op:buffer_mode] With buffering off every character is
        // sent on its own.
        Assert.False(run.Interpreter.Screen!.IsBuffering);
        Assert.Equal(3, screen.Runs.Count);
    }

    [Fact]
    public void SetColourAndSetTrueColourChangeTheColors()
    {
        var run = Execute(
            new Assembler()
                .Long(Op.SetColour, Small(3), Small(4))
                .Long(Op.SetColour, Small(10), Small(2))
                .Ext(13, Large(0x7FFF), Large(0x0000))
                .Quit(),
            screen: new RecordingScreen());

        // [zm op:set_colour] Red on green, then an illegal Version 6
        // color, then [zm op:set_true_colour] white on black.
        var model = run.Interpreter.Screen!;
        Assert.Equal((ScreenColor.White, ScreenColor.Black), (model.Foreground, model.Background));
        Assert.Contains("set_colour", Assert.Single(run.Interpreter.RuntimeErrors));
    }

    [Fact]
    public void SetFontStoresThePreviousFontOrZero()
    {
        var run = Execute(
            new Assembler()
                .Ext(4, Small(4)).Store(G0)
                .Ext(4, Small(2)).Store(G1)
                .Ext(4, Small(0)).Store(G2)
                .Quit(),
            screen: new RecordingScreen());

        // [zm op:set_font]
        Assert.Equal(1, run.Global(G0));
        Assert.Equal(0, run.Global(G1));
        Assert.Equal(4, run.Global(G2));
    }

    [Fact]
    public void PrintTablePrintsARectangleDownFromTheCursor()
    {
        var screen = new RecordingScreen(20, 10);
        var run = Execute(
            new Assembler()
                .Variable(Op.SplitWindow, true, Small(3))
                .Variable(Op.SetWindow, true, Small(1))
                .Variable(Op.SetCursor, true, Small(2), Small(2))
                .Variable(Op.PrintTable, true, Large(Table), Small(3), Small(2), Small(1))
                .Variable(Op.SetWindow, true, Small(0))
                .Variable(Op.PrintTable, true, Large(Table), Small(3))
                .Quit(),
            story => "abcXdefX"u8.CopyTo(story.Bytes.AsSpan(Table)),
            screen: screen);

        // [zm op:print_table] Width 3, height 2, skipping the X between
        // rows in the upper window; a single row in the lower.
        var model = run.Interpreter.Screen!;
        Assert.Equal(" abc", model.UpperWindow.RowText(2)[..4]);
        Assert.Equal(" def", model.UpperWindow.RowText(3)[..4]);
        Assert.Equal("abc", screen.Text);
    }

    [Fact]
    public void PrintUnicodeAndCheckUnicodeAgreeWithTheScreen()
    {
        var screen = new RecordingScreen();
        screen.Unprintable.Add('☺');
        var run = Execute(
            new Assembler()
                .Ext(11, Large(0xE9))
                .Ext(12, Large(0xE9)).Store(G0)
                .Ext(12, Large('☺')).Store(G1)
                .Ext(12, Large('€')).Store(G2)
                .Quit(),
            screen: screen);

        // [zm op:print_unicode] and [zm op:check_unicode]: e-acute can be
        // shown and typed, the euro sign shown but not typed, and the
        // face neither.
        Assert.Equal("é", screen.Text);
        Assert.Equal(3, run.Global(G0));
        Assert.Equal(0, run.Global(G1));
        Assert.Equal(1, run.Global(G2));
    }

    [Fact]
    public void ReadRedrawsTheStatusLineInVersion3()
    {
        var run = Execute(
            new Assembler()
                .Long(Op.Store, Small(G0), Small(1))
                .Long(Op.Store, Small(G1), Small(5))
                .Long(Op.Store, Small(G2), Small(7))
                .Variable(Op.Sread, true, Large(TextBuffer), Large(ParseBuffer))
                .Quit(),
            story =>
            {
                story.Bytes[TextBuffer] = 20;
                story.Bytes[ParseBuffer] = 5;
            },
            ZMachineVersion.V3,
            new ScriptedInput("look"),
            new RecordingScreen(30, 10));

        // [zm 8.2.4] and [zm 10.5.1] Object 1 is "room"; score 5, turns 7.
        Assert.Equal(" room".PadRight(26) + "5/7 ", run.Interpreter.Screen!.StatusLineText);
    }

    [Fact]
    public void ShowStatusDrawsTheLineInVersion3AndIsANopLater()
    {
        var v3 = Execute(
            new Assembler()
                .Long(Op.Store, Small(G0), Small(2))
                .Short0(Op.ShowStatus)
                .Quit(),
            version: ZMachineVersion.V3,
            screen: new RecordingScreen(30, 10));

        // [zm op:show_status] Object 2 is the lamp.
        Assert.Equal(" lamp".PadRight(26) + "0/0 ", v3.Interpreter.Screen!.StatusLineText);

        var v5 = Execute(
            new Assembler()
                .Long(Op.Store, Small(G0), Small(2))
                .Short0(Op.ShowStatus)
                .Quit(),
            screen: new RecordingScreen(30, 10));

        Assert.Null(v5.Interpreter.Screen!.StatusLineText);
    }

    [Fact]
    public void TheStatusLineShowsTheTimeWhenFlags1SaysSo()
    {
        var run = Execute(
            new Assembler()
                .Long(Op.Store, Small(G0), Small(1))
                .Long(Op.Store, Small(G1), Small(13))
                .Long(Op.Store, Small(G2), Small(5))
                .Short0(Op.ShowStatus)
                .Quit(),
            story => story.Bytes[0x01] = 0x02,
            ZMachineVersion.V3,
            screen: new RecordingScreen(30, 10));

        // [zm 8.2.1] and [zm 8.2.3.2]
        Assert.EndsWith("1:05 PM ", run.Interpreter.Screen!.StatusLineText);
    }

    [Fact]
    public void AStatusLineWithoutAnObjectIsAnError()
    {
        var run = Execute(
            new Assembler()
                .Long(Op.Store, Small(G0), Small(0))
                .Short0(Op.ShowStatus)
                .Quit(),
            version: ZMachineVersion.V3,
            screen: new RecordingScreen(30, 10));

        // [zm 8.2.2.1] The interpreter protects itself and says so.
        Assert.Equal("".PadRight(26) + "0/0 ", run.Interpreter.Screen!.StatusLineText);
        Assert.Contains("show_status", Assert.Single(run.Interpreter.RuntimeErrors));
    }

    [Fact]
    public void TheHeaderDescribesTheScreen()
    {
        var story = new Story(ZMachineVersion.V5);
        story.PutWord(0x10, 0x01D8);
        var memory = new ZMemory(story.Bytes);
        var screen = new RecordingScreen(60, 20);

        var interpreter = new Interpreter(memory, screen, new ScriptedInput());
        var header = interpreter.Header;

        // [zm 8.4] and [zm 8.4.3] Sizes, [zm 8.1.1] 1 by 1 units,
        // [zm 8.3.3] default colors, [zm 11.1.3] interpreter number and
        // version, [zm 11.1.5] the standard obeyed.
        Assert.Equal((60, 20), (header.ScreenWidthCharacters, header.ScreenHeightLines));
        Assert.Equal((60, 20), (header.ScreenWidthUnits, header.ScreenHeightUnits));
        Assert.Equal((1, 1), (header.FontWidthUnits, header.FontHeightUnits));
        Assert.Equal(((byte)ScreenColor.Blue, (byte)ScreenColor.White), (header.DefaultBackgroundColor, header.DefaultForegroundColor));
        Assert.Equal(InterpreterNumber.IbmPc, header.InterpreterNumber);
        Assert.Equal((byte)'R', header.InterpreterVersion);
        Assert.Equal((1, 1), (header.StandardRevisionMajor, header.StandardRevisionMinor));

        // [zm 8.3.3] and [zm 8.7.1.1] Colors, bold, italic, and fixed
        // pitch are all available here; timed input is not.
        Assert.Equal(
            Flags1FromVersion4.ColorsAvailable | Flags1FromVersion4.BoldfaceAvailable
                | Flags1FromVersion4.ItalicAvailable | Flags1FromVersion4.FixedSpaceAvailable,
            header.Flags1FromVersion4);

        // [zm 11.1.2] The story asked for pictures, undo, colors, sound,
        // and menus; colors survive since [zm 8.3.4] that bit is the
        // game's own, and [zm 6.1.4] undo survives because it is
        // provided.
        Assert.Equal(Flags2.WantsColors | Flags2.WantsUndo, header.Flags2);
    }

    [Fact]
    public void TheHeaderSaysWhenThereIsNoStatusLineOrUpperWindow()
    {
        var story = new Story(ZMachineVersion.V3);
        story.Bytes[0x01] = 0x20;
        var memory = new ZMemory(story.Bytes);

        var plain = new Interpreter(memory, new TextWriterScreen(new StringWriter()), new ScriptedInput());

        // [zm 8.2] Bit 4 set: no status line. [zm 8.6.1.2] Bit 5 clear:
        // no upper window.
        Assert.Equal(Flags1Versions1To3.StatusLineUnavailable, plain.Header.Flags1Versions1To3);

        var grid = new Interpreter(memory, new RecordingScreen(), new ScriptedInput());

        Assert.Equal(Flags1Versions1To3.ScreenSplittingAvailable, grid.Header.Flags1Versions1To3);
    }

    [Fact]
    public void RestartPutsTheScreenBackAsAtTheStart()
    {
        var run = Execute(
            new Assembler()
                .Variable(Op.Loadb, false, Large(0x10), Small(1)).Store(0)
                .Short1(Op.Jz, Var(0)).Branch(false, 15)
                .Variable(Op.SplitWindow, true, Small(2))
                .Long(Op.Store, Small(G0), Small(1))
                .Variable(Op.Storeb, true, Large(0x10), Small(1), Small(2))
                .Short0(Op.Restart)
                .Quit(),
            screen: new RecordingScreen());

        // [zm 6.1.3] and [zm 8.7.3.3]
        Assert.Equal(0, run.Global(G0));
        Assert.Equal(0, run.Interpreter.Screen!.UpperWindow.Lines);
    }

    [Fact]
    public void Version6GamesGetTheWindowedModel()
    {
        // [zm 5.4] A Version 6 game starts in its main routine, and
        // [zm 8.8] its screen opcodes go to the eight-window model.
        var main = new Assembler().Variable(Op.SplitWindow, true, Small(3)).Quit().ToArray();

        var run = Execute(
            new Assembler(),
            story => story.Routine(RoutineB, 0, main),
            ZMachineVersion.V6,
            screen: new RecordingScreen());

        Assert.Null(run.Interpreter.Screen);
        Assert.Equal(3, run.Interpreter.Windows!.Windows[1].Height);
        Assert.Same(run.Interpreter.Windows, run.Interpreter.Display);
    }
}

using Rezrov.Core.Blorb;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Screen;
using static Rezrov.Tests.Assembler;

namespace Rezrov.Tests;

/// <summary>
/// The Version 6 screen model of section 8.8, run through the
/// interpreter with a recording screen: eight windows, their cursors
/// and attributes, pictures from a resource file, and the header the
/// game reads to learn all this.
/// </summary>
public partial class InterpreterTests
{
    // A routine for newline interrupts, at an address that packs
    // evenly in Version 6.
    private const int Interrupt = 0x1400;

    private static readonly ScreenCapabilities GridWithPictures =
        ScreenCapabilities.StatusLine | ScreenCapabilities.UpperWindow | ScreenCapabilities.Colors
        | ScreenCapabilities.Bold | ScreenCapabilities.Italic | ScreenCapabilities.FixedPitch
        | ScreenCapabilities.FixedGrid | ScreenCapabilities.Pictures;

    // [zm 5.4] A Version 6 game starts in its main routine.
    private static Run RunVersion6(Assembler main, IScreen? screen = null, Action<Story>? setup = null, Action<Interpreter>? before = null, IInput? input = null, InterpreterNumber? interpreterNumber = null) =>
        Execute(
            new Assembler(),
            story =>
            {
                story.Routine(RoutineB, 0, main.ToArray());
                setup?.Invoke(story);
            },
            ZMachineVersion.V6,
            input: input,
            screen: screen ?? new RecordingScreen(),
            before: before,
            interpreterNumber: interpreterNumber);

    // Follows a branching instruction with a store that only happens
    // when the branch is not taken, so the global says which.
    private static Assembler NotTakenInto(Assembler code, int global) =>
        code.Branch(true, SkipStore).Long(Op.Store, Small(global), Small(1));

    [Fact]
    public void WindowsStartAsSection883Says()
    {
        var run = RunVersion6(new Assembler().Quit());

        var windows = run.Interpreter.Windows!;
        var zero = windows.Windows[0];

        // [zm 8.8.3.3] Window 0 fills the screen and is selected, window 1
        // is as wide as the screen with no height, the rest have no size.
        Assert.Null(run.Interpreter.Screen);
        Assert.Equal(0, windows.CurrentWindow);
        Assert.Equal((1, 1, 24, 80), (zero.Y, zero.X, zero.Height, zero.Width));
        Assert.Equal(WindowAttributes.Wrapping | WindowAttributes.Scrolling | WindowAttributes.Transcript | WindowAttributes.Buffering, zero.Attributes);
        Assert.Equal((0, 80, WindowAttributes.Buffering), (windows.Windows[1].Height, windows.Windows[1].Width, windows.Windows[1].Attributes));
        Assert.Equal((0, 0), (windows.Windows[7].Height, windows.Windows[7].Width));
        Assert.True(windows.CursorVisible);
    }

    [Fact]
    public void SplitWindowTilesWindowsZeroAndOneAndErasingMinusOneUnsplits()
    {
        var split = RunVersion6(new Assembler().Variable(Op.SplitWindow, true, Small(3)).Quit()).Interpreter.Windows!;

        // [zm 8.8.4.1] Window 1 gets the height at the top, window 0 what
        // is left below it.
        Assert.Equal((1, 3), (split.Windows[1].Y, split.Windows[1].Height));
        Assert.Equal((4, 21), (split.Windows[0].Y, split.Windows[0].Height));

        var erased = RunVersion6(new Assembler()
            .Variable(Op.SplitWindow, true, Small(3))
            .Variable(Op.SetWindow, true, Small(1))
            .Variable(Op.EraseWindow, true, Large(0xFFFF))
            .Quit()).Interpreter.Windows!;

        // [zm 8.8.5.3.1] and [zm 8.8.4.2]
        Assert.Equal(0, erased.Windows[1].Height);
        Assert.Equal((1, 24), (erased.Windows[0].Y, erased.Windows[0].Height));
        Assert.Equal(0, erased.CurrentWindow);
    }

    [Fact]
    public void TextLandsAtEachWindowsOwnCursor()
    {
        var run = RunVersion6(new Assembler()
            .Ext(17, Small(1), Small(2), Small(80))
            .Variable(Op.SetWindow, true, Small(1))
            .Variable(Op.SetCursor, true, Small(2), Small(3))
            .Short0(Op.Print).Text("hi")
            .Variable(Op.SetWindow, true, Small(0))
            .Short0(Op.Print).Text("lo")
            .Quit());

        var windows = run.Interpreter.Windows!;

        // [zm 8.8.3.5] Each window remembers its own cursor.
        Assert.Equal("  hi", windows.RowText(1)[..4]);
        Assert.Equal("lo", windows.RowText(0)[..2]);
        Assert.Equal((2, 5), (windows.Windows[1].CursorY, windows.Windows[1].CursorX));
        Assert.Equal((1, 3), (windows.Windows[0].CursorY, windows.Windows[0].CursorX));
    }

    [Theory]
    [InlineData(0, 0, "here is an    ", "abacus        ", 7)]
    [InlineData(1, 2, "here is an aba", "              ", 15)]
    [InlineData(8, 2, "here is an aba", "cus           ", 4)]
    [InlineData(9, 2, "here is an aba", "              ", 15)]
    public void WrappingAndBufferingShapeLinesAsTheStandardsExampleShows(int bits, int operation, string first, string second, int cursorX)
    {
        // [zm 8.8.3.1.2.2] The example: "Here is an abacus" in a window
        // fourteen units wide, with wrapping and buffering on or off.
        var code = new Assembler().Ext(17, Small(0), Small(24), Small(14));
        if (bits != 0)
        {
            code = code.Ext(18, Small(0), Small(bits), Small(operation));
        }

        var run = RunVersion6(code.Short0(Op.Print).Text("here is an abacus").Quit());
        var windows = run.Interpreter.Windows!;

        Assert.Equal(first, windows.RowText(0)[..14]);
        Assert.Equal(second, windows.RowText(1)[..14]);
        Assert.Equal(cursorX, windows.Windows[0].CursorX);
    }

    [Fact]
    public void EraseLineClearsToTheMarginOrByUnits()
    {
        var toMargin = RunVersion6(new Assembler()
            .Short0(Op.Print).Text("abcdef")
            .Variable(Op.SetCursor, true, Small(1), Small(3))
            .Variable(Op.EraseLine, true, Small(1))
            .Quit()).Interpreter.Windows!;

        var byUnits = RunVersion6(new Assembler()
            .Short0(Op.Print).Text("abcdef")
            .Variable(Op.SetCursor, true, Small(1), Small(2))
            .Variable(Op.EraseLine, true, Small(3))
            .Quit()).Interpreter.Windows!;

        // [zm op:erase_line] 1 erases to the right margin; another value
        // erases that many units less one, and the cursor stays put.
        Assert.Equal("ab    ", toMargin.RowText(0)[..6]);
        Assert.Equal("a  def", byUnits.RowText(0)[..6]);
        Assert.Equal((1, 2), (byUnits.Windows[0].CursorY, byUnits.Windows[0].CursorX));
    }

    [Fact]
    public void MarginsKeepTextInsideThem()
    {
        var run = RunVersion6(new Assembler()
            .Ext(8, Small(2), Small(2))
            .Short0(Op.Print).Text("x")
            .Short0(Op.NewLine)
            .Quit());

        var windows = run.Interpreter.Windows!;

        // [zm op:set_margins] A cursor outside the margins goes to the
        // left margin, and [zm 8.8.3.2.1] a newline returns to it.
        Assert.Equal('x', windows.RowText(0)[2]);
        Assert.Equal((2, 3), (windows.Windows[0].CursorY, windows.Windows[0].CursorX));
        Assert.Equal((2, 2), (windows.Windows[0].LeftMargin, windows.Windows[0].RightMargin));
    }

    [Fact]
    public void WindowStyleSetsClearsAndFlipsAttributes()
    {
        var run = RunVersion6(new Assembler()
            .Ext(18, Small(2), Small(3), Small(0))
            .Ext(19, Small(2), Small(14)).Store(G0)
            .Ext(18, Small(2), Small(8), Small(1))
            .Ext(19, Small(2), Small(14)).Store(G1)
            .Ext(18, Small(2), Small(1), Small(2))
            .Ext(18, Small(2), Small(15), Small(3))
            .Ext(19, Small(2), Small(14)).Store(G2)
            .Quit());

        // [zm op:window_style] Set outright, then set bits, clear bits,
        // and flip bits.
        Assert.Equal(3, run.Global(G0));
        Assert.Equal(11, run.Global(G1));
        Assert.Equal(5, run.Global(G2));
    }

    [Fact]
    public void WindowPropertiesReadAndWrite()
    {
        var run = RunVersion6(new Assembler()
            .Ext(17, Small(3), Small(6), Small(40))
            .Ext(16, Small(3), Small(5), Small(7))
            .Ext(19, Small(3), Small(0)).Store(G0)
            .Ext(19, Small(3), Small(3)).Store(G1)
            .Ext(25, Small(3), Small(15), Large(0xFC19))
            .Ext(19, Small(3), Small(15)).Store(G2)
            .Ext(25, Small(3), Small(16), Small(1))
            .Quit());

        // [zm 8.8.3.2] Properties 0 and 3 after a move and a resize,
        // property 15 written and read back as -999, and the true
        // colors refused.
        Assert.Equal(5, run.Global(G0));
        Assert.Equal(40, run.Global(G1));
        Assert.Equal(0xFC19, run.Global(G2));
        Assert.Contains("cannot be written", run.Interpreter.RuntimeErrors.Single());
    }

    [Fact]
    public void ScrollWindowMovesTheCellsEitherWay()
    {
        var up = RunVersion6(new Assembler()
            .Short0(Op.Print).Text("one").Short0(Op.NewLine)
            .Short0(Op.Print).Text("two").Short0(Op.NewLine)
            .Short0(Op.Print).Text("three")
            .Ext(20, Small(0), Small(1))
            .Quit()).Interpreter.Windows!;

        var down = RunVersion6(new Assembler()
            .Short0(Op.Print).Text("one").Short0(Op.NewLine)
            .Short0(Op.Print).Text("two")
            .Ext(20, Small(0), Large(0xFFFF))
            .Quit()).Interpreter.Windows!;

        // [zm op:scroll_window] Positive scrolls up, negative down,
        // filling with blanks.
        Assert.Equal(["two  ", "three", "     "], [up.RowText(0)[..5], up.RowText(1)[..5], up.RowText(2)[..5]]);
        Assert.Equal(["   ", "one", "two"], [down.RowText(0)[..3], down.RowText(1)[..3], down.RowText(2)[..3]]);
    }

    [Fact]
    public void ColorsArePerWindowAndTransparentKeepsTheBackground()
    {
        var run = RunVersion6(new Assembler()
            .Variable(Op.SetColour, false, Small(3), Small(4), Small(2))
            .Variable(Op.SetColour, false, Small(15), Small(2))
            .Variable(Op.SetColour, false, Small(2), Small(15))
            .Short0(Op.Print).Text("t")
            .Ext(19, Small(0), Small(17)).Store(G0)
            .Quit());

        var windows = run.Interpreter.Windows!;

        // [zm op:set_colour] The window operand; [zm 8.3.6] transparent
        // is refused as a foreground and, as a background, leaves what
        // is under the text alone; [zm 8.8.3.2.8] property 17 says -4.
        Assert.Equal((ScreenColor.Red, ScreenColor.Green), (windows.Windows[2].Foreground, windows.Windows[2].Background));
        Assert.Equal((ScreenColor.Black, ScreenColor.Transparent), (windows.Windows[0].Foreground, windows.Windows[0].Background));
        Assert.Equal((ScreenColor.Black, ScreenColor.Blue), (windows[0, 0].Attributes.Foreground, windows[0, 0].Attributes.Background));
        Assert.Equal(0xFFFC, run.Global(G0));
        Assert.Contains("colors 15 and 2", run.Interpreter.RuntimeErrors.Single());
    }

    [Fact]
    public void PicturesComeFromTheResourceFile()
    {
        var resolution = TestBlorb.Resolution(80, 24, (1, 2, 1, 0, 0, 0, 0));
        var blorb = BlorbFile.Read(TestBlorb.Build([(1, "PNG ", TestBlorb.Png(12, 3)), (2, "Rect", TestBlorb.Rect(4, 4))], resolution, release: 7));
        var screen = new RecordingScreen(80, 24, GridWithPictures);

        var run = RunVersion6(
            NotTakenInto(new Assembler().Ext(6, Small(0), Large(Table)), G0)
            .Ext(6, Small(1), Large(Table + 4)).Branch(true, SkipStore).Long(Op.Store, Small(G1), Small(1))
            .Ext(5, Small(1), Small(2), Small(3))
            .Ext(5, Small(2), Small(1), Small(1))
            .Ext(6, Small(9), Large(Table + 8)).Branch(true, SkipStore).Long(Op.Store, Small(G2), Small(1))
            .Quit(),
            screen,
            before: interpreter => interpreter.UseResources(blorb));

        var memory = run.Interpreter.Memory;
        var windows = run.Interpreter.Windows!;

        // [zm op:picture_data] Picture 0 reports the count and release;
        // a picture reports its scaled height and width; an unknown one
        // does not branch. [zm op:draw_picture] A drawn picture is noted
        // where it went; [blorb 2.3] a rectangle may not be drawn.
        Assert.Equal((2, 7), (memory.ReadWord(Table), memory.ReadWord(Table + 2)));
        Assert.Equal(0, run.Global(G0));
        Assert.Equal((6, 24), (memory.ReadWord(Table + 4), memory.ReadWord(Table + 6)));
        Assert.Equal(0, run.Global(G1));
        Assert.Equal(1, run.Global(G2));
        Assert.Equal(new PicturePlacement(1, 1, 2, 6, 24), windows.Pictures.Single());
        Assert.Contains("no picture 2 to draw", run.Interpreter.RuntimeErrors.Single());
    }

    [Fact]
    public void WithoutPicturesTheGameIsToldSoAndDrawingIsIgnored()
    {
        var run = RunVersion6(
            NotTakenInto(new Assembler().Ext(6, Small(0), Large(Table)), G0)
            .Ext(5, Small(1))
            .Quit(),
            setup: story => story.PutWord(0x10, 0x0008));

        // [zm 8.8.6] and [zm 11.1.2] No pictures: the header bits say so,
        // picture_data finds none, and a draw is quietly nothing.
        var header = run.Interpreter.Header;
        Assert.Equal((0, 0), (run.Interpreter.Memory.ReadWord(Table), run.Interpreter.Memory.ReadWord(Table + 2)));
        Assert.Equal(1, run.Global(G0));
        Assert.Empty(run.Interpreter.RuntimeErrors);
        Assert.False(header.Flags2.HasFlag(Flags2.WantsPictures));
        Assert.False(header.Flags1FromVersion4.HasFlag(Flags1FromVersion4.PicturesAvailable));
    }

    [Fact]
    public void NewlineInterruptRunsWhenTheCountdownReachesZero()
    {
        var run = RunVersion6(
            new Assembler()
                .Ext(25, Small(0), Small(9), Small(2))
                .Ext(25, Small(0), Small(8), Large(Interrupt / 4))
                .Short0(Op.Print).Text("a").Short0(Op.NewLine)
                .Long(Op.Store, Small(G2), Var(G1))
                .Short0(Op.Print).Text("b").Short0(Op.NewLine)
                .Ext(19, Small(0), Small(9)).Store(G0)
                .Quit(),
            setup: story => story.Routine(Interrupt, 0, new Assembler().Long(Op.Store, Small(G1), Small(7)).Short0(Op.Rtrue).ToArray()));

        // [zm 8.8.3.2.2] Not after the first newline, but after the
        // second, when the countdown hits zero.
        Assert.Equal(0, run.Global(G2));
        Assert.Equal(7, run.Global(G1));
        Assert.Equal(0, run.Global(G0));
    }

    [Fact]
    public void Stream3ReportsTheWidthOfItsTextInTheHeader()
    {
        var run = RunVersion6(
            new Assembler()
                .Variable(Op.OutputStream, true, Small(3), Large(Table))
                .Short0(Op.Print).Text("abc")
                .Variable(Op.OutputStream, true, Large(0xFFFD))
                .Quit(),
            new RecordingScreen { FontWidth = 4 });

        // [zm 11.1] The word at $30: the width in units of what went to
        // stream 3, which is how a game measures text to center it.
        Assert.Equal(3, run.Interpreter.Memory.ReadWord(Table));
        Assert.Equal(12, run.Interpreter.Header.OutputStream3Width);
    }

    [Fact]
    public void TheHeaderDescribesTheVersion6Screen()
    {
        var text = RunVersion6(
            new Assembler().Ext(19, Small(0), Small(13)).Store(G0).Quit(),
            new RecordingScreen { FontWidth = 4, FontHeight = 1 },
            setup: story => story.PutWord(0x10, 0x0008));
        var pictures = RunVersion6(new Assembler().Quit(), new RecordingScreen(80, 24, GridWithPictures), setup: story => story.PutWord(0x10, 0x0008));

        // [zm 8.4.3] and [zm 8.1.1] Units and the font size as the
        // frontend reports them, [zm 8.8.3.2.5] the font size property
        // likewise, and [zm 11.1.3] the interpreter number: a DEC-20
        // without pictures, an IBM PC with them.
        var header = text.Interpreter.Header;
        Assert.Equal((320, 24), (header.ScreenWidthUnits, header.ScreenHeightUnits));
        Assert.Equal((4, 1), (header.FontWidthUnits, header.FontHeightUnits));
        Assert.Equal(0x0104, text.Global(G0));
        Assert.Equal(InterpreterNumber.DecSystem20, header.InterpreterNumber);
        Assert.False(header.Flags2.HasFlag(Flags2.WantsPictures));

        var withPictures = pictures.Interpreter.Header;
        Assert.Equal(InterpreterNumber.IbmPc, withPictures.InterpreterNumber);
        Assert.True(withPictures.Flags1FromVersion4.HasFlag(Flags1FromVersion4.PicturesAvailable));
        Assert.True(withPictures.Flags2.HasFlag(Flags2.WantsPictures));
    }

    [Fact]
    public void MenusMouseAndScreenBufferingAreDeclinedHonestly()
    {
        var run = RunVersion6(
            NotTakenInto(new Assembler().Ext(27, Small(3), Large(Table)), G0)
            .Ext(22, Large(Table))
            .Ext(29, Small(1)).Store(G1)
            .Ext(23, Small(1))
            .Ext(28, Large(Table))
            .Quit(),
            setup: story =>
            {
                story.PutWord(0x10, 0x0120);
                story.PutWord(Table, 0x1234);
                story.PutWord(Table + 6, 0x5678);
            });

        // [zm 10.4.1.1] No menu is made; [zm op:read_mouse] the four
        // words are zero; [zm 8.8.7] buffer_screen is always 0; the
        // mouse window and picture table are accepted and ignored.
        var memory = run.Interpreter.Memory;
        Assert.Equal(1, run.Global(G0));
        Assert.Equal((0, 0, 0, 0), (memory.ReadWord(Table), memory.ReadWord(Table + 2), memory.ReadWord(Table + 4), memory.ReadWord(Table + 6)));
        Assert.Equal(0, run.Global(G1));
        Assert.Empty(run.Interpreter.RuntimeErrors);
        Assert.False(run.Interpreter.Header.Flags2.HasFlag(Flags2.WantsMenus));
        Assert.False(run.Interpreter.Header.Flags2.HasFlag(Flags2.WantsMouse));
    }

    [Fact]
    public void PrintFormPrintsTheLinesOfAFormattedTable()
    {
        var run = RunVersion6(
            new Assembler().Ext(26, Large(Table)).Quit(),
            new TextWriterScreen(new StringWriter()),
            setup: story =>
            {
                story.PutWord(Table, 2);
                story.Bytes[Table + 2] = (byte)'a';
                story.Bytes[Table + 3] = (byte)'b';
                story.PutWord(Table + 4, 1);
                story.Bytes[Table + 6] = (byte)'c';
                story.PutWord(Table + 7, 0);
            });

        // [zm op:print_form] Each line a count and that many characters,
        // ended by a zero word.
        var windows = run.Interpreter.Windows!;
        Assert.Equal(("ab", "c "), (windows.RowText(0)[..2], windows.RowText(1)[..2]));
        Assert.Equal((3, 1), (windows.Windows[0].CursorY, windows.Windows[0].CursorX));
    }

    [Fact]
    public void TypedInputIsRecordedInTheCells()
    {
        var run = RunVersion6(
            new Assembler().Variable(Op.Aread, true, Large(TextBuffer), Large(ParseBuffer)).Store(G0).Quit(),
            setup: story =>
            {
                story.Bytes[TextBuffer] = 20;
                story.Bytes[ParseBuffer] = 5;
            },
            input: new ScriptedInput("go"));

        // The frontend showed the typing; the model's cells and cursor
        // agree with it afterward, return key included.
        var windows = run.Interpreter.Windows!;
        Assert.Equal("go", windows.RowText(0)[..2]);
        Assert.Equal((2, 1), (windows.Windows[0].CursorY, windows.Windows[0].CursorX));
    }

    [Fact]
    public void TheMorePromptComesWhenAWindowScrollsUnreadLines()
    {
        var screen = new RecordingScreen(80, 4);
        var code = new Assembler();
        foreach (var line in "abcdef")
        {
            code = code.Short0(Op.Print).Text(line.ToString()).Short0(Op.NewLine);
        }

        var run = RunVersion6(code.Quit(), screen);

        // [zm 8.8.3.2.6] Three lines printed into a four-line window and
        // then a scroll: pause once, and the count starts over.
        Assert.Equal(1, screen.MorePrompts);
        Assert.Equal("def", string.Concat(run.Interpreter.Windows![0, 0].Character, run.Interpreter.Windows[1, 0].Character, run.Interpreter.Windows[2, 0].Character));
    }

    [Fact]
    public void SetCursorHidesAndShowsTheCursor()
    {
        var hidden = RunVersion6(new Assembler().Variable(Op.SetCursor, true, Large(0xFFFF)).Quit()).Interpreter.Windows!;
        var shown = RunVersion6(new Assembler().Variable(Op.SetCursor, true, Large(0xFFFF)).Variable(Op.SetCursor, true, Large(0xFFFE), Small(0)).Quit()).Interpreter.Windows!;

        // [zm op:set_cursor] -1 off, -2 on.
        Assert.False(hidden.CursorVisible);
        Assert.True(shown.CursorVisible);
    }

    [Fact]
    public void AStreamFrontendGetsALineBreakWhereTheCursorMovesToAnotherRow()
    {
        var writer = new StringWriter();
        RunVersion6(
            new Assembler()
                .Short0(Op.Print).Text("ab")
                .Variable(Op.SetCursor, true, Small(2), Small(1))
                .Short0(Op.Print).Text("cd")
                .Quit(),
            new TextWriterScreen(writer));

        // A frontend without a grid sees the text in order, with a break
        // wherever the game moved to another row.
        Assert.Equal("ab\ncd", writer.ToString());
    }
}

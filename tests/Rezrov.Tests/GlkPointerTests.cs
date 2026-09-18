using Rezrov.Glulx;
using Rezrov.Glulx.Glk;
using Rezrov.Gui;
using Rezrov.Glulx.Instructions;
using static Rezrov.Tests.GlulxAssembler;
using FileMode = Rezrov.Glulx.Glk.FileMode;

namespace Rezrov.Tests;

/// <summary>
/// [glk #mouse_events] and [glk #links] What the player can point at:
/// the link values text carries, the two kinds of request, the events
/// a click becomes, and what the gestalt answers say a display can do.
/// </summary>
public class GlkPointerTests
{
    private const uint RequestMouseEvent = 0x00D4;
    private const uint CancelMouseEvent = 0x00D5;
    private const uint SetHyperlink = 0x0100;
    private const uint SetHyperlinkStream = 0x0101;
    private const uint RequestHyperlinkEvent = 0x0102;
    private const uint CancelHyperlinkEvent = 0x0103;
    private const uint WindowOpen = 0x0023;
    private const uint SetWindow = 0x002F;
    private const uint PutChar = 0x0080;
    private const uint Select = 0x00C0;

    private static (GlkLibrary Glk, RecordingGlkDisplay Display) Library(bool pointer = true)
    {
        var display = new RecordingGlkDisplay(20, 6) { Pointer = pointer };
        return (new GlkLibrary(display), display);
    }

    private static void Print(GlkLibrary glk, GlkWindow window, string text)
    {
        foreach (var character in text)
        {
            glk.PutChar(window.Stream, character);
        }
    }

    [Fact]
    public void TextCarriesTheLinkValueOfItsStreamUntilItIsChanged()
    {
        var (glk, display) = Library();
        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;

        Print(glk, story, "go ");
        GlkLibrary.SetHyperlink(story.Stream, 7);
        Print(glk, story, "north");
        GlkLibrary.SetHyperlink(story.Stream, 0);
        Print(glk, story, "!");

        // [glk #link_creating] The link runs from where it was set until
        // it is set again, and zero is no link at all.
        Assert.Equal("go north!", display.Output);
        Assert.Equal([0, 0, 0, 7, 7, 7, 7, 7, 0], display.Links);
        Assert.Equal(0u, story.Stream.Link);
    }

    [Fact]
    public void ATextGridRemembersTheLinkOfEveryCell()
    {
        var (glk, _) = Library();
        var status = (TextGridWindow)glk.OpenWindow(null, 0, 0, WindowType.TextGrid, 1)!;

        GlkLibrary.SetHyperlink(status.Stream, 12);
        Print(glk, status, "map");
        GlkLibrary.SetHyperlink(status.Stream, 0);
        Print(glk, status, "!");

        Assert.Equal(12u, status.LinkAt(0, 0));
        Assert.Equal(12u, status.LinkAt(2, 0));
        Assert.Equal(0u, status.LinkAt(3, 0));

        // [glk op:window_clear] Clearing takes the links with the text.
        status.Clear();
        Assert.Equal(0u, status.LinkAt(0, 0));
    }

    [Fact]
    public void AnEchoStreamKeepsTheLinksOfWhatItCopies()
    {
        var (glk, _) = Library();
        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;
        var memory = new GlulxMemory(TestGlulx.File(ramStart: 0x400, extStart: 0x800, endMem: 0xA00));
        var transcript = glk.OpenMemoryStream(memory, 0x400, 32, false, FileMode.Write, 0)!;
        story.EchoStream = transcript;

        // [glk #echo_streams] The link command is replicated, as the
        // style command is.
        GlkLibrary.SetHyperlink(story.Stream, 5);
        Print(glk, story, "x");

        Assert.Equal(5u, transcript.Link);
    }

    [Fact]
    public void AClickBecomesAnEventOnlyWhereOneWasAskedFor()
    {
        var (glk, display) = Library();
        var status = glk.OpenWindow(null, 0, 0, WindowType.TextGrid, 1)!;

        // [glk #mouse_events] Nothing was requested, so the click is
        // dropped and the wait goes on to the next thing that arrives.
        display.Inputs.Enqueue(GlkInput.MouseClick(status, 3, 1));
        display.Inputs.Enqueue(GlkInput.Arrange);
        Assert.Equal(EventType.Arrange, glk.Select().Type);

        glk.RequestMouseEvent(status);
        Assert.True(status.MouseRequest);
        display.Inputs.Enqueue(GlkInput.MouseClick(status, 3, 1));

        // [glk #mouse_events] The column and the row of the character,
        // the top left being zero and zero.
        Assert.Equal(new GlkEvent(EventType.MouseInput, status, 3, 1), glk.Select());

        // The request is finished, so the next click is dropped again.
        Assert.False(status.MouseRequest);
        display.Inputs.Enqueue(GlkInput.MouseClick(status, 1, 1));
        display.Inputs.Enqueue(GlkInput.Arrange);
        Assert.Equal(EventType.Arrange, glk.Select().Type);
    }

    [Fact]
    public void OnlyAGridOrGraphicsWindowTakesMouseInput()
    {
        var (glk, _) = Library();
        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;

        // [glk #mouse_events] "You can request mouse input only in text
        // grid windows and graphics windows."
        glk.RequestMouseEvent(story);

        Assert.False(story.MouseRequest);
        Assert.Contains("request_mouse_event: a window of type TextBuffer takes no mouse input.", glk.Warnings);
    }

    [Fact]
    public void ALinkBecomesAnEventOnlyWhereOneWasAskedFor()
    {
        var (glk, display) = Library();
        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;

        display.Inputs.Enqueue(GlkInput.LinkSelected(story, 9));
        display.Inputs.Enqueue(GlkInput.Arrange);
        Assert.Equal(EventType.Arrange, glk.Select().Type);

        GlkLibrary.RequestHyperlinkEvent(story);
        display.Inputs.Enqueue(GlkInput.LinkSelected(story, 9));

        // [glk #link_events] The window it came from and the link value,
        // which is never zero.
        Assert.Equal(new GlkEvent(EventType.Hyperlink, story, 9, 0), glk.Select());
        Assert.False(story.HyperlinkRequest);

        // A cancelled request hears nothing more.
        GlkLibrary.RequestHyperlinkEvent(story);
        GlkLibrary.CancelHyperlinkEvent(story);
        display.Inputs.Enqueue(GlkInput.LinkSelected(story, 9));
        display.Inputs.Enqueue(GlkInput.Arrange);
        Assert.Equal(EventType.Arrange, glk.Select().Type);
    }

    [Fact]
    public void TheGestaltAnswersFollowWhatTheDisplayCanDo()
    {
        var (with, _) = Library();
        var (without, _) = Library(pointer: false);

        // [glk #link_testing] The four functions are always here, and
        // whether a click can be reported is the display's to say.
        Assert.Equal(1u, with.Gestalt((uint)GestaltSelector.Hyperlinks, 0, null));
        Assert.Equal(1u, without.Gestalt((uint)GestaltSelector.Hyperlinks, 0, null));

        Assert.Equal(1u, with.Gestalt((uint)GestaltSelector.HyperlinkInput, (uint)WindowType.TextBuffer, null));
        Assert.Equal(1u, with.Gestalt((uint)GestaltSelector.MouseInput, (uint)WindowType.TextGrid, null));
        Assert.Equal(0u, with.Gestalt((uint)GestaltSelector.MouseInput, (uint)WindowType.TextBuffer, null));
        Assert.Equal(0u, with.Gestalt((uint)GestaltSelector.MouseInput, (uint)WindowType.Graphics, null));

        Assert.Equal(0u, without.Gestalt((uint)GestaltSelector.HyperlinkInput, (uint)WindowType.TextBuffer, null));
        Assert.Equal(0u, without.Gestalt((uint)GestaltSelector.MouseInput, (uint)WindowType.TextGrid, null));
    }

    [Fact]
    public void TheWindowReportsTouchesAndLinksWhereTheSpecificationAllowsThem()
    {
        var glk = new GlkLibrary(new GuiGlkDisplay(new Cells(), () => { }, 800, 600));

        // [glk #mouse_events] A window may be touched only where there
        // is something in it to touch: a grid of characters or a canvas
        // of pixels. A buffer of flowing text is neither.
        Assert.Equal(1u, glk.Gestalt((uint)GestaltSelector.MouseInput, (uint)WindowType.TextGrid, null));
        Assert.Equal(1u, glk.Gestalt((uint)GestaltSelector.MouseInput, (uint)WindowType.Graphics, null));
        Assert.Equal(0u, glk.Gestalt((uint)GestaltSelector.MouseInput, (uint)WindowType.TextBuffer, null));

        // [glk #link_testing] A link can be selected wherever text can
        // be printed, which is the other way round.
        Assert.Equal(1u, glk.Gestalt((uint)GestaltSelector.Hyperlinks, 0, null));
        Assert.Equal(1u, glk.Gestalt((uint)GestaltSelector.HyperlinkInput, (uint)WindowType.TextBuffer, null));
        Assert.Equal(1u, glk.Gestalt((uint)GestaltSelector.HyperlinkInput, (uint)WindowType.TextGrid, null));
        Assert.Equal(0u, glk.Gestalt((uint)GestaltSelector.HyperlinkInput, (uint)WindowType.Graphics, null));
    }

    /// <summary>
    /// A font of whole cells, which is all the display needs to work
    /// out how many of them the window holds.
    /// </summary>
    private sealed class Cells : IGlyphs
    {
        public double CellWidth => 10;

        public double CellHeight => 20;

        public double Width(string text, GlkStyle style) => (text?.Length ?? 0) * 10;

        public double LineHeight(GlkStyle style) => 20;
    }

    [Fact]
    public void AScriptCanPointAtAConsoleWhereAPlayerCannot()
    {
        // [glk #mouse_events] A console has no pointer, so a display
        // over one takes a click as a line of its own kind, which is
        // how an acceptance script exercises a game's links.
        var writer = new StringWriter();
        var reader = new StringReader("[click 3,1]\n[link 5]\n[click 9,9]\n");
        var display = new TextWriterGlkDisplay(writer, reader, hasPointer: true);
        var glk = new GlkLibrary(display);
        var memory = new GlulxMemory(TestGlulx.File(ramStart: 0x400, extStart: 0x800, endMem: 0xA00));
        var story = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;
        var status = glk.OpenWindow(story, WindowMethod.Above | WindowMethod.Fixed, 2, WindowType.TextGrid, 2)!;

        glk.RequestMouseEvent(status);
        Assert.Equal(new GlkEvent(EventType.MouseInput, status, 3, 1), glk.Select());

        GlkLibrary.RequestHyperlinkEvent(story);
        Assert.Equal(new GlkEvent(EventType.Hyperlink, story, 5, 0), glk.Select());

        // With nothing waiting to be touched, the same shape of line is
        // only a line, so a click that lands nowhere is plain in the
        // recording rather than quietly doing nothing.
        glk.RequestLineEvent(story, memory, 0x400, 20, 0, false);
        var typed = glk.Select();
        Assert.Equal(EventType.LineInput, typed.Type);
        Assert.Equal("[click 9,9]", System.Text.Encoding.Latin1.GetString(memory.Slice(0x400, typed.Value1)));

        // And a console that was not told it has a pointer says so.
        var plain = new TextWriterGlkDisplay(writer);
        Assert.False(plain.CanReportMouse(WindowType.TextGrid));
        Assert.False(plain.CanReportHyperlinks(WindowType.TextBuffer));
        Assert.True(display.CanReportMouse(WindowType.TextGrid));
        Assert.True(display.CanReportHyperlinks(WindowType.TextBuffer));
    }

    [Fact]
    public void TheFunctionsWorkThroughTheDispatchLayer()
    {
        var display = new RecordingGlkDisplay(20, 6) { Pointer = true };
        var glk = new GlkLibrary(display);

        // A grid window, a link value set on the current stream and then
        // on the window's stream by name, both requests made and the
        // mouse one cancelled, and a link click waiting to be selected.
        var code = new GlulxAssembler().Function("main").Op(Opcode.SetIOSys, C(2), C(0));
        Glk(code, WindowOpen, Ram(0), C(0), C(0), C(0), C((uint)WindowType.TextGrid), C(0));
        Glk(code, SetWindow, Discard, Ram(0));
        Glk(code, SetHyperlink, Discard, C(3));
        Glk(code, PutChar, Discard, C('a'));
        Glk(code, RequestMouseEvent, Discard, Ram(0));
        Glk(code, CancelMouseEvent, Discard, Ram(0));
        Glk(code, RequestHyperlinkEvent, Discard, Ram(0));
        Glk(code, Select, Discard, Ref(8));
        Glk(code, CancelHyperlinkEvent, Discard, Ram(0));
        Glk(code, SetHyperlinkStream, Discard, Ram(0), C(0));

        var machine = GlulxRun.Machine(code.Return(C(0)), glk: glk);

        // The window does not exist until the program makes it, so the
        // click waits with no window named and the display binds it.
        display.Inputs.Enqueue(new GlkInput(GlkInputKind.Hyperlink, null, null, 0, 0, Link: 3));
        machine.Run();
        var window = (TextGridWindow)glk.Windows.Find(1)!;

        // The event structure: a hyperlink event from the window, with
        // the link value the game set.
        Assert.Equal((uint)EventType.Hyperlink, machine.Ram(8));
        Assert.Equal(window.Id, machine.Ram(12));
        Assert.Equal(3u, machine.Ram(16));
        Assert.Equal(3u, window.LinkAt(0, 0));
        Assert.False(window.MouseRequest);
        Assert.False(window.HyperlinkRequest);
        Assert.Empty(glk.Warnings);
    }

    /// <summary>
    /// [glulx op:glk] Pushes the arguments last first and calls the
    /// function, storing its result.
    /// </summary>
    private static GlulxAssembler Glk(GlulxAssembler code, uint selector, Arg result, params Arg[] args)
    {
        for (var i = args.Length - 1; i >= 0; i--)
        {
            code.Op(Opcode.Copy, args[i], Sp);
        }

        return code.Op(Opcode.Glk, C(selector), C(args.Length), result);
    }

    private static Arg Ref(uint offset) => C(GlulxRun.RamStart + offset);
}

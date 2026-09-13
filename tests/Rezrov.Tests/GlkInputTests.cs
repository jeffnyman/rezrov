using Rezrov.Glulx;
using Rezrov.Glulx.Execution;
using Rezrov.Glulx.Glk;
using FileMode = Rezrov.Glulx.Glk.FileMode;
using Rezrov.Glulx.Instructions;
using static Rezrov.Tests.GlulxAssembler;

namespace Rezrov.Tests;

/// <summary>
/// [glk #event] Input and events: line and character requests, select
/// and its events, cancelling, timers, echo, and terminators.
/// </summary>
public class GlkInputTests
{
    private const uint Buffer = 0x400;
    private const uint Select = 0x00C0;
    private const uint SelectPoll = 0x00C1;
    private const uint RequestLineEvent = 0x00D0;
    private const uint CancelLineEvent = 0x00D1;
    private const uint RequestCharEvent = 0x00D2;
    private const uint RequestTimerEvents = 0x00D6;
    private const uint WindowOpen = 0x0023;
    private const uint SetWindow = 0x002F;

    private static (GlkLibrary Glk, RecordingGlkDisplay Display, GlulxMemory Memory, GlkWindow Window) Library(WindowType type = WindowType.TextBuffer, ManualClock? clock = null)
    {
        var display = new RecordingGlkDisplay(20, 5);
        var glk = new GlkLibrary(display, clock: clock);
        var memory = new GlulxMemory(TestGlulx.File(ramStart: 0x400, extStart: 0x800, endMem: 0xA00));
        var window = glk.OpenWindow(null, 0, 0, type, 1)!;
        return (glk, display, memory, window);
    }

    [Fact]
    public void AChangeOfSizeIsAnArrangementEvent()
    {
        var (glk, display, _, window) = Library();
        display.Width = 30;
        display.Height = 8;
        display.Inputs.Enqueue(GlkInput.Arrange);
        glk.RequestCharEvent(window, false);

        var result = glk.Select();

        // [glk #arrange_events] The windows are laid out again for the
        // new size, and the event names no window, since all changed.
        Assert.Equal(EventType.Arrange, result.Type);
        Assert.Null(result.Window);
        Assert.Equal((0u, 0u), (result.Value1, result.Value2));
        Assert.Equal((30, 8), (window.Width, window.Height));
        Assert.Equal(2, display.ArrangedCount);
    }

    [Fact]
    public void ALineGoesIntoTheBufferAndComesBackAsAnEvent()
    {
        var (glk, display, memory, window) = Library();
        display.Inputs.Enqueue(GlkInput.Line(window, "look"));

        glk.RequestLineEvent(window, memory, Buffer, 10, 0, false);
        var result = glk.Select();

        // [glk #line_events] The characters in the buffer with no
        // terminator, the count in val1, no terminator key in val2, and
        // the request over.
        Assert.Equal(new GlkEvent(EventType.LineInput, window, 4, 0), result);
        Assert.Equal("look", System.Text.Encoding.Latin1.GetString(memory.Slice(Buffer, 4)));
        Assert.Null(window.LineRequest);

        // The display showed the line, and the echo was not doubled.
        Assert.Equal("look\n", display.Text(window));
        Assert.Equal([""], display.InitialTexts);
    }

    [Fact]
    public void ALineIsCutToTheBufferAndUnicodeBecomesQuestionMarks()
    {
        var (glk, display, memory, window) = Library();
        display.Inputs.Enqueue(GlkInput.Line(window, "αbcdefgh"));
        display.Inputs.Enqueue(GlkInput.Line(window, "αβ"));

        glk.RequestLineEvent(window, memory, Buffer, 4, 0, false);
        var latin = glk.Select();
        glk.RequestLineEvent(window, memory, Buffer + 0x10, 4, 0, true);
        var unicode = glk.Select();

        // [glk op:request_line_event] maxlen is the buffer's length;
        // [glk #encoding_inline] a Latin-1 buffer gets a question mark
        // for what it cannot hold, a Unicode buffer the code point.
        Assert.Equal(4u, latin.Value1);
        Assert.Equal("?bcd", System.Text.Encoding.Latin1.GetString(memory.Slice(Buffer, 4)));
        Assert.Equal(2u, unicode.Value1);
        Assert.Equal((0x3B1u, 0x3B2u), (memory.ReadWord(Buffer + 0x10), memory.ReadWord(Buffer + 0x14)));
    }

    [Fact]
    public void InitialTextReachesTheDisplay()
    {
        var (glk, display, memory, window) = Library();
        memory.WriteByte(Buffer, (byte)'g');
        memory.WriteByte(Buffer + 1, (byte)'o');
        display.Inputs.Enqueue(GlkInput.Line(window, "go north"));

        // [glk op:request_line_event] The first initlen bytes are entered
        // as if the player had typed them.
        glk.RequestLineEvent(window, memory, Buffer, 20, 2, false);
        glk.Select();

        Assert.Equal(["go"], display.InitialTexts);
    }

    [Fact]
    public void AKeyComesBackAsACharacterEvent()
    {
        var (glk, display, _, window) = Library();
        display.Inputs.Enqueue(GlkInput.KeyPress(window, 'y'));
        display.Inputs.Enqueue(GlkInput.KeyPress(window, 0x3B1));
        display.Inputs.Enqueue(GlkInput.KeyPress(window, GlkKeyCode.Return));
        display.Inputs.Enqueue(GlkInput.KeyPress(window, 0x3B1));

        glk.RequestCharEvent(window, false);
        var plain = glk.Select();
        glk.RequestCharEvent(window, false);
        var wide = glk.Select();
        glk.RequestCharEvent(window, false);
        var special = glk.Select();
        glk.RequestCharEvent(window, true);
        var unicode = glk.Select();

        // [glk #char_events] Latin-1 requests give 0 to 255 or a special
        // key, with anything wider a question mark; Unicode requests
        // give the code point.
        Assert.Equal(new GlkEvent(EventType.CharInput, window, 'y', 0), plain);
        Assert.Equal('?', wide.Value1);
        Assert.Equal(GlkKeyCode.Return, special.Value1);
        Assert.Equal(0x3B1u, unicode.Value1);
        Assert.Equal(CharRequest.None, window.CharRequest);
    }

    [Fact]
    public void AWindowTakesOneRequestAtATime()
    {
        var (glk, _, memory, window) = Library();

        glk.RequestCharEvent(window, false);
        glk.RequestLineEvent(window, memory, Buffer, 10, 0, false);
        glk.RequestCharEvent(window, false);

        // [glk #char_events] Illegal while another request is pending.
        Assert.Null(window.LineRequest);
        Assert.Equal(CharRequest.Latin1, window.CharRequest);
        Assert.Equal(2, glk.Warnings.Count);

        var (other, _, _, blank) = Library(WindowType.Blank);
        other.RequestLineEvent(blank, memory, Buffer, 10, 0, false);
        Assert.Contains(other.Warnings, w => w.Contains("does not take line input", StringComparison.Ordinal));
    }

    [Fact]
    public void CancellingALineRequestGivesAnEmptyLine()
    {
        var (glk, _, memory, window) = Library();

        glk.RequestLineEvent(window, memory, Buffer, 10, 0, false);
        var cancelled = GlkLibrary.CancelLineEvent(window);
        var nothing = GlkLibrary.CancelLineEvent(window);

        // [glk op:cancel_line_event] As if the player had hit enter with
        // nothing typed; and no event when nothing was pending.
        Assert.Equal(new GlkEvent(EventType.LineInput, window, 0, 0), cancelled);
        Assert.Equal(GlkEvent.None, nothing);
        Assert.Null(window.LineRequest);
    }

    [Fact]
    public void TheEndOfInputIsAnException()
    {
        var (glk, _, memory, window) = Library();
        glk.RequestLineEvent(window, memory, Buffer, 10, 0, false);

        Assert.Throws<EndOfStreamException>(() => glk.Select());
    }

    [Fact]
    public void LineInputInAGridStaysThereAndMovesTheCursorDown()
    {
        var (glk, display, memory, window) = Library(WindowType.TextGrid);
        var grid = (TextGridWindow)window;
        grid.Stream.PutChar('>');
        display.Inputs.Enqueue(GlkInput.Line(window, "hi"));

        glk.RequestLineEvent(window, memory, Buffer, 10, 0, false);
        glk.Select();

        // [glk #window_textgrid] The line remains at the cursor and the
        // cursor goes to the start of the next row.
        Assert.Equal(">hi", grid.Row(0).TrimEnd());
        Assert.Equal((0, 1), (grid.CursorX, grid.CursorY));
    }

    [Fact]
    public void TheEchoStreamGetsTheLineUnlessEchoIsOff()
    {
        var (glk, display, memory, window) = Library();
        var echo = glk.OpenMemoryStream(memory, Buffer + 0x40, 20, false, FileMode.Write, 0)!;
        glk.SetEchoStream(window, echo);
        display.Inputs.Enqueue(GlkInput.Line(window, "ab"));
        display.Inputs.Enqueue(GlkInput.Line(window, "cd"));

        glk.RequestLineEvent(window, memory, Buffer, 10, 0, false);
        glk.Select();
        window.EchoLineInput = false;
        glk.RequestLineEvent(window, memory, Buffer, 10, 0, false);
        glk.Select();

        // [glk #echo_streams] The first line and its newline reach the
        // echo stream; [glk op:set_echo_line_event] the second does not,
        // and the display did not show it either.
        Assert.Equal("ab\n", System.Text.Encoding.Latin1.GetString(memory.Slice(Buffer + 0x40, 3)));
        Assert.Equal(3u, echo.WriteCount);
        Assert.Equal("ab\n", display.Text(window));
    }

    [Fact]
    public void TerminatorsAreRecordedAndReported()
    {
        var (glk, display, memory, window) = Library();
        GlkLibrary.SetLineTerminators(window, [GlkKeyCode.Escape, GlkKeyCode.Func1, 'x', GlkKeyCode.Return]);
        display.Inputs.Enqueue(GlkInput.Line(window, "help", GlkKeyCode.Func1));

        glk.RequestLineEvent(window, memory, Buffer, 10, 0, false);
        var result = glk.Select();

        // [glk op:set_terminators_line_event] Only special keys are kept;
        // [glk #line_events] the terminator that ended the line is val2.
        Assert.Equal([GlkKeyCode.Escape, GlkKeyCode.Func1, GlkKeyCode.Return], window.LineTerminators);
        Assert.Equal(GlkKeyCode.Func1, result.Value2);
        Assert.Equal(4u, result.Value1);
    }

    [Fact]
    public void TimerEventsComeWhenTheIntervalHasPassed()
    {
        var clock = new ManualClock();
        var (glk, display, _, _) = Library(clock: clock);

        // [glk #timer_events] Nothing until an interval passes; then one
        // event, not a backlog; and the display is asked to wait no
        // longer than the time left.
        glk.RequestTimerEvents(20);
        Assert.Equal(EventType.None, glk.SelectPoll().Type);
        clock.Advance(19);
        Assert.Equal(EventType.None, glk.SelectPoll().Type);

        clock.Advance(41);
        Assert.Equal(EventType.Timer, glk.SelectPoll().Type);
        Assert.Equal(EventType.None, glk.SelectPoll().Type);

        clock.Advance(60);
        var waited = glk.Select();
        Assert.Equal(new GlkEvent(EventType.Timer, null, 0, 0), waited);
        Assert.Empty(display.Timeouts);

        display.Inputs.Enqueue(GlkInput.Timer);
        glk.RequestTimerEvents(1000);
        display.Inputs.Enqueue(GlkInput.Timer);
        glk.RequestTimerEvents(0);
        Assert.Equal(EventType.None, glk.SelectPoll().Type);
        Assert.Equal(0u, glk.TimerInterval);
    }

    [Fact]
    public void TheDisplayIsToldHowLongToWaitForTheTimer()
    {
        var clock = new ManualClock();
        var (glk, display, _, _) = Library(clock: clock);
        display.Inputs.Enqueue(GlkInput.Timer);

        glk.RequestTimerEvents(500);
        clock.Advance(5);

        // [glk #timer_events] The display reported the timer early, the
        // library found it not yet due and asked again, and by then the
        // queue was empty; the timeout it was given was the time left
        // of the interval.
        Assert.Throws<EndOfStreamException>(() => glk.Select());
        Assert.Equal(2, display.Timeouts.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(495), display.Timeouts[0]);
    }

    [Fact]
    public void GestaltAnswersForInput()
    {
        Assert.Equal(1u, GlkLibrary.Gestalt((uint)GestaltSelector.Timer, 0, null));
        Assert.Equal(1u, GlkLibrary.Gestalt((uint)GestaltSelector.LineInputEcho, 0, null));
        Assert.Equal(1u, GlkLibrary.Gestalt((uint)GestaltSelector.LineTerminators, 0, null));
        Assert.Equal(0u, GlkLibrary.Gestalt((uint)GestaltSelector.LineTerminatorKey, GlkKeyCode.Escape, null));
        Assert.Equal(1u, GlkLibrary.Gestalt((uint)GestaltSelector.CharInput, 'a', null));
        Assert.Equal(1u, GlkLibrary.Gestalt((uint)GestaltSelector.CharInput, GlkKeyCode.Return, null));
        Assert.Equal(0u, GlkLibrary.Gestalt((uint)GestaltSelector.CharInput, GlkKeyCode.Left, null));
        Assert.Equal(0u, GlkLibrary.Gestalt((uint)GestaltSelector.CharInput, 3, null));
        Assert.Equal(0u, GlkLibrary.Gestalt((uint)GestaltSelector.MouseInput, (uint)WindowType.TextGrid, null));
    }

    [Fact]
    public void AGameReadsALineThroughTheOpcode()
    {
        const uint Text = GlulxRun.RamStart + 0x100;
        var display = new RecordingGlkDisplay();
        var code = new GlulxAssembler().Function("main").Op(Opcode.SetIOSys, C(2), C(0));
        Call(code, WindowOpen, Ram(0), C(0), C(0), C(0), C((uint)WindowType.TextBuffer), C(0));
        Call(code, SetWindow, Discard, Ram(0));
        code.Op(Opcode.StreamStr, At("prompt"));
        Call(code, RequestLineEvent, Discard, Ram(0), C(Text), C(20), C(0));
        Call(code, Select, Discard, C(GlulxRun.RamStart + 4));
        Call(code, RequestCharEvent, Discard, Ram(0));
        Call(code, Select, Discard, C(-1));
        code.Op(Opcode.Copy, Sp, Ram(20)).Op(Opcode.Copy, Sp, Ram(24)).Op(Opcode.Copy, Sp, Ram(28)).Op(Opcode.Copy, Sp, Ram(32));
        Call(code, RequestTimerEvents, Discard, C(0));
        Call(code, SelectPoll, Discard, C(GlulxRun.RamStart + 40));
        Call(code, RequestLineEvent, Discard, Ram(0), C(Text), C(20), C(0));
        Call(code, CancelLineEvent, Discard, Ram(0), C(GlulxRun.RamStart + 60));
        code.Return(C(0)).CString("prompt", "> ");

        display.Inputs.Enqueue(GlkInput.Line(null!, "take lamp"));
        display.Inputs.Enqueue(GlkInput.KeyPress(null!, 'q'));
        var machine = GlulxRun.Run(code, glk: new GlkLibrary(display));

        // [glulx op:glk] The event structure in memory: line input from
        // window 1 of nine characters; on the stack with the last field
        // on top: character input of q; polling with no timer gives no
        // event; cancelling gives an empty line.
        Assert.Equal("> take lamp\n", display.Output);
        Assert.Equal("take lamp", System.Text.Encoding.Latin1.GetString(machine.Memory.Slice(Text, 9)));
        Assert.Equal(((uint)EventType.LineInput, 1u, 9u, 0u), (machine.Ram(4), machine.Ram(8), machine.Ram(12), machine.Ram(16)));
        Assert.Equal((0u, (uint)'q', 1u, (uint)EventType.CharInput), (machine.Ram(20), machine.Ram(24), machine.Ram(28), machine.Ram(32)));
        Assert.Equal((uint)EventType.None, machine.Ram(40));
        Assert.Equal(((uint)EventType.LineInput, 0u), (machine.Ram(60), machine.Ram(68)));
        Assert.Equal(0u, machine.Stack.StackPointer);
    }

    [Fact]
    public void AGameStopsWhenInputEnds()
    {
        var code = new GlulxAssembler().Function("main").Op(Opcode.SetIOSys, C(2), C(0));
        Call(code, WindowOpen, Ram(0), C(0), C(0), C(0), C((uint)WindowType.TextBuffer), C(0));
        Call(code, RequestCharEvent, Discard, Ram(0));
        Call(code, Select, Discard, C(-1));
        code.Return(C(0));

        Assert.Throws<EndOfStreamException>(() => GlulxRun.Run(code, glk: new GlkLibrary(new RecordingGlkDisplay())));
    }

    private static void Call(GlulxAssembler code, uint selector, Arg result, params Arg[] args)
    {
        for (var i = args.Length - 1; i >= 0; i--)
        {
            code.Op(Opcode.Copy, args[i], Sp);
        }

        code.Op(Opcode.Glk, C(selector), C(args.Length), result);
    }
}

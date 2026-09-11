using Rezrov.Glulx;
using Rezrov.Glulx.Execution;
using Rezrov.Glulx.Glk;
using Rezrov.Glulx.Instructions;
using static Rezrov.Tests.GlulxAssembler;
using FileMode = Rezrov.Glulx.Glk.FileMode;

namespace Rezrov.Tests;

/// <summary>
/// [glulx op:glk] The glk opcode from the machine's side: arguments off
/// the stack, references into memory and onto the stack, structures,
/// arrays, and strings, and the result stored.
/// </summary>
public class GlkDispatchTests
{
    private const uint WindowOpen = 0x0023;
    private const uint WindowClose = 0x0024;
    private const uint WindowGetSize = 0x0025;
    private const uint StreamOpenMemory = 0x0043;
    private const uint StreamClose = 0x0044;
    private const uint SetWindow = 0x002F;
    private const uint PutChar = 0x0080;
    private const uint PutCharStream = 0x0081;
    private const uint PutString = 0x0082;
    private const uint PutBuffer = 0x0084;
    private const uint PutCharUni = 0x0128;
    private const uint PutStringUni = 0x0129;
    private const uint Gestalt = 0x0004;
    private const uint Exit = 0x0001;
    private const uint Select = 0x00C0;
    private const uint FilerefIterate = 0x0064;

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

    /// <summary>
    /// The address of a RAM word, for a reference argument.
    /// </summary>
    private static Arg Ref(uint offset) => C(GlulxRun.RamStart + offset);

    /// <summary>
    /// A program that opens a text buffer window, keeps its id in RAM
    /// word 0, and makes it the current window.
    /// </summary>
    private static GlulxAssembler WithWindow()
    {
        var code = new GlulxAssembler().Function("main").Op(Opcode.SetIOSys, C(2), C(0));
        Glk(code, WindowOpen, Ram(0), C(0), C(0), C(0), C((uint)WindowType.TextBuffer), C(0));
        return Glk(code, SetWindow, Discard, Ram(0));
    }

    private static (GlulxMachine Machine, RecordingGlkDisplay Display) Run(GlulxAssembler code)
    {
        var display = new RecordingGlkDisplay();
        var machine = GlulxRun.Run(code, glk: new GlkLibrary(display));
        return (machine, display);
    }

    [Fact]
    public void ArgumentsComeOffTheStackAndTheResultIsStored()
    {
        var code = new GlulxAssembler().Function("main");
        Glk(code, Gestalt, Ram(0), C(0), C(0));
        var (machine, _) = Run(code.Return(C(0)));

        // [glk #version] The Glk version, through the dispatch layer.
        Assert.Equal(0x00000706u, machine.Ram(0));
        Assert.Equal(0u, machine.Stack.StackPointer);
    }

    [Fact]
    public void AWindowPrintsThroughItsStream()
    {
        var code = WithWindow();
        Glk(code, PutChar, Discard, C('H'));
        Glk(code, PutString, Discard, At("text"));
        Glk(code, PutCharUni, Discard, C(0x3B1));
        Glk(code, PutStringUni, Discard, At("greek"));
        Glk(code, PutBuffer, Discard, At("buffer"), C(3));
        code.Return(C(0))
            .CString("text", "ello ")
            .Label("greek").Bytes(0xE2, 0, 0, 0).Word(0x3B2).Word(0)
            .Label("buffer").Bytes((byte)'!', (byte)'?', (byte)'.');
        var (machine, display) = Run(code);

        // [glulx op:glk] A string argument is an E0 object, a Unicode
        // string an E2 object, and an array an address and a length.
        Assert.Equal("Hello αβ!?.", display.Output);
        Assert.Equal(1u, machine.Ram(0));
        Assert.Empty(machine.Glk.Warnings);
    }

    [Fact]
    public void ReferencesGoToMemoryOrToTheStack()
    {
        var code = WithWindow();
        Glk(code, WindowGetSize, Discard, Ram(0), Ref(4), Ref(8));
        Glk(code, WindowGetSize, Discard, Ram(0), C(-1), C(-1));
        code.Op(Opcode.Copy, Sp, Ram(12)).Op(Opcode.Copy, Sp, Ram(16));
        Glk(code, WindowGetSize, Discard, Ram(0), C(0), Ref(20));
        var (machine, _) = Run(code.Return(C(0)));

        // [glulx op:glk] By address the values are written in place; by
        // -1 they are pushed in argument order, so the height is on top;
        // a null reference is skipped.
        Assert.Equal((40u, 10u), (machine.Ram(4), machine.Ram(8)));
        Assert.Equal((10u, 40u), (machine.Ram(12), machine.Ram(16)));
        Assert.Equal(10u, machine.Ram(20));
        Assert.Equal(0u, machine.Stack.StackPointer);
    }

    [Fact]
    public void AStructureGoesToMemoryOrToTheStack()
    {
        var code = WithWindow();
        Glk(code, PutChar, Discard, C('x'));
        Glk(code, PutChar, Discard, C('y'));
        Glk(code, WindowClose, Discard, Ram(0), Ref(4));
        Glk(code, WindowOpen, Ram(0), C(0), C(0), C(0), C((uint)WindowType.TextBuffer), C(0));
        Glk(code, SetWindow, Discard, Ram(0));
        Glk(code, PutChar, Discard, C('z'));
        Glk(code, WindowClose, Discard, Ram(0), C(-1));
        code.Op(Opcode.Copy, Sp, Ram(12)).Op(Opcode.Copy, Sp, Ram(16));
        var (machine, _) = Run(code.Return(C(0)));

        // [glk op:window_close] The read and write counts: in memory as
        // two words, and on the stack with the last field on top.
        Assert.Equal((0u, 2u), (machine.Ram(4), machine.Ram(8)));
        Assert.Equal((1u, 0u), (machine.Ram(12), machine.Ram(16)));
        Assert.Null(machine.Glk.Root);
    }

    [Fact]
    public void AMemoryStreamIsOpenedWrittenAndClosedThroughTheOpcode()
    {
        const uint Buffer = GlulxRun.RamStart + 0x100;
        var code = new GlulxAssembler().Function("main");
        Glk(code, StreamOpenMemory, Ram(0), C(Buffer), C(8), C((uint)FileMode.Write), C(77));
        Glk(code, PutCharStream, Discard, Ram(0), C('o'));
        Glk(code, PutCharStream, Discard, Ram(0), C('k'));
        Glk(code, StreamClose, Discard, Ram(0), Ref(4));
        var (machine, _) = Run(code.Return(C(0)));

        // [glulx op:glk] The retained array is written in memory, and the
        // counts come back through the structure.
        Assert.Equal("ok", System.Text.Encoding.Latin1.GetString(machine.Memory.Slice(Buffer, 2)));
        Assert.Equal((0u, 2u), (machine.Ram(4), machine.Ram(8)));
        Assert.Equal(0, machine.Glk.Streams.Count);
    }

    [Fact]
    public void TheOutputOpcodesReachGlkWhenItIsTheIOSystem()
    {
        var code = WithWindow()
            .Op(Opcode.StreamChar, C('a'))
            .Op(Opcode.StreamUniChar, C(0x3B1))
            .Op(Opcode.StreamNum, C(-42))
            .Op(Opcode.StreamStr, At("plain"))
            .Op(Opcode.StreamStr, At("wide"))
            .Op(Opcode.SetStringTbl, At("table"))
            .Op(Opcode.StreamStr, At("packed"))
            .Return(C(0));
        code.CString("plain", "bc")
            .Label("wide").Bytes(0xE2, 0, 0, 0).Word(0x3B2).Word(0)
            .Label("table").Word(0x40).Word(3).Ref("root")
            .Label("root").Bytes(0).Ref("d").Ref("end")
            .Label("d").Bytes(3, (byte)'d', (byte)'e', 0)
            .Label("end").Bytes(1)
            .CompressedString("packed", 0, 0, 1);
        var (machine, display) = Run(code);

        // [glulx #input-and-output] streamchar, streamunichar, streamnum,
        // and streamstr in all three string types print through Glk.
        Assert.Equal("aα-42bcβdede", display.Output);
        Assert.Equal(0u, machine.Stack.StackPointer);
    }

    [Fact]
    public void ExitEndsTheGame()
    {
        var code = WithWindow();
        Glk(code, Exit, Discard);
        Glk(code, PutChar, Discard, C('x'));
        var (machine, display) = Run(code.Return(C(0)));

        // [glk op:exit]
        Assert.True(machine.HasQuit);
        Assert.Equal("", display.Output);
    }

    [Fact]
    public void EmptyClassesIterateToNothing()
    {
        var code = new GlulxAssembler().Function("main");
        Glk(code, FilerefIterate, Ram(0), C(0), Ref(4));
        var (machine, _) = Run(code.Return(C(0)));

        // [glk #opaque_iteration] No file references yet: null, and no
        // rock.
        Assert.Equal(0u, machine.Ram(0));
        Assert.Equal(0u, machine.Ram(4));
    }

    [Fact]
    public void UnknownAndUnbuiltFunctionsAreTold()
    {
        var unknown = new GlulxAssembler().Function("main");
        Glk(unknown, 0x0006, Discard);
        var e = Assert.Throws<GlulxException>(() => Run(unknown.Return(C(0))));
        Assert.Contains("Unknown Glk function 0006", e.Message, StringComparison.Ordinal);

        var select = WithWindow();
        Glk(select, Select, Discard, C(-1));
        var notYet = Assert.Throws<NotSupportedException>(() => Run(select.Return(C(0))));
        Assert.Contains("glk_select", notYet.Message, StringComparison.Ordinal);

        var wrongCount = new GlulxAssembler().Function("main");
        Glk(wrongCount, Gestalt, Discard, C(0));
        var count = Assert.Throws<GlulxException>(() => Run(wrongCount.Return(C(0))));
        Assert.Contains("arguments", count.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStringArgumentMustBeUnencoded()
    {
        var code = WithWindow();
        Glk(code, PutString, Discard, At("packed"));
        code.Return(C(0)).CompressedString("packed", 1);

        var e = Assert.Throws<GlulxException>(() => Run(code));
        Assert.Contains("not an unencoded string", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInvalidObjectIdIsAWarningNotAHalt()
    {
        var code = new GlulxAssembler().Function("main");
        Glk(code, WindowGetSize, Discard, C(99), Ref(4), Ref(8));
        var (machine, _) = Run(code.Return(C(0)));

        Assert.Contains(machine.Glk.Warnings, w => w.Contains("invalid window id 99", StringComparison.Ordinal));
        Assert.Equal(0u, machine.Ram(4));
    }
}

using System.Text;
using Rezrov.Glulx;
using Rezrov.Glulx.Execution;
using Rezrov.Glulx.Instructions;
using static Rezrov.Tests.GlulxAssembler;

namespace Rezrov.Tests;

/// <summary>
/// Strings and the output opcodes through the null and filter I/O
/// systems: unencoded, Unicode, and compressed strings, the decoding
/// table's every node type, and the call stubs that let a function run
/// in the middle of a string.
/// </summary>
public class GlulxStringTests
{
    // The filter function stores each character it is given as a word
    // at Buffer plus four times the count in RAM word 0, then bumps the
    // count, so a test reads the text back out of RAM.
    private const uint Buffer = GlulxRun.RamStart + 0x100;

    /// <summary>
    /// A program that selects the filter system, runs the body, returns,
    /// and carries the filter function and whatever data follows.
    /// </summary>
    private static GlulxAssembler Filtered(Action<GlulxAssembler> body, Action<GlulxAssembler>? data = null)
    {
        var code = new GlulxAssembler().Function("main").Op(Opcode.SetIOSys, C(1), At("filter"));
        body(code);
        code.Return(C(0));

        code.Function("filter", FunctionType.LocalArguments, (4, 1))
            .Op(Opcode.AStore, C(Buffer), Ram(0), Loc(0))
            .Op(Opcode.Add, Ram(0), C(1), Ram(0))
            .Return(C(0));

        data?.Invoke(code);
        return code;
    }

    /// <summary>What the filter function was given, as text.</summary>
    private static string Text(GlulxMachine machine)
    {
        var text = new StringBuilder();
        var count = machine.Ram(0);
        for (uint i = 0; i < count; i++)
        {
            text.Append(char.ConvertFromUtf32((int)machine.Memory.ReadWord(Buffer + (4 * i))));
        }

        return text.ToString();
    }

    /// <summary>
    /// The decoding table every compressed-string test shares, with a
    /// leaf of each kind. The codes: a is 0, the end is 10, bc is 110,
    /// the reference to hello is 1110, the Unicode alpha 11110, the
    /// double reference 111110, the function 1111110, and the function
    /// with arguments 1111111.
    /// </summary>
    private static void Table(GlulxAssembler code) => code
        .Label("table").Word(0x80).Word(15).Ref("root")
        .Label("root").Bytes(0).Ref("a").Ref("b1")
        .Label("a").Bytes(2, (byte)'a')
        .Label("b1").Bytes(0).Ref("end").Ref("b2")
        .Label("end").Bytes(1)
        .Label("b2").Bytes(0).Ref("bc").Ref("b3")
        .Label("bc").Bytes(3, (byte)'b', (byte)'c', 0)
        .Label("b3").Bytes(0).Ref("ind").Ref("b4")
        .Label("ind").Bytes(8).Ref("hello")
        .Label("b4").Bytes(0).Ref("uni").Ref("b5")
        .Label("uni").Bytes(4).Word(0x3B1)
        .Label("b5").Bytes(0).Ref("dbl").Ref("b6")
        .Label("dbl").Bytes(9).Ref("pointer")
        .Label("b6").Bytes(0).Ref("fn").Ref("fnargs")
        .Label("fn").Bytes(8).Ref("shout")
        .Label("fnargs").Bytes(0x0A).Ref("numbers").Word(2).Word(7).Word(35)
        .Label("pointer").Ref("nested")
        .CString("hello", "hello")
        .CompressedString("nested", 0, 0, 1, 0)
        .Function("shout")
        .Op(Opcode.StreamChar, C('!'))
        .Return(C(0))
        .Function("numbers", FunctionType.LocalArguments, (4, 2))
        .Op(Opcode.StreamNum, Loc(0))
        .Op(Opcode.StreamNum, Loc(4))
        .Return(C(0));

    private static GlulxAssembler WithTable(Action<GlulxAssembler> body, Action<GlulxAssembler>? data = null) =>
        Filtered(
            code =>
            {
                code.Op(Opcode.SetStringTbl, At("table"));
                body(code);
            },
            code =>
            {
                Table(code);
                data?.Invoke(code);
            });

    [Fact]
    public void AnUnencodedStringGoesThroughTheFilterOneCharacterAtATime()
    {
        var machine = GlulxRun.Run(Filtered(
            code => code.Op(Opcode.StreamStr, At("hello")),
            code => code.CString("hello", "hello")));

        // [glulx #callfilter] Each character is a call of the output
        // function, and [glulx #callstring] the stubs are all gone by
        // the time the string is done.
        Assert.Equal("hello", Text(machine));
        Assert.Equal(0u, machine.Stack.StackPointer);
    }

    [Fact]
    public void AUnicodeStringCarriesWholeCodePoints()
    {
        var machine = GlulxRun.Run(Filtered(
            code => code.Op(Opcode.StreamStr, At("greek")),
            code => code.Label("greek").Bytes(0xE2, 0, 0, 0).Word(0x3B1).Word(0x1F600).Word(0)));

        // [glulx #string_unicode] Three padding bytes, then four-byte
        // code points to a zero word.
        Assert.Equal("α\U0001F600", Text(machine));
    }

    [Fact]
    public void StreamCharAndStreamNumPrintThroughTheFilterToo()
    {
        var machine = GlulxRun.Run(Filtered(code => code
            .Op(Opcode.StreamChar, C(0x141))
            .Op(Opcode.StreamUniChar, C(0x3B1))
            .Op(Opcode.StreamNum, C(-123))
            .Op(Opcode.StreamNum, C(0))
            .Op(Opcode.StreamNum, C(int.MinValue))));

        // [glulx op:streamchar] Truncated to eight bits; [glulx
        // op:streamunichar] not; [glulx op:streamnum] signed decimal.
        Assert.Equal("Aα-1230-2147483648", Text(machine));
        Assert.Equal(0u, machine.Stack.StackPointer);
    }

    [Fact]
    public void TheNullSystemDiscardsEverything()
    {
        var machine = GlulxRun.Run(Filtered(
            code => code
                .Op(Opcode.SetIOSys, C(0), C(0))
                .Op(Opcode.StreamStr, At("hello"))
                .Op(Opcode.StreamChar, C('x'))
                .Op(Opcode.StreamNum, C(42))
                .Op(Opcode.GetIOSys, Ram(4), Ram(8)),
            code => code.CString("hello", "hello")));

        // [glulx op:setiosys] The null system: output discarded, and
        // [glulx op:getiosys] mode and rock read back.
        Assert.Equal("", Text(machine));
        Assert.Equal((0u, 0u), (machine.Ram(4), machine.Ram(8)));
    }

    [Fact]
    public void AnUnsupportedSystemFallsBackToNull()
    {
        var machine = GlulxRun.Run(Filtered(code => code
            .Op(Opcode.SetIOSys, C(2), C(99))
            .Op(Opcode.GetIOSys, Ram(4), Ram(8))
            .Op(Opcode.SetIOSys, C(20), C(7))
            .Op(Opcode.GetIOSys, Ram(12), Ram(16))));

        // [glulx op:setiosys] Glk is supported; FyreVM is not and
        // defaults to null, rock and all.
        Assert.Equal((2u, 99u), (machine.Ram(4), machine.Ram(8)));
        Assert.Equal((0u, 7u), (machine.Ram(12), machine.Ram(16)));
    }

    [Fact]
    public void ACompressedStringFollowsTheTableBitByBit()
    {
        var machine = GlulxRun.Run(WithTable(
            code => code.Op(Opcode.StreamStr, At("s")),
            code => code.CompressedString("s", 0, 1, 1, 0, 0, 1, 0)));

        // [glulx #string_enc] a, bc, a, then the terminator: single
        // character and C-string leaves.
        Assert.Equal("abca", Text(machine));
        Assert.Equal(0u, machine.Stack.StackPointer);
    }

    [Fact]
    public void IndirectReferencesPrintStringsAndCallFunctions()
    {
        var machine = GlulxRun.Run(WithTable(
            code => code.Op(Opcode.StreamStr, At("s")),
            code => code.CompressedString(
                "s",
                1, 1, 1, 0,
                1, 1, 1, 1, 0,
                1, 1, 1, 1, 1, 0,
                1, 1, 1, 1, 1, 1, 0,
                1, 1, 1, 1, 1, 1, 1,
                1, 0)));

        // [glulx #string_table] In order: the reference to the C string
        // hello; the Unicode alpha; the double reference, through a word
        // in memory, to a compressed string printing aa; the function
        // that prints an exclamation mark; and the function called with
        // 7 and 35, which it prints as numbers. Each one suspends the
        // outer string and resumes it afterward.
        Assert.Equal("helloαaa!735", Text(machine));
        Assert.Equal(0u, machine.Stack.StackPointer);
    }

    [Fact]
    public void FunctionsInStringsRunEvenUnderTheNullSystem()
    {
        var machine = GlulxRun.Run(WithTable(
            code => code
                .Op(Opcode.SetIOSys, C(0), C(0))
                .Op(Opcode.StreamStr, At("s"))
                .Op(Opcode.SetIOSys, C(1), At("filter"))
                .Op(Opcode.StreamStr, At("s")),
            code => code.CompressedString("s", 1, 1, 1, 1, 1, 1, 1, 1, 0)));

        // [glulx #string_table] Under null the function with arguments
        // is still called, its streamnum output goes nowhere, and the
        // stubs unwind cleanly; the same print under filter shows 735.
        Assert.Equal("735", Text(machine));
        Assert.Equal(0u, machine.Stack.StackPointer);
    }

    [Fact]
    public void AStringInsideAStringResumesTheOuterOne()
    {
        var machine = GlulxRun.Run(WithTable(
            code => code.Op(Opcode.StreamStr, At("s")),
            code => code.CompressedString("s", 0, 1, 1, 1, 1, 1, 0, 0, 1, 0)));

        // [glulx #callstring] a, then the nested compressed string aa
        // through the double reference, then a, then the end: the type
        // 10 stub brings decoding back to the right bit.
        Assert.Equal("aaaa", Text(machine));
    }

    [Fact]
    public void PrintingACompressedStringWithoutATableIsFatal()
    {
        var e = Assert.Throws<GlulxException>(() => GlulxRun.Run(Filtered(
            code => code.Op(Opcode.StreamStr, At("s")),
            code => code.CompressedString("s", 1, 0))));

        // [glulx op:streamstr]
        Assert.Contains("no decoding table", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PrintingSomethingThatIsNotAStringIsFatal()
    {
        var e = Assert.Throws<GlulxException>(() => GlulxRun.Run(Filtered(code => code.Op(Opcode.StreamStr, At("filter")))));
        Assert.Contains("non-string", e.Message, StringComparison.Ordinal);

        var reserved = Assert.Throws<GlulxException>(() => GlulxRun.Run(Filtered(
            code => code.Op(Opcode.StreamStr, At("odd")),
            code => code.Label("odd").Bytes(0xE7, 0))));
        Assert.Contains("unknown type of string", reserved.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStringTableCanBeReadAndChanged()
    {
        var machine = GlulxRun.Run(WithTable(code => code
            .Op(Opcode.GetStringTbl, Ram(4))
            .Op(Opcode.SetStringTbl, C(0))
            .Op(Opcode.GetStringTbl, Ram(8))));

        // [glulx op:getstringtbl] The table set, then none.
        Assert.NotEqual(0u, machine.Ram(4));
        Assert.Equal(0u, machine.Ram(8));

        // [glulx op:setstringtbl] Without a table in the header, the
        // machine starts with none.
        Assert.Equal(0u, GlulxRun.Run(Filtered(code => code.Op(Opcode.GetStringTbl, Ram(4)))).Ram(4));
    }

    [Fact]
    public void ATableInRamIsReadAgainWhenMemoryChanges()
    {
        const uint RamTable = GlulxRun.RamStart + 0x200;
        const uint RamRoot = RamTable + 12;

        var machine = GlulxRun.Run(WithTable(
            code => code
                .Op(Opcode.Copy, C(21), Mem(RamTable))
                .Op(Opcode.Copy, C(3), Mem(RamTable + 4))
                .Op(Opcode.Copy, C(RamRoot), Mem(RamTable + 8))
                .Op(Opcode.CopyB, C(0), Mem(RamRoot))
                .Op(Opcode.Copy, At("a"), Mem(RamRoot + 1))
                .Op(Opcode.Copy, At("b1"), Mem(RamRoot + 5))
                .Op(Opcode.SetStringTbl, C(RamTable))
                .Op(Opcode.StreamStr, At("s"))
                .Op(Opcode.Copy, Mem(RamRoot + 1), Sp)
                .Op(Opcode.Copy, Mem(RamRoot + 5), Mem(RamRoot + 1))
                .Op(Opcode.Copy, Sp, Mem(RamRoot + 5))
                .Op(Opcode.StreamStr, At("s")),
            code => code.CompressedString("s", 0, 1, 1, 0, 0, 1, 0)));

        // [glulx #string_table] A root in RAM whose children are the
        // shared table's nodes decodes abca; with its children swapped,
        // the same bits decode as hello, bc, and the end. The second
        // print sees the change because memory was written in between.
        Assert.Equal("abcahellobc", Text(machine));
    }

    [Fact]
    public void GestaltReportsUnicodeAndTheIOSystems()
    {
        var machine = GlulxRun.Run(Filtered(code => code
            .Op(Opcode.Gestalt, C(5), C(0), Ram(4))
            .Op(Opcode.Gestalt, C(4), C(1), Ram(8))
            .Op(Opcode.Gestalt, C(4), C(2), Ram(12))));

        // [glulx #opcodes_misc] Unicode, the filter system, and Glk.
        Assert.Equal((1u, 1u, 1u), (machine.Ram(4), machine.Ram(8), machine.Ram(12)));
    }
}

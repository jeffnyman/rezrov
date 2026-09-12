using System.Buffers.Binary;
using Rezrov.Glulx;
using Rezrov.Glulx.Glk;
using Rezrov.Glulx.Instructions;
using static Rezrov.Tests.GlulxAssembler;

namespace Rezrov.Tests;

/// <summary>
/// [glulx #opcodes_accel] Accelerated functions: replacing a call,
/// cancelling, the parameters, and the thirteen functions over a small
/// Inform-style object image built in RAM.
/// </summary>
public class GlulxAccelerationTests
{
    private const uint R = GlulxRun.RamStart;

    // The image, at offsets from RAMSTART: an instance A, a class C1
    // whose parent is the Class object, A's property table with the
    // class list (2), a word property (3), and a private two-word
    // property (5), C1's table with property 3, the property data, the
    // common property defaults, the global self, and the classes table.
    private const uint ObjA = R + 0x100;
    private const uint ObjC1 = R + 0x120;
    private const uint TableA = R + 0x140;
    private const uint ClassObject = R + 0x180;
    private const uint ClassList = R + 0x1A0;
    private const uint Prop3 = R + 0x1A4;
    private const uint Prop5 = R + 0x1A8;
    private const uint Defaults = R + 0x1B0;
    private const uint Self = R + 0x1E0;
    private const uint ClassesTable = R + 0x1E4;
    private const uint TableC1 = R + 0x200;
    private const uint ImageStart = R + 0x100;
    private const int ImageLength = 0x110;
    private const uint ObjectMetaclass = R + 0x300;
    private const uint RoutineMetaclass = R + 0x304;
    private const uint StringMetaclass = R + 0x308;
    private const uint IndividualStart = 100;

    private const uint WindowOpen = 0x0023;
    private const uint SetWindow = 0x002F;

    private static byte[] Image()
    {
        var image = new byte[ImageLength];
        void Word(uint address, uint value) => BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan((int)(address - ImageStart)), value);
        void Short(uint address, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan((int)(address - ImageStart)), value);
        void Entry(uint at, ushort id, ushort length, uint address, ushort flags)
        {
            Short(at, id);
            Short(at + 2, length);
            Word(at + 4, address);
            Short(at + 8, flags);
        }

        image[ObjA - ImageStart] = 0x70;
        Word(ObjA + 16, TableA);
        image[ObjC1 - ImageStart] = 0x70;
        Word(ObjC1 + 16, TableC1);
        Word(ObjC1 + 20, ClassObject);
        image[ClassObject - ImageStart] = 0x70;

        Word(TableA, 3);
        Entry(TableA + 4, 2, 1, ClassList, 0);
        Entry(TableA + 14, 3, 1, Prop3, 0);
        Entry(TableA + 24, 5, 2, Prop5, 1);
        Word(TableC1, 1);
        Entry(TableC1 + 4, 3, 1, Prop3, 0);

        Word(ClassList, ObjC1);
        Word(Prop3, 0x1234);
        Word(Prop5, 0x55);
        Word(Prop5 + 4, 0x66);
        Word(Defaults + 28, 0x77);
        Word(ClassesTable, ObjC1);
        return image;
    }

    /// <summary>
    /// A program with the image copied into RAM, the parameters set,
    /// and the stub function ready to be accelerated.
    /// </summary>
    private static GlulxAssembler Prepared() => new GlulxAssembler().Function("main")
        .Op(Opcode.MCopy, C(ImageLength), At("image"), C(ImageStart))
        .Op(Opcode.AccelParam, C(0), C(ClassesTable))
        .Op(Opcode.AccelParam, C(1), C(IndividualStart))
        .Op(Opcode.AccelParam, C(2), C(ClassObject))
        .Op(Opcode.AccelParam, C(3), C(ObjectMetaclass))
        .Op(Opcode.AccelParam, C(4), C(RoutineMetaclass))
        .Op(Opcode.AccelParam, C(5), C(StringMetaclass))
        .Op(Opcode.AccelParam, C(6), C(Self))
        .Op(Opcode.AccelParam, C(7), C(7))
        .Op(Opcode.AccelParam, C(8), C(Defaults));

    /// <summary>
    /// The stub, a string, and the image, after the code.
    /// </summary>
    private static GlulxAssembler Finish(GlulxAssembler code) => code
        .Return(C(0))
        .Function("stub")
        .Return(C(0xDEAD))
        .CString("str", "x")
        .Label("image")
        .Bytes(Image());

    private static GlulxAssembler Accelerate(GlulxAssembler code, uint number) =>
        code.Op(Opcode.AccelFunc, C(number), At("stub"));

    private static GlulxAssembler Call(GlulxAssembler code, Arg first, Arg second, uint result) =>
        code.Op(Opcode.CallFII, At("stub"), first, second, Ram(result));

    [Fact]
    public void ACallOfAnAcceleratedAddressRunsTheBuiltInFunction()
    {
        var code = new GlulxAssembler().Function("main")
            .Op(Opcode.CallFI, At("stub"), At("str"), Ram(0))
            .Op(Opcode.AccelFunc, C(1), At("stub"))
            .Op(Opcode.CallFI, At("stub"), At("str"), Ram(4))
            .Op(Opcode.CallFI, At("stub"), At("stub"), Ram(8))
            .Op(Opcode.CallFI, At("stub"), C(10), Ram(12))
            .Op(Opcode.AccelFunc, C(99), At("stub"))
            .Op(Opcode.CallFI, At("stub"), At("str"), Ram(16))
            .Op(Opcode.AccelFunc, C(1), At("stub"))
            .Op(Opcode.AccelFunc, C(0), At("stub"))
            .Op(Opcode.CallFI, At("stub"), At("str"), Ram(20))
            .Op(Opcode.AccelFunc, C(1), At("stub"))
            .Op(Opcode.CallF, At("tail"), Ram(24))
            .Op(Opcode.CopyB, C(0x70), Mem(R + 0x100))
            .Op(Opcode.CallFI, At("stub"), C(R + 0x100), Ram(28))
            .Op(Opcode.CallFI, At("stub"), At("rom"), Ram(32))
            .Op(Opcode.CallFI, At("stub"), C(0xA00), Ram(36))
            .Return(C(0))
            .Function("tail")
            .Op(Opcode.Copy, At("str"), Sp)
            .Op(Opcode.TailCall, At("stub"), C(1))
            .Function("stub")
            .Return(C(0xDEAD))
            .CString("str", "x")
            .Label("rom")
            .Bytes(0x70);

        var machine = GlulxRun.Run(code);

        // [glulx op:accelfunc] The game's own function until the request;
        // then Z__Region: 3 for a string, 2 for a function, 0 below 36;
        // an unsupported number cancels, so does zero; a tail call is a
        // call too; [glulx #opcodes_accel] an object type byte counts
        // only in RAM, and an address past memory is nothing.
        Assert.Equal(0xDEADu, machine.Ram(0));
        Assert.Equal(3u, machine.Ram(4));
        Assert.Equal(2u, machine.Ram(8));
        Assert.Equal(0u, machine.Ram(12));
        Assert.Equal(0xDEADu, machine.Ram(16));
        Assert.Equal(0xDEADu, machine.Ram(20));
        Assert.Equal(3u, machine.Ram(24));
        Assert.Equal(1u, machine.Ram(28));
        Assert.Equal(0u, machine.Ram(32));
        Assert.Equal(0u, machine.Ram(36));
        Assert.Equal(1, machine.Accelerator.Count);
    }

    [Fact]
    public void GestaltAndParameters()
    {
        var machine = GlulxRun.Run(new GlulxAssembler().Function("main")
            .Op(Opcode.Gestalt, C(9), C(0), Ram(0))
            .Op(Opcode.Gestalt, C(10), C(1), Ram(4))
            .Op(Opcode.Gestalt, C(10), C(13), Ram(8))
            .Op(Opcode.Gestalt, C(10), C(14), Ram(12))
            .Op(Opcode.Gestalt, C(10), C(0), Ram(16))
            .Op(Opcode.AccelParam, C(8), C(0x1234))
            .Op(Opcode.AccelParam, C(9), C(0x5678))
            .Return(C(0)));

        // [glulx #opcodes_misc] Acceleration (9) and AccelFunc (10) for
        // 1 to 13; [glulx op:accelparam] an unknown position is ignored.
        Assert.Equal(1u, machine.Ram(0));
        Assert.Equal(1u, machine.Ram(4));
        Assert.Equal(1u, machine.Ram(8));
        Assert.Equal(0u, machine.Ram(12));
        Assert.Equal(0u, machine.Ram(16));
        Assert.Equal(0x1234u, machine.Accelerator.Parameter(8));
        Assert.Equal(0u, machine.Accelerator.Parameter(9));
    }

    [Fact]
    public void AcceleratingANonFunctionIsFatal()
    {
        // [glulx op:accelfunc] The address must be a function's, as the
        // reference interpreter insists.
        Assert.Throws<GlulxException>(() => GlulxRun.Run(new GlulxAssembler().Function("main")
            .Op(Opcode.AccelFunc, C(1), At("str"))
            .Return(C(0))
            .CString("str", "x")));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(6u)]
    public void PropertyAddressLengthAndValue(uint offset)
    {
        var code = Prepared();
        Accelerate(code, 3 + offset);
        Call(code, C(ObjA), C(3), 0);
        Call(code, C(ObjA), C(5), 4);
        Call(code, C(ObjA), C(9), 8);
        Call(code, C(ObjA), C(3 << 16), 12);
        Call(code, C(ObjC1), C(3), 16);
        code.Op(Opcode.Copy, C(ObjA), Mem(Self));
        Call(code, C(ObjA), C(5), 20);
        Accelerate(code, 4 + offset);
        Call(code, C(ObjA), C(3), 24);
        Call(code, C(ObjA), C(5), 28);
        Call(code, C(ObjA), C(9), 32);
        Accelerate(code, 6 + offset);
        Call(code, C(ObjA), C(3), 36);
        Call(code, C(ObjA), C(5), 40);
        Call(code, C(ObjA), C(7), 44);
        Call(code, C(ObjA), C(200), 48);

        var machine = GlulxRun.Run(Finish(code));

        // [glulx #opcodes_accel] RA__Pr: the data address; nothing for a
        // private property of another object, for a property the object
        // lacks, or for a common property asked of a class; through the
        // class part of a property number, the class's own property.
        Assert.Equal(Prop3, machine.Ram(0));
        Assert.Equal(0u, machine.Ram(4));
        Assert.Equal(0u, machine.Ram(8));
        Assert.Equal(Prop3, machine.Ram(12));
        Assert.Equal(0u, machine.Ram(16));
        Assert.Equal(Prop5, machine.Ram(20));

        // RL__Pr: the length in bytes.
        Assert.Equal(4u, machine.Ram(24));
        Assert.Equal(8u, machine.Ram(28));
        Assert.Equal(0u, machine.Ram(32));

        // RV__Pr: the value, or the default for a common property, or
        // zero with an error for anything else.
        Assert.Equal(0x1234u, machine.Ram(36));
        Assert.Equal(0x55u, machine.Ram(40));
        Assert.Equal(0x77u, machine.Ram(44));
        Assert.Equal(0u, machine.Ram(48));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(6u)]
    public void PropertyTableEntriesAndProvision(uint offset)
    {
        var code = Prepared();
        Accelerate(code, 2 + offset);
        Call(code, C(ObjA), C(3), 0);
        Call(code, C(ObjA), C(4), 4);
        Call(code, C(Prop3), C(3), 8);
        Accelerate(code, 7 + offset);
        Call(code, C(ObjA), C(3), 12);
        Call(code, C(ObjA), C(9), 16);
        Call(code, C(ObjC1), C(IndividualStart), 20);
        Call(code, At("str"), C(IndividualStart + 6), 24);
        Call(code, At("str"), C(3), 28);
        Call(code, At("stub"), C(IndividualStart + 5), 32);
        Call(code, At("stub"), C(3), 36);

        var machine = GlulxRun.Run(Finish(code));

        // [glulx #opcodes_accel] CP__Tab: the table entry, or zero for a
        // missing property or a non-object. OP__Pr: an object provides
        // what RA__Pr finds, a class its individual properties, a
        // string print, and a routine call.
        Assert.Equal(TableA + 14, machine.Ram(0));
        Assert.Equal(0u, machine.Ram(4));
        Assert.Equal(0u, machine.Ram(8));
        Assert.Equal(1u, machine.Ram(12));
        Assert.Equal(0u, machine.Ram(16));
        Assert.Equal(1u, machine.Ram(20));
        Assert.Equal(1u, machine.Ram(24));
        Assert.Equal(0u, machine.Ram(28));
        Assert.Equal(1u, machine.Ram(32));
        Assert.Equal(0u, machine.Ram(36));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(6u)]
    public void OfClassByMetaclassAndByClassList(uint offset)
    {
        var code = Prepared();
        Accelerate(code, 5 + offset);
        Call(code, C(ObjA), C(ObjectMetaclass), 0);
        Call(code, C(ObjA), C(ClassObject), 4);
        Call(code, C(ObjC1), C(ClassObject), 8);
        Call(code, C(ObjC1), C(ObjectMetaclass), 12);
        Call(code, C(ObjA), C(ObjC1), 16);
        Call(code, C(ObjC1), C(ObjC1), 20);
        Call(code, C(ObjA), C(Prop3), 24);
        Call(code, At("str"), C(StringMetaclass), 28);
        Call(code, At("stub"), C(RoutineMetaclass), 32);
        Call(code, At("stub"), C(StringMetaclass), 36);
        Call(code, C(ObjA), C(StringMetaclass), 40);

        var machine = GlulxRun.Run(Finish(code));

        // [glulx #opcodes_accel] OC__Cl: an instance is an Object and
        // not a Class, a class the other way round; membership of any
        // other class is by the class list; a non-class is an error;
        // strings and routines answer only to their metaclasses.
        Assert.Equal(1u, machine.Ram(0));
        Assert.Equal(0u, machine.Ram(4));
        Assert.Equal(1u, machine.Ram(8));
        Assert.Equal(0u, machine.Ram(12));
        Assert.Equal(1u, machine.Ram(16));
        Assert.Equal(0u, machine.Ram(20));
        Assert.Equal(0u, machine.Ram(24));
        Assert.Equal(1u, machine.Ram(28));
        Assert.Equal(1u, machine.Ram(32));
        Assert.Equal(0u, machine.Ram(36));
        Assert.Equal(0u, machine.Ram(40));
    }

    [Fact]
    public void ErrorsGoToTheCurrentGlkStream()
    {
        var display = new RecordingGlkDisplay();
        var code = Prepared().Op(Opcode.SetIOSys, C(2), C(0));
        code.Op(Opcode.Copy, C(0), Sp).Op(Opcode.Copy, C((uint)WindowType.TextBuffer), Sp).Op(Opcode.Copy, C(0), Sp).Op(Opcode.Copy, C(0), Sp).Op(Opcode.Copy, C(0), Sp);
        code.Op(Opcode.Glk, C(WindowOpen), C(5), Sp);
        code.Op(Opcode.Glk, C(SetWindow), C(1), Discard);
        Accelerate(code, 12);
        Call(code, C(ObjA), C(200), 0);

        GlulxRun.Run(Finish(code), glk: new GlkLibrary(display));

        // [glulx #opcodes_accel] Displayed by some convenient means: the
        // current Glk stream, on a line of its own.
        Assert.Equal("\n[** Programming error: tried to read (something) **]\n", display.Output);
    }
}

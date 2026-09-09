using Rezrov.ZMachine;

namespace Rezrov.Tests;

public class StoryHeaderTests
{
    // A minimal but internally consistent header: dynamic memory is the
    // header itself, static memory starts right after it, and high memory
    // starts at the same place. The file is padded to 128 bytes so there
    // is room to put an extension table in it when a test needs one.
    private static byte[] Story(ZMachineVersion version, int length = 128)
    {
        var bytes = new byte[length];
        bytes[0x00] = (byte)version;
        bytes[0x04] = 0x00; bytes[0x05] = 0x40; // high memory base = $0040
        bytes[0x0E] = 0x00; bytes[0x0F] = 0x40; // static memory base = $0040
        return bytes;
    }

    private static StoryHeader Header(byte[] bytes) => new(new ZMemory(bytes));

    private static void PutWord(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)(value >> 8);
        bytes[offset + 1] = (byte)value;
    }

    [Fact]
    public void ReadsTheFixedFields()
    {
        var bytes = Story(ZMachineVersion.V3);
        PutWord(bytes, 0x02, 88);      // release
        PutWord(bytes, 0x06, 0x4F05);  // initial PC
        PutWord(bytes, 0x08, 0x3B21);  // dictionary
        PutWord(bytes, 0x0A, 0x02B0);  // object table
        PutWord(bytes, 0x0C, 0x2187);  // globals
        PutWord(bytes, 0x18, 0x01F0);  // abbreviations
        "840726"u8.CopyTo(bytes.AsSpan(0x12));

        var header = Header(bytes);

        Assert.Equal(ZMachineVersion.V3, header.Version);
        Assert.Equal(88, header.Release);
        Assert.Equal(0x4F05, header.InitialProgramCounter);
        Assert.Equal(0x3B21, header.DictionaryAddress);
        Assert.Equal(0x02B0, header.ObjectTableAddress);
        Assert.Equal(0x2187, header.GlobalVariablesAddress);
        Assert.Equal(0x01F0, header.AbbreviationsTableAddress);
        Assert.Equal(0x0040, header.StaticMemoryBase);
        Assert.Equal(0x0040, header.HighMemoryBase);
        Assert.Equal("840726", header.SerialCode);
    }

    [Theory]
    [InlineData(ZMachineVersion.V1, 2)]
    [InlineData(ZMachineVersion.V2, 2)]
    [InlineData(ZMachineVersion.V3, 2)]
    [InlineData(ZMachineVersion.V4, 4)]
    [InlineData(ZMachineVersion.V5, 4)]
    [InlineData(ZMachineVersion.V6, 8)]
    [InlineData(ZMachineVersion.V7, 8)]
    [InlineData(ZMachineVersion.V8, 8)]
    public void ScalesTheFileLengthByVersion(ZMachineVersion version, int scale)
    {
        // [zm 11.1.6] The stored word is the length divided by 2, 4, or 8.
        // A stored value of 16 keeps every version's real length inside
        // the 128 byte test file.
        var bytes = Story(version);
        PutWord(bytes, 0x1A, 16);

        Assert.Equal(16 * scale, Header(bytes).FileLength);
    }

    [Fact]
    public void InitialProgramCounterIsNotAvailableInVersion6()
    {
        var header = Header(Story(ZMachineVersion.V6));

        // [zm 11.1] In Version 6 the word at $06 is a packed address of
        // the main routine instead.
        Assert.Throws<InvalidOperationException>(() => header.InitialProgramCounter);
        Assert.Equal(0, header.MainRoutinePackedAddress);
    }

    [Theory]
    [InlineData(ZMachineVersion.V5)]
    [InlineData(ZMachineVersion.V7)]
    [InlineData(ZMachineVersion.V8)]
    public void MainRoutinePackedAddressIsOnlyAvailableInVersion6(ZMachineVersion version)
    {
        // [zm 1.2.4] Versions 7 and 8 follow Version 5 here, so the
        // Version 6 special case does not spread to them.
        var header = Header(Story(version));

        Assert.Throws<InvalidOperationException>(() => header.MainRoutinePackedAddress);
        Assert.Equal(0, header.InitialProgramCounter);
    }

    [Fact]
    public void FontWidthAndHeightSwapBytesInVersion6()
    {
        // [zm 11.1] Version 5 stores width at $26 and height at $27, and
        // Version 6 stores them the other way round.
        var v5 = Story(ZMachineVersion.V5);
        v5[0x26] = 8;
        v5[0x27] = 16;

        var v6 = Story(ZMachineVersion.V6);
        v6[0x26] = 16;
        v6[0x27] = 8;

        Assert.Equal(8, Header(v5).FontWidthUnits);
        Assert.Equal(16, Header(v5).FontHeightUnits);
        Assert.Equal(8, Header(v6).FontWidthUnits);
        Assert.Equal(16, Header(v6).FontHeightUnits);
    }

    [Fact]
    public void Flags1UsesTheEarlyLayoutBeforeVersion4()
    {
        var bytes = Story(ZMachineVersion.V3);
        bytes[0x01] = 0b0011_0010; // bits 1, 4, 5

        var header = Header(bytes);

        var flags = header.Flags1Versions1To3;
        Assert.True(flags.HasFlag(Flags1Versions1To3.TimeStatusLine));
        Assert.True(flags.HasFlag(Flags1Versions1To3.StatusLineUnavailable));
        Assert.True(flags.HasFlag(Flags1Versions1To3.ScreenSplittingAvailable));
        Assert.False(flags.HasFlag(Flags1Versions1To3.Tandy));

        // [zm 11.1.4] The later layout is meaningless for this version.
        Assert.Throws<InvalidOperationException>(() => header.Flags1FromVersion4);
    }

    [Theory]
    [InlineData(ZMachineVersion.V4)]
    [InlineData(ZMachineVersion.V8)]
    public void Flags1UsesTheLaterLayoutFromVersion4(ZMachineVersion version)
    {
        var bytes = Story(version);
        bytes[0x01] = 0b1001_1101; // bits 0, 2, 3, 4, 7

        var header = Header(bytes);

        var flags = header.Flags1FromVersion4;
        Assert.True(flags.HasFlag(Flags1FromVersion4.ColorsAvailable));
        Assert.True(flags.HasFlag(Flags1FromVersion4.BoldfaceAvailable));
        Assert.True(flags.HasFlag(Flags1FromVersion4.ItalicAvailable));
        Assert.True(flags.HasFlag(Flags1FromVersion4.FixedSpaceAvailable));
        Assert.True(flags.HasFlag(Flags1FromVersion4.TimedInputAvailable));
        Assert.False(flags.HasFlag(Flags1FromVersion4.PicturesAvailable));

        Assert.Throws<InvalidOperationException>(() => header.Flags1Versions1To3);
    }

    [Fact]
    public void Flags2NumbersBitsAcrossTheWholeWord()
    {
        // [zm 11.1.2] Bit 8 is bit 0 of the first byte, so the word is
        // read big-endian and the numbering is the word's own.
        var bytes = Story(ZMachineVersion.V6);
        bytes[0x10] = 0b0000_0001; // bit 8
        bytes[0x11] = 0b0001_0001; // bits 0 and 4

        var flags = Header(bytes).Flags2;

        Assert.True(flags.HasFlag(Flags2.WantsMenus));
        Assert.True(flags.HasFlag(Flags2.Transcripting));
        Assert.True(flags.HasFlag(Flags2.WantsUndo));
        Assert.False(flags.HasFlag(Flags2.WantsPictures));
    }

    [Fact]
    public void ExtensionWordsReadAsZeroWhenThereIsNoTable()
    {
        // [zm 11.1.7.1]
        var header = Header(Story(ZMachineVersion.V5));

        Assert.Equal(0, header.HeaderExtensionTableAddress);
        Assert.Equal(0, header.ReadExtensionWord(3));
        Assert.Equal(Flags3.None, header.Flags3);
    }

    [Fact]
    public void ExtensionWordsBeyondTheTableReadAsZero()
    {
        // A table at $40 saying two words follow, holding mouse X and Y.
        var bytes = Story(ZMachineVersion.V5);
        PutWord(bytes, 0x36, 0x0040);
        PutWord(bytes, 0x40, 2);
        PutWord(bytes, 0x42, 17);
        PutWord(bytes, 0x44, 23);

        var header = Header(bytes);

        Assert.Equal(2, header.ReadExtensionWord(0));
        Assert.Equal(17, header.MouseX);
        Assert.Equal(23, header.MouseY);

        // [zm 11.1.7.1] Word 3 is past the two the table declares.
        Assert.Equal(0, header.UnicodeTranslationTableAddress);
        Assert.Equal(Flags3.None, header.Flags3);
    }

    [Fact]
    public void ExtensionWritesBeyondTheTableDoNothing()
    {
        var bytes = Story(ZMachineVersion.V5);
        PutWord(bytes, 0x36, 0x0040);
        PutWord(bytes, 0x40, 1);
        PutWord(bytes, 0x42, 5);

        var header = Header(bytes);

        header.WriteExtensionWord(1, 99);
        Assert.Equal(99, header.MouseX);

        // [zm 11.1.7.2] Writing word 2 of a one word table is ignored, and
        // the bytes where it would have landed stay untouched.
        header.WriteExtensionWord(2, 0xFFFF);
        Assert.Equal(0, header.ReadExtensionWord(2));
        Assert.Equal(0, bytes[0x44]);
        Assert.Equal(0, bytes[0x45]);
    }

    [Fact]
    public void ExtensionWritesWithNoTableDoNothing()
    {
        var header = Header(Story(ZMachineVersion.V5));

        // [zm 11.1.7.2] Nothing to write to, nothing happens.
        header.WriteExtensionWord(1, 99);
        Assert.Equal(0, header.MouseX);
    }

    [Theory]
    [InlineData(ZMachineVersion.V1, 0x1234, 0x2468)]
    [InlineData(ZMachineVersion.V3, 0x1234, 0x2468)]
    [InlineData(ZMachineVersion.V4, 0x1234, 0x48D0)]
    [InlineData(ZMachineVersion.V5, 0x1A2B, 0x68AC)]
    [InlineData(ZMachineVersion.V8, 0x1234, 0x91A0)]
    public void UnpacksAddressesWithoutOffsets(ZMachineVersion version, int packed, int expected)
    {
        // [zm 1.2.3] Outside Versions 6 and 7 the same formula serves
        // routines and strings. The Version 5 case is the worked example
        // from the standard's editor's note.
        var header = Header(Story(version));

        Assert.Equal(expected, header.UnpackRoutineAddress((ushort)packed));
        Assert.Equal(expected, header.UnpackStringAddress((ushort)packed));
    }

    [Theory]
    [InlineData(ZMachineVersion.V6)]
    [InlineData(ZMachineVersion.V7)]
    public void UnpacksRoutinesAndStringsSeparatelyInVersions6And7(ZMachineVersion version)
    {
        // [zm 1.2.3] B = 4P + 8 * R_O for routines and 4P + 8 * S_O for
        // strings, where both offsets are stored already divided by 8.
        var bytes = Story(version);
        PutWord(bytes, 0x28, 0x0100); // routines offset
        PutWord(bytes, 0x2A, 0x0200); // strings offset

        var header = Header(bytes);

        Assert.Equal((0x10 * 4) + (0x0100 * 8), header.UnpackRoutineAddress(0x10));
        Assert.Equal((0x10 * 4) + (0x0200 * 8), header.UnpackStringAddress(0x10));
    }

    [Fact]
    public void ComputesTheChecksumOverTheDeclaredLengthOnly()
    {
        // [zm op:verify] Sum from $40 to the declared length, mod $10000,
        // ignoring anything after the declared length.
        var bytes = Story(ZMachineVersion.V3, length: 128);
        PutWord(bytes, 0x1A, 40); // declared length 80, so bytes $40 to $4F count

        for (var i = 0x40; i < 0x50; i++)
        {
            bytes[i] = 0xFF;
        }

        // Padding past the declared length must not affect the sum.
        for (var i = 0x50; i < 0x80; i++)
        {
            bytes[i] = 0xAA;
        }

        var header = Header(bytes);
        Assert.Equal(16 * 0xFF, header.ComputeChecksum());

        PutWord(bytes, 0x1C, 16 * 0xFF);
        Assert.True(header.VerifyChecksum());

        PutWord(bytes, 0x1C, 1);
        Assert.False(header.VerifyChecksum());
    }

    [Fact]
    public void ChecksumTruncatesToSixteenBits()
    {
        var bytes = Story(ZMachineVersion.V5, length: 1024);
        PutWord(bytes, 0x1A, 256); // declared length 1024

        for (var i = 0x40; i < 1024; i++)
        {
            bytes[i] = 0xFF;
        }

        // 960 bytes of $FF is $3BC40, and modulo $10000 that is $BC40.
        Assert.Equal(0xBC40, Header(bytes).ComputeChecksum());
    }

    [Fact]
    public void EarlyFilesWithNoLengthCannotBeVerified()
    {
        // [zm 11.1] Some early Version 3 files carry no length or checksum.
        var header = Header(Story(ZMachineVersion.V3));

        Assert.False(header.HasFileLength);
        Assert.Throws<InvalidOperationException>(() => header.ComputeChecksum());
        Assert.Throws<InvalidOperationException>(() => header.VerifyChecksum());
    }

    [Fact]
    public void ReadsTheConventionalTextFields()
    {
        var bytes = Story(ZMachineVersion.V5);
        "6.11"u8.CopyTo(bytes.AsSpan(0x3C));

        var header = Header(bytes);

        Assert.Equal("6.11", header.InformVersion);

        // [zm 11.1] The eight user name bytes at $38 overlap the four
        // Inform version bytes at $3C, so setting one shows up in the
        // other. That overlap is real and this pins it down.
        Assert.Equal("\0\0\0\0" + "6.11", header.UserName);
    }

    [Fact]
    public void ReadsTheInterpreterFields()
    {
        var bytes = Story(ZMachineVersion.V5);
        bytes[0x1E] = 6;
        bytes[0x1F] = (byte)'A';
        bytes[0x32] = 1;
        bytes[0x33] = 1;

        var header = Header(bytes);

        Assert.Equal(InterpreterNumber.IbmPc, header.InterpreterNumber);
        Assert.Equal((byte)'A', header.InterpreterVersion);
        Assert.Equal(1, header.StandardRevisionMajor);
        Assert.Equal(1, header.StandardRevisionMinor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void RejectsVersionsOutsideOneToEight(byte version)
    {
        var bytes = Story(ZMachineVersion.V3);
        bytes[0x00] = version;

        Assert.Throws<InvalidDataException>(() => Header(bytes));
    }

    [Fact]
    public void RejectsDynamicMemorySmallerThanTheHeader()
    {
        // [zm 1.1] Dynamic memory must contain at least 64 bytes.
        var bytes = Story(ZMachineVersion.V3);
        PutWord(bytes, 0x0E, 0x0020);

        Assert.Throws<InvalidDataException>(() => Header(bytes));
    }

    [Fact]
    public void RejectsStaticMemoryStartingPastTheEndOfTheFile()
    {
        var bytes = Story(ZMachineVersion.V3, length: 128);
        PutWord(bytes, 0x0E, 0x0100);
        PutWord(bytes, 0x04, 0x0100);

        Assert.Throws<InvalidDataException>(() => Header(bytes));
    }

    [Fact]
    public void RejectsHighMemoryOverlappingDynamicMemory()
    {
        // [zm 1.1] High memory may overlap static memory but never dynamic.
        var bytes = Story(ZMachineVersion.V3);
        PutWord(bytes, 0x0E, 0x0060);
        PutWord(bytes, 0x04, 0x0050);

        Assert.Throws<InvalidDataException>(() => Header(bytes));
    }

    [Fact]
    public void RejectsADeclaredLengthLongerThanTheFile()
    {
        var bytes = Story(ZMachineVersion.V3, length: 128);
        PutWord(bytes, 0x1A, 100); // declares 200 bytes

        Assert.Throws<InvalidDataException>(() => Header(bytes));
    }

    [Fact]
    public void ReflectsChangesToMemoryRatherThanSnapshotting()
    {
        // [zm 11.1.1] Some header fields legally change during play, so
        // the view has to read through rather than copy.
        var bytes = Story(ZMachineVersion.V3);
        var header = Header(bytes);

        Assert.Equal(Flags2.None, header.Flags2);

        bytes[0x11] |= 0x01;
        Assert.True(header.Flags2.HasFlag(Flags2.Transcripting));
    }
}

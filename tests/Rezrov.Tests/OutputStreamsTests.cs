using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Streams;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

/// <summary>
/// The output streams of section 7 driven directly: which stream gets
/// what, the memory stream's nesting, the transcript's agreement with
/// the header, and the record of commands.
/// </summary>
public class OutputStreamsTests
{
    private const int Table = 0x300;

    [Fact]
    public void TextGoesToTheScreenUntilStreamOneIsDeselected()
    {
        var (streams, screen, _, _, _) = Make();

        Print(streams, "shown");
        streams.Select(-1, 0);
        Print(streams, "hidden");
        streams.Select(1, 0);
        Print(streams, "back");
        streams.Flush();

        // [zm op:output_stream] Negative deselects, positive selects.
        Assert.Equal("shownback", screen.Text);
    }

    [Fact]
    public void StreamThreeWritesIntoATableAndStopsEverythingElse()
    {
        var (streams, screen, _, memory, _) = Make();

        Print(streams, "before ");
        Assert.Equal(StreamSelection.Selected, streams.Select(3, Table));
        Print(streams, "in memory");
        streams.Print(Zscii.Newline);
        Assert.Equal(StreamSelection.Selected, streams.Select(-3, 0));
        Print(streams, "after");
        streams.Flush();

        // [zm 7.1.2.1] The first word holds the count and the characters
        // follow; [zm 7.1.2.2.1] a newline is ZSCII 13; [zm 7.1.2.2]
        // nothing reached the screen meanwhile.
        Assert.Equal(10, memory.ReadWord(Table));
        Assert.Equal("in memory\r", Ascii(memory, Table + 2, 10));
        Assert.Equal("before after", screen.Text);
    }

    [Fact]
    public void StreamThreeNestsAndResumesTheOuterTable()
    {
        // [zm 7.1.2.1.1] and the remarks: Inform checks an object's name
        // for "a" against "an" by printing it to memory, which may itself
        // print to memory.
        var (streams, _, _, memory, _) = Make();

        streams.Select(3, Table);
        Print(streams, "outer ");
        streams.Select(3, Table + 0x40);
        Print(streams, "inner");
        streams.Select(-3, 0);
        Print(streams, "again");
        streams.Select(-3, 0);

        Assert.Equal(11, memory.ReadWord(Table));
        Assert.Equal("outer again", Ascii(memory, Table + 2, 11));
        Assert.Equal(5, memory.ReadWord(Table + 0x40));
        Assert.Equal("inner", Ascii(memory, Table + 0x42, 5));
        Assert.Equal(0, streams.MemoryDepth);
    }

    [Fact]
    public void StreamThreeCannotNestSeventeenDeep()
    {
        var (streams, _, _, _, _) = Make();

        for (var i = 0; i < OutputStreams.MaxMemoryDepth; i++)
        {
            Assert.Equal(StreamSelection.Selected, streams.Select(3, (ushort)(Table + (i * 4))));
        }

        // [zm 7.1.2.1.1]
        Assert.Equal(StreamSelection.TooDeep, streams.Select(3, Table));
        Assert.Equal(16, streams.MemoryDepth);
    }

    [Fact]
    public void UnicodeInStreamThreeBecomesZsciiOrAQuestionMark()
    {
        // [zm 7.5.3] e-acute is 170 in the default table; the euro sign
        // has no code.
        var (streams, _, _, memory, _) = Make();

        streams.Select(3, Table);
        streams.PrintUnicode('é');
        streams.PrintUnicode('€');
        streams.Select(-3, 0);

        Assert.Equal(2, memory.ReadWord(Table));
        Assert.Equal(170, memory.ReadByte(Table + 2));
        Assert.Equal((byte)'?', memory.ReadByte(Table + 3));
    }

    [Fact]
    public void TheTranscriptGetsLowerWindowTextAndEchoedInput()
    {
        var (streams, _, model, _, files) = Make();
        var transcript = new StringWriter();
        files.Transcript = transcript;

        Assert.Equal(StreamSelection.Selected, streams.Select(2, 0));
        Print(streams, "You are in a maze.");
        streams.Print(Zscii.Newline);
        streams.EchoInput([(ushort)'g', (ushort)'o'], Zscii.Newline);

        // Text printed in the upper window stays out of it.
        model.SplitWindow(1);
        model.SetWindow(ScreenModel.Upper);
        Print(streams, "Status");
        model.SetWindow(ScreenModel.Lower);
        streams.Flush();

        // [zm 7.1.1] and [zm 7.1.1.1]
        Assert.Equal("You are in a maze.\ngo\n", transcript.ToString());
        Assert.True(streams.TranscriptSelected);
    }

    [Fact]
    public void TheTranscriptIsAskedForOnlyOncePerSession()
    {
        // [zm 7.1.1.2] A Mind Forever Voyaging turns it off and on again
        // repeatedly.
        var (streams, _, _, _, files) = Make();
        files.Transcript = new StringWriter();

        streams.Select(2, 0);
        streams.Select(-2, 0);
        streams.Select(2, 0);
        streams.Select(-2, 0);
        streams.Select(2, 0);

        Assert.Equal(1, files.TranscriptRequests);
        Assert.True(streams.TranscriptSelected);
    }

    [Fact]
    public void TheTranscriptBitFollowsTheStreamAndTheStreamFollowsTheBit()
    {
        var (streams, _, _, memory, files) = Make();
        var header = new StoryHeader(memory);
        files.Transcript = new StringWriter();

        // [zm 7.4] Selecting the stream sets bit 0 of Flags 2.
        streams.Select(2, 0);
        Assert.True(header.Flags2.HasFlag(Flags2.Transcripting));
        streams.Select(-2, 0);
        Assert.False(header.Flags2.HasFlag(Flags2.Transcripting));

        // [zm 7.3] The game may set the bit itself.
        header.Flags2 |= Flags2.Transcripting;
        Assert.True(streams.SyncWithHeader());
        Assert.True(streams.TranscriptSelected);

        header.Flags2 &= ~Flags2.Transcripting;
        Assert.True(streams.SyncWithHeader());
        Assert.False(streams.TranscriptSelected);
    }

    [Fact]
    public void WithoutAFileTheTranscriptStaysOffAndTheBitIsCleared()
    {
        // [zm 7.6.5] and [zm 11.1.2.1] The bit must reflect the truth.
        var (streams, _, _, memory, _) = Make();
        var header = new StoryHeader(memory);

        Assert.Equal(StreamSelection.Unavailable, streams.Select(2, 0));
        Assert.False(streams.TranscriptSelected);
        Assert.False(header.Flags2.HasFlag(Flags2.Transcripting));

        header.Flags2 |= Flags2.Transcripting;
        Assert.False(streams.SyncWithHeader());
        Assert.False(header.Flags2.HasFlag(Flags2.Transcripting));
    }

    [Fact]
    public void StreamFourRecordsCommandsAndKeysInTheCommandFileFormat()
    {
        var (streams, _, _, _, files) = Make();
        var record = new StringWriter();
        files.Record = record;

        Assert.Equal(StreamSelection.Selected, streams.Select(4, 0));
        Print(streams, "printed text is not recorded");
        streams.RecordCommand("turn it on.".Select(c => (ushort)c).ToList(), Zscii.Keypad9);
        streams.RecordCommand("look".Select(c => (ushort)c).ToList(), Zscii.Newline);
        streams.RecordCommand("look unde".Select(c => (ushort)c).ToList(), 0);
        streams.RecordCommand([(ushort)'[', 170], Zscii.Newline);
        streams.RecordKey('y');
        streams.RecordKey(Zscii.Newline);
        streams.RecordKey(Zscii.CursorUp);
        streams.Select(-4, 0);
        streams.RecordKey('n');

        // [zm 7.1.2.3] and the remarks on section 7: the style Frotz
        // adopted, which CommandFile reads back.
        Assert.Equal("turn it on.[154]\nlook\nlook unde[0]\n[91][170]\ny\n\n[129]\n", record.ToString());
    }

    [Fact]
    public void UnknownStreamsAreReportedAndZeroDoesNothing()
    {
        var (streams, _, _, _, _) = Make();

        Assert.Equal(StreamSelection.Unknown, streams.Select(5, 0));
        Assert.Equal(StreamSelection.Unknown, streams.Select(-7, 0));
        Assert.Equal(StreamSelection.Selected, streams.Select(0, 0));
        Assert.True(streams.ScreenSelected);
    }

    [Fact]
    public void ResetEmptiesTheMemoryNestingButKeepsTheTranscript()
    {
        var (streams, _, _, _, files) = Make();
        files.Transcript = new StringWriter();
        streams.Select(2, 0);
        streams.Select(3, Table);
        streams.Select(-1, 0);

        streams.Reset();

        // [zm 6.1.3] and [zm 11.1.2.1]
        Assert.Equal(0, streams.MemoryDepth);
        Assert.True(streams.ScreenSelected);
        Assert.True(streams.TranscriptSelected);
    }

    private static (OutputStreams Streams, RecordingScreen Screen, ScreenModel Model, ZMemory Memory, ScriptedFiles Files) Make(
        ZMachineVersion version = ZMachineVersion.V5)
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
        var header = new StoryHeader(memory);
        var screen = new RecordingScreen();
        var model = new ScreenModel(screen, header, memory);
        var files = new ScriptedFiles();
        var streams = new OutputStreams(model, new GameState(memory, header), header, UnicodeTranslationTable.Default, files);
        return (streams, screen, model, memory, files);
    }

    private static void PutWord(byte[] bytes, int address, int value)
    {
        bytes[address] = (byte)(value >> 8);
        bytes[address + 1] = (byte)value;
    }

    private static void Print(OutputStreams streams, string text)
    {
        foreach (var c in text)
        {
            streams.Print(c);
        }
    }

    private static string Ascii(ZMemory memory, int address, int length) =>
        string.Concat(memory.Slice(address, length).ToArray().Select(b => (char)b));
}

using Rezrov.Glulx;
using Rezrov.Glulx.Glk;
using FileMode = Rezrov.Glulx.Glk.FileMode;

namespace Rezrov.Tests;

/// <summary>
/// [glk #stream] Streams other than a window's: memory streams, the
/// current stream, and the character case functions.
/// </summary>
public class GlkStreamTests
{
    private const uint Buffer = 0x400;

    private static (GlkLibrary Glk, GlulxMemory Memory) Library()
    {
        var memory = new GlulxMemory(TestGlulx.File(ramStart: 0x400, extStart: 0x800, endMem: 0xA00));
        return (new GlkLibrary(new RecordingGlkDisplay()), memory);
    }

    [Fact]
    public void AMemoryStreamWritesIntoMemoryAndCountsEverything()
    {
        var (glk, memory) = Library();
        var stream = glk.OpenMemoryStream(memory, Buffer, 4, false, FileMode.Write, 5)!;

        foreach (var character in "abcdef")
        {
            glk.PutChar(stream, character);
        }

        glk.PutChar(stream, 0x3B1);

        // [glk #memory-streams] Four characters fit, the rest are thrown
        // away but counted, and the position stays at the end.
        Assert.Equal("abcd", System.Text.Encoding.Latin1.GetString(memory.Slice(Buffer, 4)));
        Assert.Equal(0, memory.ReadByte(Buffer + 4));
        Assert.Equal(7u, stream.WriteCount);
        Assert.Equal(4u, stream.Position);
        Assert.Equal(5u, stream.Rock);
        Assert.False(stream.Readable);

        var counts = glk.CloseStream(stream);
        Assert.Equal((0u, 7u), counts);
        Assert.Equal(0, glk.Streams.Count);
    }

    [Fact]
    public void UnicodeInAByteBufferIsAQuestionMark()
    {
        var (glk, memory) = Library();
        var bytes = glk.OpenMemoryStream(memory, Buffer, 4, false, FileMode.Write, 0)!;
        var words = glk.OpenMemoryStream(memory, Buffer + 0x10, 4, true, FileMode.Write, 0)!;

        glk.PutChar(bytes, 0x3B1);
        glk.PutChar(words, 0x3B1);

        // [glk #memory-streams] A byte buffer cannot hold it; a word
        // buffer can.
        Assert.Equal((byte)'?', memory.ReadByte(Buffer));
        Assert.Equal(0x3B1u, memory.ReadWord(Buffer + 0x10));
    }

    [Fact]
    public void AMemoryStreamReadsBackAndReachesTheEnd()
    {
        var (glk, memory) = Library();
        memory.WriteByte(Buffer, (byte)'h');
        memory.WriteByte(Buffer + 1, (byte)'i');
        var stream = glk.OpenMemoryStream(memory, Buffer, 2, false, FileMode.Read, 0)!;

        Assert.Equal('h', stream.GetChar());
        Assert.Equal('i', stream.GetChar());
        Assert.Equal(-1, stream.GetChar());
        Assert.Equal(2u, stream.ReadCount);
        Assert.False(stream.Writable);

        // [glk #stream_positions] Back to the start, from the end, and
        // never outside the buffer.
        stream.SetPosition(0, SeekMode.Start);
        Assert.Equal('h', stream.GetChar());
        stream.SetPosition(-1, SeekMode.End);
        Assert.Equal('i', stream.GetChar());
        stream.SetPosition(-5, SeekMode.Current);
        Assert.Equal(0u, stream.Position);
        stream.SetPosition(9, SeekMode.Start);
        Assert.Equal(2u, stream.Position);
    }

    [Fact]
    public void ANullBufferTakesNothingAndGivesTheEnd()
    {
        var (glk, memory) = Library();
        var stream = glk.OpenMemoryStream(memory, 0, 100, false, FileMode.ReadWrite, 0)!;

        glk.PutChar(stream, 'x');
        Assert.Equal(1u, stream.WriteCount);
        Assert.Equal(-1, stream.GetChar());
    }

    [Fact]
    public void PrintingNeedsACurrentOutputStream()
    {
        var (glk, memory) = Library();

        glk.PutChar('x');
        Assert.Contains(glk.Warnings, w => w.Contains("no current output stream", StringComparison.Ordinal));

        var input = glk.OpenMemoryStream(memory, Buffer, 4, false, FileMode.Read, 0)!;
        glk.PutChar(input, 'x');
        Assert.Contains(glk.Warnings, w => w.Contains("not an output stream", StringComparison.Ordinal));
        Assert.Equal(0u, input.WriteCount);
    }

    [Fact]
    public void ClosingTheCurrentStreamLeavesNone()
    {
        var (glk, memory) = Library();
        var stream = glk.OpenMemoryStream(memory, Buffer, 4, false, FileMode.Write, 0)!;
        glk.SetCurrentStream(stream);

        glk.CloseStream(stream);

        // [glk #stream_close]
        Assert.Null(glk.CurrentStream);
    }

    [Fact]
    public void AWindowStreamIsNotClosedByStreamClose()
    {
        var (glk, _) = Library();
        var window = glk.OpenWindow(null, 0, 0, WindowType.TextBuffer, 1)!;

        glk.CloseStream(window.Stream);

        Assert.Equal(1, glk.Streams.Count);
        Assert.Contains(glk.Warnings, w => w.Contains("window stream", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData((byte)'A', (byte)'a')]
    [InlineData((byte)'Z', (byte)'z')]
    [InlineData(0xC0, 0xE0)]
    [InlineData(0xD6, 0xF6)]
    [InlineData(0xD8, 0xF8)]
    [InlineData(0xDE, 0xFE)]
    public void LatinOneCasingFollowsTheParallelRanges(byte upper, byte lower)
    {
        // [glk #encoding_hilo] Upper to lower adds 20, lower to upper
        // subtracts it, across all three ranges.
        Assert.Equal(lower, GlkLibrary.CharToLower(upper));
        Assert.Equal(upper, GlkLibrary.CharToUpper(lower));
    }

    [Theory]
    [InlineData((byte)'a')]
    [InlineData((byte)'1')]
    [InlineData(0xD7)]
    [InlineData(0xF7)]
    [InlineData(0xFF)]
    public void LatinOneCasingLeavesOtherCharactersAlone(byte character)
    {
        // [glk #encoding_hilo] A character already in the case asked for,
        // or not a letter at all, comes back unchanged: the multiplication
        // and division signs sit between the ranges, and y with diaeresis
        // has no upper case in Latin-1.
        Assert.Equal(character, GlkLibrary.CharToLower(character));
        Assert.Equal(character == (byte)'a' ? (byte)'A' : character, GlkLibrary.CharToUpper(character));
    }
}

using System.Text;
using Rezrov.Core.Blorb;
using Rezrov.Glulx.Glk;
using Rezrov.Glulx.Instructions;
using static Rezrov.Tests.GlulxAssembler;

namespace Rezrov.Tests;

/// <summary>
/// [glk #resource_streams] Streams that read the resource file's data
/// chunks: text and binary, bytes and Unicode, forms kept whole, and
/// what there is to read without a resource file.
/// </summary>
public class GlkResourceStreamTests
{
    private const uint StreamOpenResource = 0x0049;
    private const uint GetCharStream = 0x0090;

    private static readonly byte[] Form = [.. "IFZS"u8, .. "IFhd"u8, 0, 0, 0, 2, 0x12, 0x34];

    private static BlorbFile Resources() => BlorbFile.Read(TestBlorb.Build(
        [(9, "PNG ", TestBlorb.Png(1, 1))],
        data:
        [
            (1, "TEXT", [.. "hé"u8, 0x0A]),
            (2, "BINA", [0, 0, 0x03, 0xB1, 0, 0, 0, 0x62]),
            (3, "FORM", Form),
            (4, "JPEG", [1, 2, 3]),
        ]));

    private static GlkLibrary Library(BlorbFile? resources) => new(new RecordingGlkDisplay()) { Resources = resources };

    private static string ReadAll(GlkLibrary glk, GlkStream stream)
    {
        var text = new StringBuilder();
        for (var character = glk.GetChar(stream); character >= 0; character = glk.GetChar(stream))
        {
            text.Append(char.ConvertFromUtf32(character));
        }

        return text.ToString();
    }

    [Fact]
    public void ATextChunkIsLatin1AsBytesAndUtf8AsUnicode()
    {
        var glk = Library(Resources());

        // [glk #resource_streams] The same bytes: Latin-1, one per
        // character, through a byte stream, and UTF-8 through a Unicode
        // one, and a Unix newline is kept as it is.
        var bytes = glk.OpenResourceStream(1, false, 5)!;
        Assert.Equal("hÃ©\n", ReadAll(glk, bytes));
        Assert.Equal(4u, bytes.ReadCount);
        Assert.Equal(5u, bytes.Rock);
        Assert.Equal(1u, bytes.Number);
        Assert.True(bytes.IsText);
        Assert.True(bytes.Readable);
        Assert.False(bytes.Writable);

        var unicode = glk.OpenResourceStream(1, true, 0)!;
        Assert.Equal("hé\n", ReadAll(glk, unicode));
        Assert.Equal(3u, unicode.ReadCount);
    }

    [Fact]
    public void ABinaryChunkIsBytesOrBigEndianWords()
    {
        var glk = Library(Resources());

        var bytes = glk.OpenResourceStream(2, false, 0)!;
        Assert.Equal("\0\0±\0\0\0b", ReadAll(glk, bytes));
        Assert.False(bytes.IsText);

        // [glk #resource_streams] Four-byte words, and positions count
        // them.
        var words = glk.OpenResourceStream(2, true, 0)!;
        Assert.Equal("αb", ReadAll(glk, words));
        Assert.Equal(2u, words.Position);
        words.SetPosition(1, SeekMode.Start);
        Assert.Equal('b', glk.GetChar(words));
    }

    [Fact]
    public void AFormChunkIsReadFromItsHeader()
    {
        var glk = Library(Resources());

        // [glk #resource_streams] A FORM resource begins at its FORM
        // header, so an embedded file comes out whole, and it counts
        // as binary.
        var stream = glk.OpenResourceStream(3, false, 0)!;
        var expected = "FORM\0\0\0" + (char)Form.Length + Encoding.Latin1.GetString(Form);
        Assert.Equal(expected, ReadAll(glk, stream));
        Assert.False(stream.IsText);
    }

    [Fact]
    public void ThereIsNothingToOpenWithoutTheResource()
    {
        var glk = Library(Resources());

        // [glk op:stream_open_resource] No resource of the number, a
        // picture's number, or a chunk of a kind that is not data, and
        // no resource file at all.
        Assert.Null(glk.OpenResourceStream(7, false, 0));
        Assert.Null(glk.OpenResourceStream(9, false, 0));
        Assert.Null(glk.OpenResourceStream(4, false, 0));
        Assert.Null(Library(null).OpenResourceStream(1, false, 0));
        Assert.Equal(0, glk.Streams.Count);
        Assert.Empty(glk.Warnings);
    }

    [Fact]
    public void AResourceStreamCannotBeWrittenAndClosesLikeAnyOther()
    {
        var glk = Library(Resources());
        var stream = glk.OpenResourceStream(1, false, 0)!;

        glk.PutChar(stream, 'x');
        Assert.Single(glk.Warnings);
        Assert.Equal('h', glk.GetChar(stream));

        Assert.Equal((1u, 0u), glk.CloseStream(stream));
        Assert.Equal(0, glk.Streams.Count);
    }

    [Fact]
    public void GestaltPromisesResourceStreams()
    {
        Assert.Equal(1u, GlkLibrary.Gestalt((uint)GestaltSelector.ResourceStream, 0, null));
    }

    [Fact]
    public void AGameReadsAResourceThroughTheOpcode()
    {
        var code = new GlulxAssembler().Function("main").Op(Opcode.SetIOSys, C(2), C(0));
        Call(code, StreamOpenResource, Ram(0), C(1), C(0));
        Call(code, GetCharStream, Ram(4), Ram(0));
        Call(code, StreamOpenResource, Ram(8), C(7), C(0));
        code.Return(C(0));

        var glk = Library(Resources());
        var machine = GlulxRun.Run(code, glk: glk);

        Assert.NotEqual(0u, machine.Ram(0));
        Assert.Equal('h', machine.Ram(4));
        Assert.Equal(0u, machine.Ram(8));
    }

    /// <summary>
    /// [glulx op:glk] Pushes the arguments last first and calls the
    /// function, storing its result.
    /// </summary>
    private static GlulxAssembler Call(GlulxAssembler code, uint selector, Arg result, params Arg[] args)
    {
        for (var i = args.Length - 1; i >= 0; i--)
        {
            code.Op(Opcode.Copy, args[i], Sp);
        }

        return code.Op(Opcode.Glk, C(selector), C(args.Length), result);
    }
}

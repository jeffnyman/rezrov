using Rezrov.Glulx;
using Rezrov.Glulx.Instructions;
using static Rezrov.Tests.GlulxAssembler;

namespace Rezrov.Tests;

/// <summary>
/// [glulx #searching] The three search opcodes over structures laid out
/// in ROM by the test assembler.
/// </summary>
public class GlulxSearchTests
{
    private const uint KeyIndirect = 1;
    private const uint ZeroKeyTerminates = 2;
    private const uint ReturnIndex = 4;

    /// <summary>
    /// Four structures of eight bytes, a word key then a word value,
    /// with keys 3, 7, 10, and 20 in ascending order, and a fifth with a
    /// zero key after them; then a six-byte key and two structures of
    /// ten bytes with six-byte keys.
    /// </summary>
    private static GlulxAssembler Data(GlulxAssembler code) => code
        .Label("array")
        .Word(3).Word(300)
        .Word(7).Word(700)
        .Word(10).Word(1000)
        .Word(20).Word(2000)
        .Word(0).Word(0)
        .Label("key6").Bytes(1, 2, 3, 4, 5, 6)
        .Label("wide")
        .Bytes(1, 2, 3, 4, 5, 5).Word(55)
        .Bytes(1, 2, 3, 4, 5, 6).Word(66);

    /// <summary>
    /// Runs one search and returns its result with the addresses of
    /// the array and the wide structures in the same program.
    /// </summary>
    private static (uint Result, uint Array, uint Wide) Search(Opcode opcode, params Arg[] args)
    {
        var code = new GlulxAssembler().Function("main")
            .Op(opcode, [.. args, Ram(0)])
            .Op(Opcode.Copy, At("array"), Ram(4))
            .Op(Opcode.Copy, At("wide"), Ram(8))
            .Return(C(0));
        var machine = GlulxRun.Run(Data(code));
        return (machine.Ram(0), machine.Ram(4), machine.Ram(8));
    }

    [Fact]
    public void LinearSearchFindsAStructureByAddressOrIndex()
    {
        // [glulx op:linearsearch] The address of the structure found, or
        // its index with ReturnIndex; 0 or -1 for none.
        var found = Search(Opcode.LinearSearch, C(10), C(4), At("array"), C(8), C(4), C(0), C(0));
        Assert.Equal(found.Array + 16, found.Result);
        Assert.Equal(2u, Search(Opcode.LinearSearch, C(10), C(4), At("array"), C(8), C(4), C(0), C(ReturnIndex)).Result);
        Assert.Equal(0u, Search(Opcode.LinearSearch, C(11), C(4), At("array"), C(8), C(4), C(0), C(0)).Result);
        Assert.Equal(0xFFFFFFFFu, Search(Opcode.LinearSearch, C(11), C(4), At("array"), C(8), C(4), C(0), C(ReturnIndex)).Result);
    }

    [Fact]
    public void LinearSearchStopsAtAZeroKeyOrAtTheCount()
    {
        // [glulx op:linearsearch] A count of -1 runs until a match or a
        // zero key; a zero key being looked for is found even so; a
        // count stops the search short.
        var last = Search(Opcode.LinearSearch, C(20), C(4), At("array"), C(8), C(-1), C(0), C(ZeroKeyTerminates));
        Assert.Equal(last.Array + 24, last.Result);

        var zero = Search(Opcode.LinearSearch, C(0), C(4), At("array"), C(8), C(-1), C(0), C(ZeroKeyTerminates));
        Assert.Equal(zero.Array + 32, zero.Result);

        Assert.Equal(0u, Search(Opcode.LinearSearch, C(20), C(4), At("array"), C(8), C(2), C(0), C(0)).Result);
        Assert.Equal(0u, Search(Opcode.LinearSearch, C(99), C(4), At("array"), C(8), C(-1), C(0), C(ZeroKeyTerminates)).Result);
    }

    [Fact]
    public void TheKeyCanBeInMemoryOrTheLowBytesOfAValue()
    {
        // [glulx #searching] KeyIndirect allows a six-byte key; by
        // value, the key is its low 1, 2, or 4 bytes.
        var indirect = Search(Opcode.LinearSearch, At("key6"), C(6), At("wide"), C(10), C(2), C(0), C(KeyIndirect));
        Assert.Equal(indirect.Wide + 10, indirect.Result);

        var two = Search(Opcode.LinearSearch, C(0x0506), C(2), At("wide"), C(10), C(2), C(4), C(0));
        Assert.Equal(two.Wide + 10, two.Result);

        var one = Search(Opcode.LinearSearch, C(0x1105), C(1), At("wide"), C(10), C(2), C(5), C(0));
        Assert.Equal(one.Wide, one.Result);

        Assert.Equal(1u, Search(Opcode.LinearSearch, C(66), C(4), At("wide"), C(10), C(2), C(6), C(ReturnIndex)).Result);
    }

    [Fact]
    public void AKeyByValueMustBeANativeSize()
    {
        var e = Assert.Throws<GlulxException>(() => Search(Opcode.LinearSearch, C(1), C(3), At("array"), C(8), C(4), C(0), C(0)));
        Assert.Contains("1, 2, or 4 bytes", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3u, 0u)]
    [InlineData(7u, 1u)]
    [InlineData(10u, 2u)]
    [InlineData(20u, 3u)]
    [InlineData(0u, 0xFFFFFFFFu)]
    [InlineData(8u, 0xFFFFFFFFu)]
    [InlineData(21u, 0xFFFFFFFFu)]
    public void BinarySearchFindsEveryKeyAndNoOther(uint key, uint index)
    {
        // [glulx op:binarysearch] Over the four sorted structures.
        Assert.Equal(index, Search(Opcode.BinarySearch, C(key), C(4), At("array"), C(8), C(4), C(0), C(ReturnIndex)).Result);

        var byAddress = Search(Opcode.BinarySearch, C(key), C(4), At("array"), C(8), C(4), C(0), C(0));
        var expected = index == 0xFFFFFFFF ? 0 : byAddress.Array + (8 * index);
        Assert.Equal(expected, byAddress.Result);
    }

    [Fact]
    public void BinarySearchTakesAnIndirectKeyOfAnyLength()
    {
        var found = Search(Opcode.BinarySearch, At("key6"), C(6), At("wide"), C(10), C(2), C(0), C(KeyIndirect));
        Assert.Equal(found.Wide + 10, found.Result);
    }

    [Fact]
    public void LinkedSearchFollowsTheNextFields()
    {
        // Three structures of twelve bytes anywhere in memory: a key
        // word, a value word, and the link, chained c, b, a, z.
        var code = new GlulxAssembler().Function("main")
            .Op(Opcode.LinkedSearch, C(7), C(4), At("c"), C(0), C(8), C(0), Ram(0))
            .Op(Opcode.LinkedSearch, C(9), C(4), At("c"), C(0), C(8), C(0), Ram(4))
            .Op(Opcode.LinkedSearch, C(1), C(4), At("c"), C(0), C(8), C(ZeroKeyTerminates), Ram(8))
            .Op(Opcode.LinkedSearch, C(0), C(4), At("c"), C(0), C(8), C(ZeroKeyTerminates), Ram(12))
            .Op(Opcode.Copy, At("b"), Ram(16))
            .Op(Opcode.Copy, At("z"), Ram(20))
            .Return(C(0))
            .Label("a").Word(3).Word(300).Ref("z")
            .Label("b").Word(7).Word(700).Ref("a")
            .Label("c").Word(10).Word(1000).Ref("b")
            .Label("z").Word(0).Word(0).Word(0);

        var machine = GlulxRun.Run(code);

        // [glulx op:linkedsearch] Found in the second structure of the
        // chain; not found once the zero link is reached; stopped at
        // the zero key before the end; the zero key itself found.
        Assert.Equal(machine.Ram(16), machine.Ram(0));
        Assert.Equal(0u, machine.Ram(4));
        Assert.Equal(0u, machine.Ram(8));
        Assert.Equal(machine.Ram(20), machine.Ram(12));
    }
}

using Rezrov.Glulx.Instructions;

namespace Rezrov.Tests;

public class GlulxOpcodeTableTests
{
    [Fact]
    public void HasEveryOpcodeOfTheDictionaryOnce()
    {
        // [glulx #dictionary-of-opcodes] 150 entries, from nop to jdisinf.
        Assert.Equal(150, OpcodeTable.All.Count);
        Assert.Equal(150, OpcodeTable.All.Select(info => info.Number).Distinct().Count());
        Assert.Equal(150, OpcodeTable.All.Select(info => info.Name).Distinct().Count());
        Assert.Equal(Opcode.Nop, OpcodeTable.All[0].Opcode);
        Assert.Equal(Opcode.JDIsInf, OpcodeTable.All[^1].Opcode);
    }

    [Fact]
    public void TheEnumValueIsTheOpcodeNumber()
    {
        Assert.Equal(0x10u, (uint)Opcode.Add);
        Assert.Equal(0x130u, (uint)Opcode.Glk);
        Assert.Equal(0x239u, (uint)Opcode.JDIsInf);
        Assert.Equal(Opcode.Glk, OpcodeTable.Resolve(0x130)!.Opcode);
        Assert.Null(OpcodeTable.Resolve(0x131));
        Assert.Equal("glk", OpcodeTable.Describe(Opcode.Glk).Name);
    }

    [Theory]
    [InlineData(Opcode.Nop, "")]
    [InlineData(Opcode.Add, "LLS")]
    [InlineData(Opcode.Catch, "SL")]
    [InlineData(Opcode.GetIOSys, "SS")]
    [InlineData(Opcode.FMod, "LLSS")]
    [InlineData(Opcode.LinearSearch, "LLLLLLLS")]
    [InlineData(Opcode.LinkedSearch, "LLLLLLS")]
    [InlineData(Opcode.DAdd, "LLLLSS")]
    [InlineData(Opcode.JDeq, "LLLLLLL")]
    public void OperandListsFollowTheDictionaryHeadings(Opcode opcode, string operands)
    {
        var info = OpcodeTable.Describe(opcode);

        Assert.Equal(operands, info.Operands);
        Assert.Equal(operands.Length, info.OperandCount);
        for (var i = 0; i < operands.Length; i++)
        {
            Assert.Equal(operands[i] == 'S', info.IsStore(i));
        }
    }

    [Fact]
    public void BranchesAreMarkedAndJumpAbsIsNot()
    {
        // [glulx #opcodes_branch] All branches except jumpabs take an
        // offset with the special values 0 and 1.
        var branches = OpcodeTable.All.Where(info => info.Branches).Select(info => info.Name).ToList();

        Assert.Equal(29, branches.Count);
        Assert.All(branches, name => Assert.StartsWith("j", name, StringComparison.Ordinal));
        Assert.Contains("jump", branches);
        Assert.Contains("jfeq", branches);
        Assert.Contains("jdisinf", branches);
        Assert.DoesNotContain("jumpabs", branches);
    }

    [Fact]
    public void CopyShortAndCopyByteHaveNarrowOperands()
    {
        // [glulx #instruction] The indirect modes access 32-bit fields
        // except in copys and copyb.
        Assert.Equal(4, OpcodeTable.Describe(Opcode.Copy).OperandSize);
        Assert.Equal(2, OpcodeTable.Describe(Opcode.CopyS).OperandSize);
        Assert.Equal(1, OpcodeTable.Describe(Opcode.CopyB).OperandSize);
        Assert.Equal(148, OpcodeTable.All.Count(info => info.OperandSize == 4));
        Assert.Equal(4, OpcodeTable.Describe(Opcode.ALoadB).OperandSize);
    }
}

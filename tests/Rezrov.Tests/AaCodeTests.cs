using Rezrov.AaMachine;
using Rezrov.AaMachine.Instructions;

namespace Rezrov.Tests;

/// <summary>
/// [aam opcode] Reading the bytecode of an Aa-machine story.
/// </summary>
public class AaCodeTests
{
    [Fact]
    public void EveryStoryDecodesFromItsFirstInstructionToItsLast()
    {
        var stories = Corpus.AaStoryFiles();
        Assert.SkipUnless(stories.Count > 0, "The entharion submodule is not populated.");

        var failures = new List<string>();
        var used = new HashSet<byte>();

        foreach (var path in stories)
        {
            var name = Path.GetFileName(path);
            var story = AaStory.Read(File.ReadAllBytes(path));
            var code = story.Instructions;

            // [aam story] Address 0 always holds FAIL, so that a jump
            // to nowhere fails at once rather than running into
            // whatever happens to be there.
            Assert.Equal(Opcode.Fail, code.Decode(0).Opcode);

            var boundaries = new HashSet<int>();
            var branches = new List<(int At, int To)>();
            var strings = new List<(int At, int To)>();
            var last = 0;

            foreach (var instruction in code.All())
            {
                boundaries.Add(instruction.Address);
                used.Add(instruction.Info.Code);
                last = instruction.NextAddress;

                foreach (var operand in instruction.Operands)
                {
                    if (operand.Kind == OperandKind.Code)
                    {
                        branches.Add((instruction.Address, operand.Number));
                    }
                    else if (operand.Kind == OperandKind.Text)
                    {
                        strings.Add((instruction.Address, operand.Number));
                    }
                }
            }

            // Nothing says how long an instruction is but the
            // instruction itself, so a chunk read one byte wrongly
            // anywhere would come out of step and stay that way. That
            // it ends exactly where the chunk ends is the proof that
            // every operand in it was the length the table says.
            if (last != code.Length)
            {
                failures.Add($"{name}: read to {last} of {code.Length} bytes");
            }

            // And the same reading has to agree with itself: every
            // address the code branches to must be an address the
            // sweep stopped at.
            foreach (var (at, to) in branches)
            {
                if (to != 0 && !boundaries.Contains(to))
                {
                    failures.Add($"{name}: the instruction at {at} branches to {to}, which is not an instruction");
                }
            }

            // Every string the code names must be one the text decoder
            // can read, which puts the whole of a game's prose through
            // it rather than the few strings a resource names.
            var characters = 0;

            foreach (var (at, to) in strings.DistinctBy(s => s.To))
            {
                try
                {
                    characters += story.Text.At(to).Length;
                }
                catch (Exception e) when (e is InvalidDataException or ArgumentOutOfRangeException)
                {
                    failures.Add($"{name}: the string at {to}, named at {at}, does not read: {e.Message}");
                }
            }

            Assert.True(
                characters > 5000,
                $"{name} has only {characters} characters of text, which cannot be a whole game.");
        }

        Assert.Empty(failures);

        // [aam opcode] The table has more rows than any one story
        // needs, but between them the corpus should reach most of it.
        Assert.True(used.Count > 140, $"The corpus uses only {used.Count} of the opcode bytes.");
    }

    [Fact]
    public void TheOpcodeTableNamesEachByteOnce()
    {
        var bytes = new HashSet<byte>();

        foreach (var entry in OpcodeTable.All)
        {
            Assert.True(bytes.Add(entry.Code), $"Two rows claim the byte {entry.Code:x2}.");
            Assert.NotEmpty(entry.Name);
            Assert.Equal(entry.Name.ToUpperInvariant(), entry.Name);
        }

        // Every operation the machine has is reachable by some byte,
        // which is the check that the enum and the table agree.
        var named = OpcodeTable.All.Select(entry => entry.Opcode).ToHashSet();

        Assert.All(Enum.GetValues<Opcode>(), opcode => Assert.Contains(opcode, named));
    }

    [Fact]
    public void TheStatusOpcodesChangedHandsInVersionOne()
    {
        // [aam opcode] $67 was ENTER_STATUS, with the 0 that chose the
        // top status area implied, until version 1.0 gave the byte to
        // SET_BODY and moved every status area onto $6f. The corpus
        // bears this out: the three older stories use $67 and $e7 and
        // never $6f, and the version 1.0 story does the reverse.
        var old = OpcodeTable.Resolve(0x67, 0, 5);
        Assert.NotNull(old);
        Assert.Equal(Opcode.EnterStatus, old.Opcode);
        Assert.Equal([OperandKind.Index], old.Operands);

        var current = OpcodeTable.Resolve(0x67, 1, 0);
        Assert.NotNull(current);
        Assert.Equal(Opcode.SetBody, current.Opcode);

        // [aam opcode] AUX_POP_VAL was taken out in version 1.0, so a
        // story of that version has no such opcode at all.
        Assert.Equal(Opcode.AuxPopVal, OpcodeTable.Resolve(0x16, 0, 5)?.Opcode);
        Assert.Null(OpcodeTable.Resolve(0x16, 1, 0));
    }

    [Fact]
    public void AnOperandPrintsAsADisassemblerWould()
    {
        Assert.Equal("R0a", new Operand(OperandKind.Value, OperandSource.Register, 0x0a).ToString());
        Assert.Equal("E3f", new Operand(OperandKind.Value, OperandSource.EnvSlot, 0x3f).ToString());
        Assert.Equal("#3f00", new Operand(OperandKind.Value, OperandSource.Immediate, 0x3f00).ToString());
        Assert.Equal("12", new Operand(OperandKind.Byte, OperandSource.Immediate, 12).ToString());
        Assert.Equal("@01a2", new Operand(OperandKind.Code, OperandSource.Immediate, 0x01a2).ToString());
        Assert.Equal("$0a16", new Operand(OperandKind.Text, OperandSource.Immediate, 0x0a16).ToString());

        // A destination is marked with the direction the value goes:
        // over what is there, or against it.
        Assert.Equal(">R01", new Operand(OperandKind.Dest, OperandSource.Register, 1).ToString());
        Assert.Equal("=E01", new Operand(OperandKind.Dest, OperandSource.EnvSlot, 1, Unify: true).ToString());
    }
}

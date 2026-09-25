using Rezrov.Debugging;
using Rezrov.ZMachine;

namespace Rezrov.Tests;

/// <summary>
/// Reading a story file as a listing.
/// </summary>
public class DisassemblyTests
{
    private const string SubmoduleAbsent = "The entharion submodule is not populated.";

    [Fact]
    public void ABranchThatStaysInsideItsRoutineIsGivenAName()
    {
        // [zm 4.7.2] The branch data sits at $43, so the address after
        // it is $44 and an offset of 3 reaches $45.
        var lines = Lines(new Assembler()
            .Bytes(0x00)
            .Short1(Op.Jz, Assembler.Small(0)).Branch(true, 3)
            .Short0(Op.Rfalse)
            .Short0(Op.Rtrue)
            .ToArray());

        Assert.Contains("?L1", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("L1:", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void AJumpSaysWhereItGoesRatherThanHowFar()
    {
        var lines = Lines(new Assembler()
            .Bytes(0x00)
            .Short1(Op.Jump, Assembler.Large(4))
            .Short0(Op.Rfalse)
            .Short0(Op.Rfalse)
            .Short0(Op.Rtrue)
            .ToArray());

        Assert.Contains("jump", lines[0], StringComparison.Ordinal);
        Assert.Contains("L1", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("#0004", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("L1:", lines[3], StringComparison.Ordinal);
    }

    [Fact]
    public void AVariableByReferenceIsShownAsTheVariableItNames()
    {
        // [zm 4.2.3] The operand of inc is a variable number held as a
        // small constant, so #05 means the fourth local.
        var lines = Lines(new Assembler()
            .Bytes(0x00)
            .Short1(Op.Inc, Assembler.Small(5))
            .Short0(Op.Rtrue)
            .ToArray());

        Assert.Contains("L04", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("#05", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void TextAnInstructionCarriesIsShownBesideIt()
    {
        var lines = Lines(new Assembler()
            .Bytes(0x00)
            .Short0(Op.Print).Text("hi")
            .Short0(Op.Rtrue)
            .ToArray());

        Assert.EndsWith("\"hi\"", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AGapThatReadsAsTextSaysHowManyStringsFillIt()
    {
        // [zm 3.2] A word with its top bit set ends a string, so $8000
        // is a whole string of three spaces.
        var (memory, header) = DebugStory.Of(new Assembler()
            .Bytes(0x00)
            .Short0(Op.NewLine)
            .Short0(Op.NewLine)
            .Short0(Op.Rtrue)
            .Bytes(0x80, 0x00)
            .ToArray());

        var gap = Assert.Single(Disassembly.Of(memory, header).Gaps);

        Assert.Equal(0x44, gap.Address);
        Assert.Equal(2, gap.Length);
        Assert.Equal(1, gap.Strings);
    }

    [Fact]
    public void AGapThatReadsAsNothingClaimsNothing()
    {
        var (memory, header) = DebugStory.Of(new Assembler()
            .Bytes(0x00)
            .Short0(Op.NewLine)
            .Short0(Op.NewLine)
            .Short0(Op.Rtrue)
            .Bytes(0x00, 0x00)
            .ToArray());

        var gap = Assert.Single(Disassembly.Of(memory, header).Gaps);

        Assert.Equal(0x44, gap.Address);
        Assert.Equal(2, gap.Length);
        Assert.Equal(0, gap.Strings);
    }

    [Fact]
    public void TheByteBetweenTwoRoutinesIsPaddingAndNotAGap()
    {
        // [zm 1.2.3] The first routine ends at $47 and the second is
        // packed to $48, so the byte between them belongs to nothing.
        var (memory, header) = DebugStory.Of(new Assembler()
            .Bytes(0x00)
            .Variable(Op.Call, true, Assembler.Large(0x48 / 2)).Store(0)
            .Short0(Op.Rtrue)
            .Bytes(0x00)
            .Bytes(0x00)
            .Short0(Op.Rtrue)
            .ToArray());

        var listing = Disassembly.Of(memory, header);

        Assert.Empty(listing.Gaps);
        Assert.Equal(1, listing.Padding);
    }

    [Fact]
    public void ACallToAConstantAddressSaysWhichRoutineItReaches()
    {
        var lines = Lines(new Assembler()
            .Bytes(0x00)
            .Variable(Op.Call, true, Assembler.Large(0x48 / 2)).Store(0)
            .Short0(Op.Rtrue)
            .Bytes(0x00)
            .Bytes(0x00)
            .Short0(Op.Rtrue)
            .ToArray());

        Assert.EndsWith("; routine 0048", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AnAddressBelongsToARoutineUpToTheByteAfterIt()
    {
        // The routine runs from $40 to $43, and $44 is the string
        // after it, which is nobody's instruction.
        var (memory, header) = DebugStory.Of(new Assembler()
            .Bytes(0x00)
            .Short0(Op.NewLine)
            .Short0(Op.NewLine)
            .Short0(Op.Rtrue)
            .Bytes(0x80, 0x00)
            .ToArray());

        var listing = Disassembly.Of(memory, header);
        var routine = Assert.Single(listing.Routines);

        Assert.Equal(routine, listing.RoutineAt(0x40));
        Assert.Equal(routine, listing.RoutineAt(0x43));
        Assert.Null(listing.RoutineAt(0x3F));
        Assert.Null(listing.RoutineAt(0x44));
    }

    [Fact]
    public void EveryStoryFileReadsAsAListing()
    {
        var files = Corpus.StoryFiles();
        Assert.SkipUnless(files.Count > 0, SubmoduleAbsent);

        var failures = new List<string>();

        foreach (var file in files)
        {
            try
            {
                var memory = new ZMemory(File.ReadAllBytes(file));
                var header = new StoryHeader(memory);

                using var writer = new StringWriter();
                Disassembly.Of(memory, header).Write(writer);

                if (writer.ToString().Length == 0)
                {
                    failures.Add($"{Path.GetFileName(file)}: nothing was written");
                }
            }
            catch (Exception e) when (e is not Xunit.Sdk.XunitException)
            {
                failures.Add($"{Path.GetFileName(file)}: {e.GetType().Name}: {e.Message}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void TheRoutinesAndTheGapsBetweenThemAccountForHighMemory()
    {
        var files = Corpus.StoryFiles();
        Assert.SkipUnless(files.Count > 0, SubmoduleAbsent);

        var failures = new List<string>();

        foreach (var file in files)
        {
            var memory = new ZMemory(File.ReadAllBytes(file));
            var header = new StoryHeader(memory);
            var listing = Disassembly.Of(memory, header);

            var counted = listing.Routines.Sum(r => (long)r.Length)
                + listing.Gaps.Sum(g => (long)g.Length)
                + listing.Padding;

            var high = memory.Length - header.HighMemoryBase;

            if (counted != high)
            {
                failures.Add($"{Path.GetFileName(file)}: {counted} accounted for, {high} in high memory");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void TheListingForZorkOneReadsAsItDid()
    {
        // A characterization test. Its job is to notice a change in
        // the listing, not to argue that this is the best shape for
        // one. If a deliberate change breaks it, read the new output,
        // and if it reads better than this does, pin that instead.
        var root = Corpus.FindRepositoryRoot();
        Assert.SkipWhen(root is null, SubmoduleAbsent);

        var file = Path.Combine(root, "entharion", "zcode-infocom", "zork1-r88-s840726.z3");
        Assert.SkipUnless(File.Exists(file), SubmoduleAbsent);

        var memory = new ZMemory(File.ReadAllBytes(file));
        var listing = Disassembly.Of(memory, new StoryHeader(memory));

        Assert.Equal(411, listing.Routines.Count);
        Assert.Equal(54, listing.Gaps.Count);
        Assert.Equal(177, listing.Padding);

        // The newline is fixed so the same text is pinned whichever
        // machine the test runs on.
        using var written = new StringWriter { NewLine = "\n" };
        listing.Write(written);

        var head = string.Join("\n", written.ToString().Split('\n').Take(12));

        Assert.Equal(
            """
            ; Version 3, release 88, serial 840726, checksum A129
            ; dynamic memory at 0000, static at 2E53, high at 4E37
            ; 84876 bytes in the file, execution begins at 4F05
            ; 411 routines, 54 gaps, 177 bytes of padding

            routine 4E38, 1 local
                   4E3B  print         "a "
                   4E3E  print_obj     L00
                   4E40  rtrue

            routine 4E42, 1 local
                   4E45  jz            G3C ?L1
            """,
            head);
    }

    /// <summary>
    /// The listing of the one routine a fixture holds, line by line.
    /// </summary>
    private static string[] Lines(byte[] high)
    {
        var (memory, header) = DebugStory.Of(high);
        var listing = Disassembly.Of(memory, header);
        var routine = listing.Routines[0];
        var labels = Disassembly.Labels(routine);

        return [.. routine.Body.Select(one => listing.Line(one, labels))];
    }
}

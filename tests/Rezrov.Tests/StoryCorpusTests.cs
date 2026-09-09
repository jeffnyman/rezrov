using Rezrov.ZMachine;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

/// <summary>
/// Runs the interpreter's building blocks over every real story file in
/// the entharion submodule. These are the checks that hand-built inputs
/// cannot give: that the code accepts every file Infocom and Inform ever
/// shipped, and agrees with what their compilers wrote.
/// </summary>
/// <remarks>
/// The submodule is optional, and CI does not fetch it, so every test here
/// skips rather than fails when it is absent. Locally, with the submodule
/// populated, they run against the full corpus.
/// </remarks>
public class StoryCorpusTests
{
    private const string SubmoduleAbsent = "The entharion submodule is not populated.";

    [Fact]
    public void EveryStoryFileHasAValidHeader()
    {
        var files = Corpus.StoryFiles();
        Assert.SkipUnless(files.Count > 0, SubmoduleAbsent);

        var failures = new List<string>();

        foreach (var file in files)
        {
            try
            {
                var header = new StoryHeader(new ZMemory(File.ReadAllBytes(file)));

                // The extension on these files is the version, so the two
                // had better agree.
                var expected = Corpus.VersionFromExtension(file);
                if ((int)header.Version != expected)
                {
                    failures.Add($"{Path.GetFileName(file)}: header says Version {(int)header.Version}");
                }
            }
            catch (InvalidDataException e)
            {
                failures.Add($"{Path.GetFileName(file)}: {e.Message}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void EveryStoryFileWithAChecksumVerifies()
    {
        var files = Corpus.StoryFiles();
        Assert.SkipUnless(files.Count > 0, SubmoduleAbsent);

        var failures = new List<string>();
        var verified = 0;

        foreach (var file in files)
        {
            var header = new StoryHeader(new ZMemory(File.ReadAllBytes(file)));

            // [zm 11.1] Early Version 3 files have nothing to verify.
            if (!header.HasFileLength)
            {
                continue;
            }

            // A declared length with a zero checksum means the compiler
            // wrote the one and not the other. Inform 1 did this in 1993,
            // and dejavu-r1-s930921.z3 is the example in this corpus. Such
            // a file fails verification honestly, so it is not a failure
            // of the checksum code and is left out of the count.
            if (header.Checksum == 0)
            {
                continue;
            }

            if (header.VerifyChecksum())
            {
                verified++;
            }
            else
            {
                failures.Add(
                    $"{Path.GetFileName(file)}: header {header.Checksum:X4}, computed {header.ComputeChecksum():X4}");
            }
        }

        Assert.Empty(failures);
        Assert.True(verified > 0, "No file in the corpus carried a checksum, which cannot be right.");
    }

    [Fact]
    public void SimpleTestFixturesDecodeTheirSentence()
    {
        var fixtures = Corpus.SimpleTestFixtures();
        Assert.SkipUnless(fixtures.Count > 0, SubmoduleAbsent);

        // One fixture per version, so all eight decoders get exercised.
        Assert.Equal(8, fixtures.Count);

        var failures = new List<string>();

        foreach (var file in fixtures)
        {
            var memory = new ZMemory(File.ReadAllBytes(file));
            var header = new StoryHeader(memory);
            var decoder = new ZTextDecoder(memory, header);

            var text = decoder.Decode(SimpleTestSentenceAddress(memory, header));

            if (text != "hello from all z machine versions")
            {
                failures.Add($"{Path.GetFileName(file)} decoded as \"{text}\"");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void EveryAbbreviationInTheCorpusDecodes()
    {
        var files = Corpus.StoryFiles();
        Assert.SkipUnless(files.Count > 0, SubmoduleAbsent);

        var decoded = 0;

        foreach (var file in files)
        {
            var memory = new ZMemory(File.ReadAllBytes(file));
            var header = new StoryHeader(memory);

            // [zm 3.5.2] Version 1 has no abbreviations at all, and
            // [zm 3.3] Version 2 has 32 where later versions have 96.
            var count = header.Version switch
            {
                ZMachineVersion.V1 => 0,
                ZMachineVersion.V2 => 32,
                _ => 96,
            };

            // A few Infocom files, both "generic" releases and every
            // "ziptest" among them, have no abbreviations table at all
            // and leave its address as zero. There is nothing to decode
            // in those.
            if (count == 0 || header.AbbreviationsTableAddress == 0)
            {
                continue;
            }

            var decoder = new ZTextDecoder(memory, header);

            for (var number = 0; number < count; number++)
            {
                // [zm 3.3] Each entry is a word address, so double it.
                var address = memory.ReadWord(header.AbbreviationsTableAddress + (number * 2)) * 2;

                // The point is that every one of these decodes without
                // throwing, across every version and every compiler in
                // the corpus. The text itself has no ground truth to
                // compare against yet.
                _ = decoder.Decode(address);
                decoded++;
            }
        }

        Assert.True(decoded > 0);
    }

    /// <summary>
    /// Finds the address of the sentence in a simple-test fixture.
    /// </summary>
    /// <remarks>
    /// Inform compiles a print of a literal string as print_paddr with a
    /// packed address, not as an inline print, and it wraps Main in a stub
    /// that calls it. Inspecting the eight fixtures shows the same shape in
    /// every one: from the entry point, the first byte $8D is the
    /// print_paddr instruction in short form with a large constant
    /// operand, and the word after it is the packed address of the
    /// string. This scan relies on that inspection rather than on a real
    /// instruction decoder, and it should be replaced with one once that
    /// exists.
    /// </remarks>
    private static int SimpleTestSentenceAddress(ZMemory memory, StoryHeader header)
    {
        // [zm 11.1] Version 6 starts by calling a packed main routine, and
        // every other version starts executing at a byte address.
        var start = header.Version == ZMachineVersion.V6
            ? header.UnpackRoutineAddress(header.MainRoutinePackedAddress)
            : header.InitialProgramCounter;

        var at = start;
        while (memory.ReadByte(at) != 0x8D)
        {
            at++;
        }

        return header.UnpackStringAddress(memory.ReadWord(at + 1));
    }
}

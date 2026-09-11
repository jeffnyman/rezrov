using Rezrov.Core;
using Rezrov.Glulx;
using Rezrov.Glulx.Instructions;

namespace Rezrov.Tests;

/// <summary>
/// Runs the Glulx building blocks over every real Glulx file in the
/// entharion submodule, games and conformance tests alike, as
/// <see cref="StoryCorpusTests"/> does for the Z-Machine.
/// </summary>
/// <remarks>
/// The submodule is optional, and CI does not fetch it, so every test
/// here skips rather than fails when it is absent.
/// </remarks>
public class GlulxCorpusTests
{
    private const string SubmoduleAbsent = "The entharion submodule is not populated.";

    [Fact]
    public void EveryGlulxFileIsRecognized()
    {
        var files = Corpus.GlulxFiles();
        Assert.SkipUnless(files.Count > 0, SubmoduleAbsent);

        var failures = new List<string>();

        foreach (var file in files)
        {
            // A bare .ulx is Glulx; a .gblorb is a Blorb container first,
            // and the Glulx inside it is found by reading the container.
            var expected = Path.GetExtension(file) == ".ulx" ? StoryFormat.Glulx : StoryFormat.Blorb;
            var format = StoryFormatDetector.Detect(File.ReadAllBytes(file));
            if (format != expected)
            {
                failures.Add($"{Path.GetFileName(file)}: detected as {format}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void EveryGlulxFileLoadsAndVerifiesItsChecksum()
    {
        var files = Corpus.GlulxFiles();
        Assert.SkipUnless(files.Count > 0, SubmoduleAbsent);

        var failures = new List<string>();

        foreach (var file in files)
        {
            try
            {
                var memory = new GlulxMemory(Corpus.GlulxImage(file));
                var header = memory.Header;

                // [glulx #the-memory-map] Memory runs to ENDMEM whatever
                // the file's length.
                if (memory.Length != header.EndMem)
                {
                    failures.Add($"{Path.GetFileName(file)}: memory is {memory.Length:X8} bytes, ENDMEM is {header.EndMem:X8}");
                }

                // [glulx #the-header] Every compiler in the corpus writes
                // a correct checksum, so a mismatch is a fault here.
                if (!memory.VerifyChecksum())
                {
                    failures.Add($"{Path.GetFileName(file)}: header {header.Checksum:X8}, computed {memory.ComputeChecksum():X8}");
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
    public void EveryStartFunctionBeginsByCallingAFunction()
    {
        var files = Corpus.GlulxFiles();
        Assert.SkipUnless(files.Count > 0, SubmoduleAbsent);

        var failures = new List<string>();

        foreach (var file in files)
        {
            try
            {
                var memory = new GlulxMemory(Corpus.GlulxImage(file));
                var start = FunctionHeader.Read(memory, memory.Header.StartFunction);
                var first = new InstructionDecoder(memory).Decode(start.CodeAddress);

                // Every compiler in the corpus makes the start function a
                // stub that calls the real main function, by constant
                // address, so the first instruction is some kind of call
                // and its first operand names a function.
                if (first.Opcode is not (Opcode.Call or Opcode.CallF or Opcode.CallFI or Opcode.CallFII or Opcode.CallFIII))
                {
                    failures.Add($"{Path.GetFileName(file)}: starts with {first}");
                    continue;
                }

                var target = first.Operands[0];
                if (target.Kind != OperandKind.Constant)
                {
                    failures.Add($"{Path.GetFileName(file)}: calls {target}");
                    continue;
                }

                var main = FunctionHeader.Read(memory, target.Value);
                if (main.CodeAddress <= target.Value)
                {
                    failures.Add($"{Path.GetFileName(file)}: the function at {target.Value:X8} has no code");
                }
            }
            catch (GlulxException e)
            {
                failures.Add($"{Path.GetFileName(file)}: {e.Message}");
            }
        }

        Assert.Empty(failures);
    }
}

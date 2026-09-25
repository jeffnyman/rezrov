using Rezrov.Debugging;
using Rezrov.ZMachine;

namespace Rezrov.Tests;

/// <summary>
/// Accounting for every byte of a story file.
/// </summary>
public class MemoryMapTests
{
    private const string SubmoduleAbsent = "The entharion submodule is not populated.";

    [Fact]
    public void TheHeaderAndHighMemoryAreAlwaysAccountedFor()
    {
        var (memory, header) = DebugStory.Of(new Assembler()
            .Bytes(0x00)
            .Short0(Op.NewLine)
            .Short0(Op.Rtrue)
            .ToArray());

        var map = MemoryMap.Of(memory, header);

        Assert.Equal(new MemoryRegion("header", 0, 64), map[0]);
        Assert.Equal(new MemoryRegion("code and text", 0x40, 3), map[1]);
    }

    [Fact]
    public void EveryByteOfEveryStoryFileIsCoveredByARegion()
    {
        var files = Corpus.StoryFiles();
        Assert.SkipUnless(files.Count > 0, SubmoduleAbsent);

        var failures = new List<string>();

        foreach (var file in files)
        {
            var memory = new ZMemory(File.ReadAllBytes(file));
            var header = new StoryHeader(memory);
            var covered = new bool[memory.Length];

            foreach (var region in MemoryMap.Of(memory, header))
            {
                if (region.Address < 0 || region.EndAddress > memory.Length)
                {
                    failures.Add($"{Path.GetFileName(file)}: {region.Name} runs outside the file");
                    continue;
                }

                for (var i = region.Address; i < region.EndAddress; i++)
                {
                    covered[i] = true;
                }
            }

            if (Array.IndexOf(covered, false) >= 0)
            {
                failures.Add($"{Path.GetFileName(file)}: {covered.Count(c => !c)} bytes covered by nothing");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void TheRegionsComeInAddressOrder()
    {
        var files = Corpus.StoryFiles();
        Assert.SkipUnless(files.Count > 0, SubmoduleAbsent);

        var failures = new List<string>();

        foreach (var file in files)
        {
            var memory = new ZMemory(File.ReadAllBytes(file));
            var header = new StoryHeader(memory);
            var at = 0;

            foreach (var region in MemoryMap.Of(memory, header))
            {
                if (region.Address < at)
                {
                    failures.Add($"{Path.GetFileName(file)}: {region.Name} comes after {region.Address:X4}");
                }

                at = region.Address;
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void ZorkOneIsMappedWhereItsHeaderSaysItIs()
    {
        var root = Corpus.FindRepositoryRoot();
        Assert.SkipWhen(root is null, SubmoduleAbsent);

        var file = Path.Combine(root, "entharion", "zcode-infocom", "zork1-r88-s840726.z3");
        Assert.SkipUnless(File.Exists(file), SubmoduleAbsent);

        var memory = new ZMemory(File.ReadAllBytes(file));
        var header = new StoryHeader(memory);
        var map = MemoryMap.Of(memory, header);

        var named = map.Where(r => r.Name != MemoryRegion.Unaccounted)
            .Select(r => r.Name)
            .ToList();

        Assert.Equal(
            [
                "header",
                "abbreviation text",
                "abbreviation pointers",
                "property defaults",
                "object entries",
                "property tables",
                "global variables",
                "dictionary",
                "code and text",
            ],
            named);

        // [zm 6.2] 240 words, wherever the header puts them.
        var globals = map.Single(r => r.Name == "global variables");
        Assert.Equal(header.GlobalVariablesAddress, globals.Address);
        Assert.Equal(480, globals.Length);

        // [zm 1.1.1] Everything from high memory on is code and text.
        var code = map.Single(r => r.Name == "code and text");
        Assert.Equal(header.HighMemoryBase, code.Address);
        Assert.Equal(memory.Length, code.EndAddress);
    }
}

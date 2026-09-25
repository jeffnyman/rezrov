using Rezrov.Core.Acceptance;
using Rezrov.Debugging;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Screen;

namespace Rezrov.Tests;

/// <summary>
/// Finding the routines in a story file.
/// </summary>
public class RoutineScanTests
{
    private const string SubmoduleAbsent = "The entharion submodule is not populated.";

    [Fact]
    public void FindsARoutineWhereOneIsWritten()
    {
        // [zm 5.2] One local with an initial value, then a body.
        var (memory, header) = DebugStory.Of(new Assembler()
            .Bytes(0x01, 0x00, 0x00)
            .Long(Op.Add, Assembler.Small(1), Assembler.Small(2)).Store(0)
            .Short1(Op.Ret, Assembler.Var(0))
            .ToArray());

        var routine = Assert.Single(RoutineScan.Find(memory, header));

        Assert.Equal(0x40, routine.Address);
        Assert.Equal(1, routine.LocalCount);
        Assert.Equal(0x43, routine.CodeAddress);
        Assert.Equal(2, routine.Body.Count);
        Assert.Equal(0x49, routine.EndAddress);
        Assert.Equal(9, routine.Length);
    }

    [Fact]
    public void ARoutineOfOneInstructionIsKeptWhenSomethingCallsIt()
    {
        // [zm 1.2.3] In Version 3 a packed address is doubled, so the
        // routine at $48 is called by the constant $24.
        var (memory, header) = DebugStory.Of(Pair(0x48 / 2));

        var routines = RoutineScan.Find(memory, header);

        Assert.Equal([0x40, 0x48], routines.Select(r => r.Address));
        Assert.Single(routines[1].Body);
    }

    [Fact]
    public void ARoutineOfOneInstructionNothingCallsIsLeftAlone()
    {
        // The same bytes, with the call naming an address that is not in
        // the file. A single return is the commonest thing in the world
        // to find by accident, so on its own it is not believed.
        var (memory, header) = DebugStory.Of(Pair(0x100));

        var routine = Assert.Single(RoutineScan.Find(memory, header));

        Assert.Equal(0x40, routine.Address);
    }

    [Fact]
    public void ARoutineIsOnlyLookedForWhereItsVersionCanPackOne()
    {
        // [zm 1.2.3] A routine is packed to two bytes in Versions 1 to
        // 3, four in Versions 4 to 7, and eight in Version 8, so the
        // same bytes hold a routine in one version and cannot in the
        // next. $FF is too many locals to be a header, $BB is new_line
        // and $B0 is rtrue, so each of these has exactly one place a
        // routine could begin.
        byte[] atTwo = [0xFF, 0xFF, 0x00, 0xBB, 0xB0];
        byte[] atFour = [0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0xBB, 0xB0];

        Assert.Equal([0x42], Found(atTwo, ZMachineVersion.V3));
        Assert.Empty(Found(atTwo, ZMachineVersion.V5));

        Assert.Equal([0x44], Found(atFour, ZMachineVersion.V5));
        Assert.Empty(Found(atFour, ZMachineVersion.V8));
    }

    [Fact]
    public void EveryBodyRunsFromTheHeaderToTheEndWithoutAGap()
    {
        var files = Corpus.StoryFiles();
        Assert.SkipUnless(files.Count > 0, SubmoduleAbsent);

        var failures = new List<string>();

        foreach (var file in files)
        {
            var memory = new ZMemory(File.ReadAllBytes(file));
            var header = new StoryHeader(memory);

            foreach (var routine in RoutineScan.Find(memory, header))
            {
                var at = routine.CodeAddress;

                foreach (var one in routine.Body)
                {
                    if (one.Address != at)
                    {
                        failures.Add($"{Path.GetFileName(file)}: {routine.Address:X4} jumps to {one.Address:X4}");
                        break;
                    }

                    at = one.NextAddress;
                }
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void NoTwoRoutinesOverlap()
    {
        var files = Corpus.StoryFiles();
        Assert.SkipUnless(files.Count > 0, SubmoduleAbsent);

        var failures = new List<string>();

        foreach (var file in files)
        {
            var memory = new ZMemory(File.ReadAllBytes(file));
            var header = new StoryHeader(memory);
            var at = (int)header.HighMemoryBase;

            foreach (var routine in RoutineScan.Find(memory, header))
            {
                if (routine.Address < at)
                {
                    failures.Add($"{Path.GetFileName(file)}: {routine.Address:X4} begins inside the routine before it");
                }

                at = routine.EndAddress;
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void EveryInstructionZorkOneExecutesBeginsWhereTheScanSaysItDoes()
    {
        // The only authority on what is code is a game running. Where a
        // program counter lands inside a routine the scan claims, but
        // not on an instruction the scan found, the scan read that
        // routine wrongly. Nothing else about the listing is worth
        // anything if this is not true.
        var root = Corpus.FindRepositoryRoot();
        Assert.SkipWhen(root is null, SubmoduleAbsent);

        var path = Path.Combine(root, "acceptance", "zork1-r88-s840726.accept");
        Assert.SkipUnless(File.Exists(path), SubmoduleAbsent);

        var script = AcceptanceScript.Load(path);
        Assert.SkipUnless(File.Exists(script.GamePath), SubmoduleAbsent);

        var memory = new ZMemory(File.ReadAllBytes(script.GamePath));
        var header = new StoryHeader(memory);
        var routines = RoutineScan.Find(memory, header);

        var inside = new bool[memory.Length];
        var boundary = new HashSet<int>();

        foreach (var routine in routines)
        {
            for (var i = routine.Address; i < routine.EndAddress; i++)
            {
                inside[i] = true;
            }

            foreach (var one in routine.Body)
            {
                boundary.Add(one.Address);
            }
        }

        var interpreter = new Interpreter(
            memory,
            new TextWriterScreen(new StringWriter()),
            new ScriptedInput([.. script.Commands]),
            new RandomGenerator(script.Seed));

        var walked = new HashSet<int>();
        var steps = 0;

        while (!interpreter.HasQuit && steps++ < 5_000_000)
        {
            walked.Add(interpreter.State.ProgramCounter);
            interpreter.Step();
        }

        // A walkthrough this long is the point of the test, so a run
        // that stopped early would make the rest of it meaningless.
        Assert.True(steps > 100_000, $"the walkthrough only ran {steps} instructions");

        var misread = walked.Where(pc => pc < inside.Length && inside[pc] && !boundary.Contains(pc))
            .Order()
            .Select(pc => pc.ToString("X4", System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        Assert.Empty(misread);
    }

    /// <summary>Where the scan finds a routine in these bytes.</summary>
    private static int[] Found(byte[] high, ZMachineVersion version)
    {
        var (memory, header) = DebugStory.Of(high, version);
        return [.. RoutineScan.Find(memory, header).Select(r => r.Address)];
    }

    /// <summary>
    /// Two routines: one of two instructions that calls a packed
    /// address, and one of a single return at $48.
    /// </summary>
    private static byte[] Pair(int packed) => new Assembler()
        .Bytes(0x00)
        .Variable(Op.Call, true, Assembler.Large(packed)).Store(0)
        .Short0(Op.Rtrue)
        .Bytes(0x00)
        .Bytes(0x00)
        .Short0(Op.Rtrue)
        .ToArray();
}

using Rezrov.Glulx;
using Rezrov.Glulx.Execution;
using Rezrov.Glulx.Glk;

namespace Rezrov.Tests;

/// <summary>
/// Runs assembled Glulx code on a machine built over a small game file:
/// ROM from $24 to $400 holding the code, RAM from $400 to $800, the
/// extension to $A00, and a stack of a kilobyte. Results are best left
/// in RAM, where a test can read them back.
/// </summary>
internal static class GlulxRun
{
    public const uint RamStart = 0x400;

    /// <summary>
    /// The file with the code in place and its checksum right.
    /// </summary>
    public static byte[] File(GlulxAssembler code, uint stackSize = 0x400)
    {
        var file = TestGlulx.File(ramStart: RamStart, extStart: 0x800, endMem: 0xA00, stackSize: stackSize, startFunction: code.Origin);
        var bytes = code.ToArray();
        if (code.Origin + bytes.Length > RamStart)
        {
            throw new InvalidOperationException("The code does not fit in ROM.");
        }

        bytes.CopyTo(file, (int)code.Origin);
        TestGlulx.PutWord(file, 0x20, 0);
        TestGlulx.PutWord(file, 0x20, TestGlulx.SumOfWords(file, 0x800));
        return file;
    }

    /// <summary>
    /// A machine over the code, ready at its first instruction.
    /// </summary>
    public static GlulxMachine Machine(GlulxAssembler code, uint? seed = null, uint stackSize = 0x400, GlkLibrary? glk = null) =>
        new(new GlulxMemory(File(code, stackSize)), seed is { } s ? new GlulxRandom(s) : null, glk);

    /// <summary>
    /// Runs the code to its end and returns the machine.
    /// </summary>
    public static GlulxMachine Run(GlulxAssembler code, uint? seed = null, uint stackSize = 0x400, GlkLibrary? glk = null)
    {
        var machine = Machine(code, seed, stackSize, glk);
        machine.Run();
        return machine;
    }

    /// <summary>The word at an offset into RAM.</summary>
    public static uint Ram(this GlulxMachine machine, uint offset) => machine.Memory.ReadWord(RamStart + offset);
}

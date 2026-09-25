using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Screen;

namespace Rezrov.Tests;

/// <summary>
/// A small Version 5 game for the debugger to be pointed at: the start
/// calls the routine at $1100, which calls the one at $1200, which
/// takes a command and returns. It is given two commands to answer
/// with, so a run that is let go reaches the end rather than stopping
/// with nothing left to read.
/// </summary>
/// <remarks>
/// High memory begins at $400 and everything above it is zero except
/// the three pieces of code, so a scan of the file finds those and
/// nothing else. A zero byte reads as a routine of no locals whose
/// first instruction is 2OP opcode 0, which no version has, so the
/// empty space rejects itself.
/// </remarks>
internal static class DebugGame
{
    public const int Start = 0x1000;
    public const int First = 0x1100;
    public const int Second = 0x1200;

    private const int Text = 0x200;
    private const int Parse = 0x300;

    public static (ZMemory Memory, StoryHeader Header, Interpreter Machine) Of(ScriptedInput? input = null)
    {
        var bytes = new byte[8192];

        bytes[0x00] = (byte)ZMachineVersion.V5;
        Put(bytes, 0x04, 0x0400);   // [zm 11.1] high memory base
        Put(bytes, 0x06, Start);    // [zm 11.1] initial program counter
        Put(bytes, 0x08, 0x0380);   // [zm 11.1] dictionary
        Put(bytes, 0x0E, 0x0400);   // [zm 11.1] static memory base

        // [zm 13.2] An empty dictionary: no separators, six-byte
        // entries, none of them.
        bytes[0x0381] = 6;

        // [zm op:aread] The buffers the command is read into, which
        // have to be in dynamic memory and have to say how much they
        // hold. Sixty-four letters and sixteen words is plenty.
        bytes[Text] = 0x40;
        bytes[Parse] = 0x10;

        new Assembler()
            .Variable(Op.Call, true, Assembler.Large(First / 4)).Store(0)
            .Quit()
            .ToArray()
            .CopyTo(bytes, Start);

        bytes[First] = 0;
        new Assembler()
            .Variable(Op.Call, true, Assembler.Large(Second / 4)).Store(0)
            .Short0(Op.Nop)
            .Short0(Op.Rtrue)
            .ToArray()
            .CopyTo(bytes, First + 1);

        bytes[Second] = 0;
        new Assembler()
            .Variable(Op.Aread, true, Assembler.Large(Text), Assembler.Large(Parse)).Store(0)
            .Short0(Op.Rtrue)
            .ToArray()
            .CopyTo(bytes, Second + 1);

        var memory = new ZMemory(bytes);

        return (memory, new StoryHeader(memory), new Interpreter(
            memory,
            new TextWriterScreen(new StringWriter()),
            input ?? new ScriptedInput("look", "look")));
    }

    private static void Put(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 8);
        bytes[offset + 1] = (byte)value;
    }
}

using Rezrov.Core;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Instructions;
using Rezrov.ZMachine.Lexing;
using Rezrov.ZMachine.Objects;
using Rezrov.ZMachine.Text;

namespace Rezrov.Cli;

internal static class Program
{
    internal static int Main(string[] args)
    {
        var run = false;
        var trace = false;
        string? commands = null;
        var usage = args.Length < 1;

        for (var i = 1; i < args.Length && !usage; i++)
        {
            switch (args[i])
            {
                case "--run":
                    run = true;
                    break;
                case "--trace":
                    trace = true;
                    break;
                case "--commands" when i + 1 < args.Length:
                    run = true;
                    commands = args[++i];
                    break;
                default:
                    usage = true;
                    break;
            }
        }

        if (usage)
        {
            Console.Error.WriteLine("usage: rezrov <story-file> [--run] [--trace] [--commands <file>]");
            return 2;
        }

        var path = args[0];

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"rezrov: no such file: {path}");
            return 1;
        }

        if (commands is not null && !File.Exists(commands))
        {
            Console.Error.WriteLine($"rezrov: no such file: {commands}");
            return 1;
        }

        // [zm 1.1.4] A story file is at most 512K, so reading the whole
        // thing up front costs nothing worth avoiding.
        var bytes = File.ReadAllBytes(path);
        var format = StoryFormatDetector.Detect(bytes);

        if (run || trace)
        {
            if (format != StoryFormat.ZMachine)
            {
                Console.Error.WriteLine($"rezrov: only Z-machine story files can be run yet, and this is {format}");
                return 1;
            }

            return RunZMachine(bytes, trace, commands);
        }

        Console.WriteLine($"{Path.GetFileName(path)}: {format}");

        return format switch
        {
            StoryFormat.ZMachine => DescribeZMachine(bytes),
            StoryFormat.Unknown => 1,
            _ => 0,
        };
    }

    /// <summary>
    /// Runs the story until it quits or reaches something the interpreter
    /// cannot do yet, printing its output as it goes and taking commands
    /// from the console. With tracing on, every instruction is written
    /// to standard error before it runs, which is the quickest way to see
    /// how a game arrived somewhere. With a file of commands, [zm 10.2.2]
    /// the game plays from the file first and the console takes over
    /// when it ends.
    /// </summary>
    private static int RunZMachine(byte[] bytes, bool trace, string? commands)
    {
        var memory = new ZMemory(bytes);
        var header = new StoryHeader(memory);
        var interpreter = new Interpreter(memory, new TextWriterOutput(Console.Out, header, memory), new ConsoleInput(header, memory));

        if (commands is not null)
        {
            interpreter.PlayCommands(new StreamReader(commands));
        }

        try
        {
            if (trace)
            {
                while (!interpreter.HasQuit)
                {
                    var next = interpreter.Decoder.Decode(interpreter.State.ProgramCounter);
                    Console.Error.WriteLine($"{next.Address:X5}  {next}");
                    interpreter.Step();
                }
            }
            else
            {
                interpreter.Run();
            }
        }
        catch (NotSupportedException e)
        {
            Console.Out.Flush();
            Console.Error.WriteLine();
            Console.Error.WriteLine($"rezrov: stopped after {interpreter.InstructionsExecuted} instructions: {e.Message}");
            return 3;
        }
        catch (EndOfStreamException)
        {
            // The console was a pipe or a file and it ran dry while the
            // game still wanted a command, which is a normal way for a
            // scripted run to end.
            Console.Out.Flush();
            Console.Error.WriteLine();
            Console.Error.WriteLine($"rezrov: input ended after {interpreter.InstructionsExecuted} instructions");
            return 0;
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException)
        {
            Console.Out.Flush();
            Console.Error.WriteLine();
            Console.Error.WriteLine($"rezrov: error after {interpreter.InstructionsExecuted} instructions: {e.Message}");
            return 1;
        }
        finally
        {
            // [zm A] Whatever the game did that it should not have, at
            // the level the interpreter was asked to notice.
            foreach (var error in interpreter.RuntimeErrors)
            {
                Console.Error.WriteLine($"rezrov: {error}");
            }
        }

        return 0;
    }

    private static int DescribeZMachine(byte[] bytes)
    {
        var memory = new ZMemory(bytes);

        StoryHeader header;
        try
        {
            header = new StoryHeader(memory);
        }
        catch (InvalidDataException e)
        {
            Console.Error.WriteLine($"rezrov: {e.Message}");
            return 1;
        }

        Console.WriteLine($"  version {(int)header.Version}, release {header.Release}, serial {header.SerialCode}");

        if (header.InformVersion.Trim('\0').Length > 0)
        {
            Console.WriteLine($"  compiled by Inform {header.InformVersion}");
        }

        Console.WriteLine(
            $"  dynamic memory to {header.StaticMemoryBase:X4}, high memory from {header.HighMemoryBase:X4}, {bytes.Length} bytes on disk");

        if (header.HasFileLength)
        {
            var verdict = header.VerifyChecksum() ? "matches" : "does not match";
            Console.WriteLine($"  declared length {header.FileLength}, checksum {header.Checksum:X4} {verdict}");
        }
        else
        {
            Console.WriteLine("  no length or checksum recorded");
        }

        var decoder = new ZTextDecoder(memory, header);

        var objects = new ObjectTable(memory, header, decoder);
        if (objects.Count > 0)
        {
            Console.WriteLine($"  {objects.Count} objects, the first named \"{objects.ShortName(1)}\"");
        }

        var dictionary = DictionaryTable.Standard(memory, header, decoder, ZTextEncoder.ForStory(header, memory));
        var separators = string.Concat(dictionary.WordSeparators.Select(s => (char)s));
        Console.WriteLine($"  {dictionary.Count} dictionary words, separators {separators}");

        // [zm 5.4] and [zm 5.5] The first instruction the machine would
        // execute: inside the main routine in Version 6, and at the
        // initial program counter everywhere else.
        var start = header.Version == ZMachineVersion.V6
            ? RoutineHeader.Read(memory, header.Version, header.UnpackRoutineAddress(header.MainRoutinePackedAddress)).CodeAddress
            : header.InitialProgramCounter;
        var first = new InstructionDecoder(memory, header, decoder).Decode(start);
        Console.WriteLine($"  first instruction at {start:X4}: {first}");

        return 0;
    }
}

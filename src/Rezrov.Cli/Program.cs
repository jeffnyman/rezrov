using Rezrov.Core;
using Rezrov.Core.Blorb;
using Rezrov.Glulx;
// The two machines have an InstructionDecoder each, so the Glulx
// types are named individually rather than imported as a namespace.
using FunctionHeader = Rezrov.Glulx.Instructions.FunctionHeader;
using FunctionType = Rezrov.Glulx.Instructions.FunctionType;
using GlulxDecoder = Rezrov.Glulx.Instructions.InstructionDecoder;
using GlulxMachine = Rezrov.Glulx.Execution.GlulxMachine;
using GlulxRandom = Rezrov.Glulx.Execution.GlulxRandom;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Instructions;
using Rezrov.ZMachine.Lexing;
using Rezrov.ZMachine.Objects;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Text;

namespace Rezrov.Cli;

internal static class Program
{
    internal static int Main(string[] args)
    {
        if (args is ["--version"])
        {
            Console.WriteLine($"rezrov {ProgramVersion.Current}");
            return 0;
        }

        // The acceptance command stands on its own: a script says which
        // game to play, so there is no story file on the command line.
        if (args.Length > 0 && args[0] == "--accept")
        {
            return args switch
            {
                [_, var script] => Acceptance.Run(script, AcceptanceMode.Check),
                [_, var script, "--update"] => Acceptance.Run(script, AcceptanceMode.Update),
                [_, var script, "--resume"] => Acceptance.Run(script, AcceptanceMode.Resume),
                _ => Usage(),
            };
        }

        var run = false;
        var trace = false;
        string? commands = null;
        string? transcript = null;
        string? record = null;
        string? save = null;
        string? blorb = null;
        int? seed = null;
        InterpreterNumber? machine = null;
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
                case "--transcript" when i + 1 < args.Length:
                    run = true;
                    transcript = args[++i];
                    break;
                case "--record" when i + 1 < args.Length:
                    run = true;
                    record = args[++i];
                    break;
                case "--save" when i + 1 < args.Length:
                    run = true;
                    save = args[++i];
                    break;
                case "--blorb" when i + 1 < args.Length:
                    blorb = args[++i];
                    break;
                case "--seed" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed) && parsed >= 1:
                    run = true;
                    seed = parsed;
                    i++;
                    break;
                case "--interpreter" when i + 1 < args.Length && InterpreterNumbers.TryParse(args[i + 1], out var number):
                    run = true;
                    machine = number;
                    i++;
                    break;
                default:
                    usage = true;
                    break;
            }
        }

        if (usage)
        {
            return Usage();
        }

        var path = args[0];

        if (commands is not null && !File.Exists(commands))
        {
            Console.Error.WriteLine($"rezrov: no such file: {commands}");
            return 1;
        }

        if (run || trace)
        {
            if (StoryLoader.Load(path, blorb, Console.Error) is not { } story)
            {
                return 1;
            }

            if (story.Format == StoryFormat.Glulx)
            {
                if (commands is not null || transcript is not null || record is not null || save is not null || machine is not null)
                {
                    Console.Error.WriteLine("rezrov: the command, transcript, record, save, and interpreter options do not apply to Glulx yet");
                }

                return RunGlulx(story.Bytes, trace, seed);
            }

            return RunZMachine(story.Bytes, trace, commands, transcript, record, save, story.Resources, seed, machine);
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"rezrov: no such file: {path}");
            return 1;
        }

        var bytes = File.ReadAllBytes(path);
        var format = StoryFormatDetector.Detect(bytes);

        Console.WriteLine($"{Path.GetFileName(path)}: {format}");

        return format switch
        {
            StoryFormat.ZMachine => DescribeZMachine(bytes),
            StoryFormat.Glulx => DescribeGlulx(bytes),
            StoryFormat.Blorb => DescribeBlorb(bytes, path),
            _ => 1,
        };
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: rezrov <story-file> [--run] [--trace] [--commands <file>] [--transcript <file>] [--record <file>] [--save <file>] [--blorb <file>] [--seed <number>] [--interpreter <machine>]");
        Console.Error.WriteLine($"       machines: {string.Join(", ", InterpreterNumbers.AllNames)}, or a number from 1 to 11");
        Console.Error.WriteLine("       rezrov --accept <script> [--update | --resume]");
        Console.Error.WriteLine("       rezrov --version");
        return 2;
    }

    private static int DescribeBlorb(byte[] bytes, string path)
    {
        if (StoryLoader.ReadBlorb(bytes, path, Console.Error) is not { } blorb)
        {
            return 1;
        }

        var kinds = blorb.Resources
            .GroupBy(r => r.Usage)
            .Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}{(g.Count() == 1 ? "" : "s")} ({string.Join(", ", g.Select(r => r.ChunkType.Trim()).Distinct())})");
        Console.WriteLine($"  {string.Join(", ", kinds)}");

        if (blorb.GameIdentifier is { } id)
        {
            Console.WriteLine($"  for release {id.Release}, serial {id.Serial}, checksum {id.Checksum:X4}");
        }

        if (blorb.LoopingSounds.Count > 0)
        {
            Console.WriteLine($"  {blorb.LoopingSounds.Count(l => l.Value)} sounds loop until stopped");
        }

        if (blorb.Author is not null)
        {
            Console.WriteLine($"  by {blorb.Author}");
        }

        // [blorb 5] The packaged game, described as its own file would
        // be, whichever machine it is for.
        if (blorb.Executable is { } executable)
        {
            Console.WriteLine($"  runs its own {executable.ChunkType.Trim()} game of {executable.Data.Length} bytes");

            return executable.ChunkType switch
            {
                "ZCOD" => DescribeZMachine(executable.Data.ToArray()),
                "GLUL" => DescribeGlulx(executable.Data.ToArray()),
                _ => 0,
            };
        }

        return 0;
    }

    /// <summary>
    /// Says what the header of a Glulx file declares: the specification
    /// version, what Inform recorded if Inform made it, the memory map,
    /// and whether the checksum holds. Running it is for later.
    /// </summary>
    private static int DescribeGlulx(byte[] bytes)
    {
        GlulxMemory memory;
        try
        {
            memory = new GlulxMemory(bytes);
        }
        catch (InvalidDataException e)
        {
            Console.Error.WriteLine($"rezrov: {e.Message}");
            return 1;
        }

        var header = memory.Header;

        var inform = header.InformVersion is { } version
            ? $", compiled by Inform {version}, release {header.InformRelease}, serial {header.InformSerial}"
            : "";
        Console.WriteLine($"  Glulx {header.VersionText}{inform}");

        Console.WriteLine(
            $"  ROM to {header.RamStart:X8}, RAM to {header.ExtStart:X8}, memory to {header.EndMem:X8}, stack of {header.StackSize} bytes, {bytes.Length} bytes on disk");

        var table = header.DecodingTable == 0 ? "no string decoding table" : $"string decoding table at {header.DecodingTable:X8}";
        Console.WriteLine($"  starts at {header.StartFunction:X8}, {table}");

        var verdict = memory.VerifyChecksum() ? "matches" : "does not match";
        Console.WriteLine($"  checksum {header.Checksum:X8} {verdict}");

        // [glulx #function] The start function's header and the first
        // instruction the machine would execute.
        try
        {
            var start = FunctionHeader.Read(memory, header.StartFunction);
            var first = new GlulxDecoder(memory).Decode(start.CodeAddress);
            var arguments = start.Type == FunctionType.StackArguments ? "on the stack" : "in its locals";
            Console.WriteLine($"  the start function takes arguments {arguments} and has {start.LocalCount} locals");
            Console.WriteLine($"  first instruction at {start.CodeAddress:X8}: {first}");
        }
        catch (GlulxException e)
        {
            Console.Error.WriteLine($"rezrov: {e.Message}");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Runs the story until it quits or reaches something the interpreter
    /// cannot do yet, printing its output as it goes and taking commands
    /// from the console. With tracing on, every instruction is written
    /// to standard error before it runs, which is the quickest way to see
    /// how a game arrived somewhere. With a file of commands, [zm 10.2.2]
    /// the game plays from the file first and the console takes over
    /// when it ends. A transcript or record file named here is used
    /// when the game turns [zm 7] stream 2 or 4 on, without asking, and
    /// a save file named here is where [zm op:save] and [zm op:restore]
    /// go without asking either. Resources, if there are any, give the
    /// game its sounds, though the console can only ring its bell.
    /// </summary>
    private static int RunZMachine(byte[] bytes, bool trace, string? commands, string? transcript, string? record, string? save, BlorbFile? resources, int? seed, InterpreterNumber? machine)
    {
        var memory = new ZMemory(bytes);
        var header = new StoryHeader(memory);

        // [zm 16] and [zm 3.8.5] The font 3 characters and the accented
        // ones are Unicode, which the console shows only as UTF-8.
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // [zm 2.4.2] A seed makes the game's random numbers predictable,
        // so a scripted run plays the same way every time.
        var interpreter = new Interpreter(
            memory,
            new TextWriterScreen(Console.Out),
            new ConsoleInput(header, memory),
            seed is { } s ? new RandomGenerator(s) : null,
            files: new ConsoleFiles(transcript, record, save),
            sound: new ConsoleSound(),
            interpreterNumber: machine);

        if (resources is not null)
        {
            try
            {
                interpreter.UseResources(resources);
            }
            catch (InvalidDataException e)
            {
                // [blorb 6] Complain righteously, then carry on without.
                Console.Error.WriteLine($"rezrov: ignoring the resource file: {e.Message}");
            }
        }

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
            interpreter.Streams.Flush();

            // [zm A] Whatever the game did that it should not have, at
            // the level the interpreter was asked to notice.
            foreach (var error in interpreter.RuntimeErrors)
            {
                Console.Error.WriteLine($"rezrov: {error}");
            }
        }

        return 0;
    }

    /// <summary>
    /// Runs a Glulx game as far as the machine goes so far, which is up
    /// to the first opcode that is not built yet, printing what stopped
    /// it. A seed makes its random numbers predictable.
    /// </summary>
    private static int RunGlulx(byte[] bytes, bool trace, int? seed)
    {
        GlulxMachine machine;
        try
        {
            machine = new GlulxMachine(new GlulxMemory(bytes), seed is { } s ? new GlulxRandom((uint)s) : null);
        }
        catch (InvalidDataException e)
        {
            Console.Error.WriteLine($"rezrov: {e.Message}");
            return 1;
        }

        try
        {
            if (trace)
            {
                while (!machine.HasQuit)
                {
                    var next = machine.Decoder.Decode(machine.ProgramCounter);
                    Console.Error.WriteLine($"{next.Address:X8}  {next}");
                    machine.Step();
                }
            }
            else
            {
                machine.Run();
            }
        }
        catch (NotSupportedException e)
        {
            Console.Out.Flush();
            Console.Error.WriteLine();
            Console.Error.WriteLine($"rezrov: stopped after {machine.InstructionsExecuted} instructions: {e.Message}");
            return 3;
        }
        catch (GlulxException e)
        {
            Console.Out.Flush();
            Console.Error.WriteLine();
            Console.Error.WriteLine($"rezrov: fatal error after {machine.InstructionsExecuted} instructions: {e.Message}");
            return 1;
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

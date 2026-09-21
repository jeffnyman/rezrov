using Rezrov.AaMachine;
using Rezrov.Core;
using Rezrov.Core.Blorb;
using Rezrov.Core.Graphics;
using Rezrov.Glulx;
// The two machines have an InstructionDecoder each, so the Glulx
// types are named individually rather than imported as a namespace.
using FunctionHeader = Rezrov.Glulx.Instructions.FunctionHeader;
using FunctionType = Rezrov.Glulx.Instructions.FunctionType;
using GlulxDecoder = Rezrov.Glulx.Instructions.InstructionDecoder;
using GlulxMachine = Rezrov.Glulx.Execution.GlulxMachine;
using GlulxRandom = Rezrov.Glulx.Execution.GlulxRandom;
using Rezrov.Glulx.Glk;
using Rezrov.Glulx.Text;
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

        if (args is ["--help"] or ["-h"])
        {
            // Asked for, the help goes to standard output and is no error.
            Help(Console.Out);
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
        var tandy = false;
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
                case "--tandy":
                    tandy = true;
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

            if (story.Format == StoryFormat.AaMachine)
            {
                foreach (var option in Inapplicable(
                    ("interpreter", machine is not null),
                    ("tandy", tandy),
                    ("trace", trace),
                    ("transcript", transcript is not null),
                    ("record", record is not null),
                    ("blorb", blorb is not null)))
                {
                    Console.Error.WriteLine($"rezrov: the {option} option does not apply to the Aa-machine");
                }

                return AaMachinePlayer.Play(story.Bytes, commands, save, seed);
            }

            if (story.Format == StoryFormat.Glulx)
            {
                if (machine is not null)
                {
                    Console.Error.WriteLine("rezrov: the interpreter option does not apply to Glulx");
                }

                if (tandy)
                {
                    Console.Error.WriteLine("rezrov: the tandy option does not apply to Glulx");
                }

                var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
                return RunGlulx(story.Bytes, story.Resources, directory, trace, commands, transcript, record, save, seed);
            }

            return RunZMachine(story.Bytes, trace, commands, transcript, record, save, story.Resources, seed, machine, tandy);
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
            StoryFormat.AaMachine => DescribeAaMachine(bytes),
            _ => 1,
        };
    }

    /// <summary>
    /// The options that were given but mean nothing to the machine
    /// about to run, so that a player is told rather than left to
    /// wonder why one of them did nothing.
    /// </summary>
    private static IEnumerable<string> Inapplicable(params (string Name, bool Given)[] options)
    {
        foreach (var (name, given) in options)
        {
            if (given)
            {
                yield return name;
            }
        }
    }

    /// <summary>
    /// [aam story] What an Aa-machine story file says about itself.
    /// </summary>
    private static int DescribeAaMachine(byte[] bytes)
    {
        AaStory story;

        try
        {
            story = AaStory.Read(bytes);
        }
        catch (InvalidDataException e)
        {
            Console.Error.WriteLine($"rezrov: {e.Message}");
            return 1;
        }

        Console.WriteLine(
            $"  version {story.MajorVersion}.{story.MinorVersion}, release {story.Release}, serial {story.Serial}");
        Console.WriteLine(
            $"  {story.WordSize} bytes to a word, {story.HeapSize} words of heap, "
            + $"{story.AuxSize} of auxiliary, {story.RamSize} writable");

        // The checksum covers seven of the chunks in one fixed order, so
        // it is worth saying whether it comes out, as for a Z-Machine
        // story's own checksum.
        Console.WriteLine(story.VerifyChecksum()
            ? $"  checksum {story.Checksum:X8} matches"
            : $"  checksum {story.Checksum:X8} does NOT match {story.ComputeChecksum():X8}");

        if (story.Identifier is { Length: > 0 } identifier)
        {
            Console.WriteLine($"  {identifier}");
        }

        Console.WriteLine($"  {story.Chunks.Count} chunks: "
            + string.Join(", ", story.Chunks.Select(chunk => $"{chunk.Name} {chunk.Length}")));

        // [aam story] The language is what everything else is written
        // in, so it is worth saying how large it is before saying
        // anything that had to be read through it.
        Console.WriteLine(
            $"  {story.Language.Characters.Count} characters past ASCII, "
            + $"{story.Language.DecodingTable.Length / 2} nodes of decoding tree, "
            + $"{story.Dictionary.Count} dictionary words");

        // Walking the bytecode is also the plainest check that it can
        // be walked: the count only comes out if every instruction in
        // the chunk decoded.
        Console.WriteLine(
            $"  {story.Instructions.All().Count()} instructions in {story.Instructions.Length} bytes");

        DescribeAaMetadata(story.Metadata);

        foreach (var resource in story.Resources)
        {
            // [aam story] A resource that names a file the story
            // carries is one the interpreter can actually show, so it
            // is worth saying how large that file is.
            var packaged = story.Contents(resource).Length;

            Console.WriteLine($"  {resource.Url}: {story.Text.At(resource.AltText)}"
                + (packaged > 0 ? $" ({packaged} bytes)" : string.Empty));
        }

        return 0;
    }

    /// <summary>
    /// [aam story] What a story says about itself, which is the first
    /// thing read through the game's own character set.
    /// </summary>
    private static void DescribeAaMetadata(AaMetadata metadata)
    {
        if (metadata.Title is { Length: > 0 } title)
        {
            Console.WriteLine($"  {title}"
                + (metadata.Noun is { Length: > 0 } noun ? $", {noun}" : string.Empty));
        }

        if (metadata.Author is { Length: > 0 } author)
        {
            Console.WriteLine($"  by {author}"
                + (metadata.ReleaseDate is { Length: > 0 } date ? $", {date}" : string.Empty));
        }

        if (metadata.Compiler is { Length: > 0 } compiler)
        {
            Console.WriteLine($"  built by {compiler}");
        }
    }

    /// <summary>
    /// The help, on standard error, for a command line that could not
    /// be understood.
    /// </summary>
    private static int Usage()
    {
        Help(Console.Error);
        return 2;
    }

    private static void Help(TextWriter writer)
    {
        var names = InterpreterNumbers.AllNames.ToList();
        writer.Write($"""
            usage: rezrov <story-file> [options]
                   rezrov --accept <script> [--update | --resume]
                   rezrov --version
                   rezrov --help

            Given a story file alone, rezrov describes it. With --run it plays the
            game on the console, as a plain stream of text.

            options:
              --run                    play the game
              --trace                  play, listing every instruction on standard error
              --commands <file>        take commands from a file before the console
              --transcript <file>      write the transcript the game keeps to a file
              --record <file>          write the commands typed to a file
              --save <file>            save to and restore from this file, unasked
              --blorb <file>           take sounds and pictures from this resource file
              --seed <number>          seed the game's random numbers, so a play repeats
              --interpreter <machine>  tell the game which machine it is running on
              --tandy                  set the Tandy bit for a Version 1 to 3 game

            machines: {string.Join(", ", names.Take(6))},
                      {string.Join(", ", names.Skip(6))}, or a number from 1 to 11

            The acceptance command plays a script and checks the play against the
            recording beside it: --update records the play afresh, and --resume
            hands the game over at the console where the script ends.

            """);
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

        DescribePictures(blorb);

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
    /// [blorb 2] The pictures a resource file carries, and how many of
    /// them can actually be turned into pixels, which is what a
    /// frontend that draws them will be working from.
    /// </summary>
    private static void DescribePictures(BlorbFile blorb)
    {
        var catalog = BlorbPictures.From(blorb);
        if (catalog.Count == 0)
        {
            return;
        }

        var kinds = catalog.Pictures
            .GroupBy(p => p.Kind)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}");

        Console.WriteLine($"  pictures: {string.Join(", ", kinds)}");

        // [blorb 2.3] A placeholder rectangle has a size and nothing to
        // draw, so it is left out of the count of what decodes.
        var drawable = catalog.Pictures.Where(p => p.Kind != PictureKind.Rectangle).ToList();
        if (drawable.Count > 0)
        {
            var read = drawable.Count(p => PictureReader.Decode(p) is not null);
            var widest = drawable.Max(p => p.Width);
            var tallest = drawable.Max(p => p.Height);

            Console.WriteLine($"  {read} of {drawable.Count} decoded, the largest {widest} by {tallest}");
        }

        // [blorb 11.2] The window the author drew for, which is what the
        // scaling rules measure a real screen against.
        if (catalog.StandardWindow is { } window)
        {
            Console.WriteLine($"  drawn for a window of {window.Width} by {window.Height}");
        }
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

        if (header.DecodingTable != 0)
        {
            // [glulx #string_table] The tree as it reads, beside the count
            // the table declares, which older Inform files overstate.
            try
            {
                var tree = DecodingTable.Read(memory, header.DecodingTable);
                Console.WriteLine($"  the table is {tree.Length} bytes with {tree.NodesRead} nodes, {tree.Leaves} of them leaves; it declares {tree.DeclaredNodeCount}");
            }
            catch (GlulxException e)
            {
                Console.Error.WriteLine($"rezrov: {e.Message}");
                return 1;
            }
        }

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
    private static int RunZMachine(byte[] bytes, bool trace, string? commands, string? transcript, string? record, string? save, BlorbFile? resources, int? seed, InterpreterNumber? machine, bool tandy)
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
            interpreterNumber: machine,
            tandy: tandy);

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
    /// Runs a Glulx game, taking the player's commands from the console
    /// after any in a command file, until it ends or reaches something
    /// not built yet, printing what stopped it. Glk's text buffer
    /// windows go to the console as one stream of text, and its files
    /// live beside the game unless the player types a path, or a
    /// transcript, record, or save file was named on the command line.
    /// The resource file, if there is one, is what the game's resource
    /// streams read. A seed makes the game's random numbers predictable.
    /// </summary>
    private static int RunGlulx(byte[] bytes, BlorbFile? resources, string directory, bool trace, string? commands, string? transcript, string? record, string? save, int? seed)
    {
        // [glk #encoding] Glk text is Latin-1 and Unicode, which the
        // console shows only as UTF-8.
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        using var reader = commands is null
            ? Console.In
            : new CommandReplayReader(new StreamReader(commands), Console.In, Console.Out);

        // [glk op:fileref_create_by_prompt] A file the game asks the
        // player for is asked on standard error, so the question stays
        // out of a transcript of standard output, and answered on the
        // same reader as commands, so a command file can answer it.
        var files = new DiskGlkFileSystem(directory, reader, Console.Error);
        if (transcript is not null)
        {
            files.NamedFiles[FileUsage.Transcript] = Path.GetFullPath(transcript);
        }

        if (record is not null)
        {
            files.NamedFiles[FileUsage.InputRecord] = Path.GetFullPath(record);
        }

        if (save is not null)
        {
            files.NamedFiles[FileUsage.SavedGame] = Path.GetFullPath(save);
        }

        var glk = new GlkLibrary(new TextWriterGlkDisplay(Console.Out, reader), files) { Resources = resources };
        GlulxMachine machine;
        try
        {
            machine = new GlulxMachine(new GlulxMemory(bytes), seed is { } s ? new GlulxRandom((uint)s) : null, glk);
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
        catch (EndOfStreamException)
        {
            // The console was a pipe or a file and it ran dry while the
            // game still wanted a command, which is a normal way for a
            // scripted run to end.
            Console.Out.Flush();
            Console.Error.WriteLine();
            Console.Error.WriteLine($"rezrov: input ended after {machine.InstructionsExecuted} instructions");
            return 0;
        }
        finally
        {
            glk.CloseFiles();
            Console.Out.Flush();

            // Whatever the game asked Glk for that Glk calls illegal.
            foreach (var warning in glk.Warnings)
            {
                Console.Error.WriteLine($"rezrov: glk: {warning}");
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

        // [babel legacy Z-code IFID] A story carries nothing that says
        // which game it is, so it is named from its header, and the
        // games that came before anyone thought to write a name down
        // are looked up in the catalog of them.
        if (Ifid.Of(bytes) is { } ifid)
        {
            var title = InfocomCatalog.Title(ifid);

            Console.WriteLine(title is null ? $"  {ifid}" : $"  {ifid}, Infocom's {title}");
        }

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

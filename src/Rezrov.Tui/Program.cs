using Rezrov.Core;
using Rezrov.Core.Blorb;
using Rezrov.Glulx;
using Rezrov.Glulx.Execution;
using Rezrov.Glulx.Glk;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Text;
using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace Rezrov.Tui;

/// <summary>
/// The terminal frontend: the interpreter on its own thread, Terminal.Gui
/// on the main one, and the four seams of the core between them.
/// </summary>
internal static class Program
{
    internal static int Main(string[] args)
    {
        if (args is ["--version"])
        {
            Console.WriteLine($"rezrov-tui {ProgramVersion.Current}");
            return 0;
        }

        string? blorb = null;
        string? commands = null;
        string? transcript = null;
        string? record = null;
        string? save = null;
        int? seed = null;
        InterpreterNumber? machine = null;
        var usage = args.Length < 1;

        for (var i = 1; i < args.Length && !usage; i++)
        {
            switch (args[i])
            {
                case "--blorb" when i + 1 < args.Length:
                    blorb = args[++i];
                    break;
                case "--commands" when i + 1 < args.Length:
                    commands = args[++i];
                    break;
                case "--transcript" when i + 1 < args.Length:
                    transcript = args[++i];
                    break;
                case "--record" when i + 1 < args.Length:
                    record = args[++i];
                    break;
                case "--save" when i + 1 < args.Length:
                    save = args[++i];
                    break;
                case "--seed" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed) && parsed >= 1:
                    seed = parsed;
                    i++;
                    break;
                case "--interpreter" when i + 1 < args.Length && InterpreterNumbers.TryParse(args[i + 1], out var number):
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
            Console.Error.WriteLine("usage: rezrov-tui <story-file> [--blorb <file>] [--commands <file>] [--transcript <file>] [--record <file>] [--save <file>] [--seed <number>] [--interpreter <machine>]");
            Console.Error.WriteLine($"       machines: {string.Join(", ", InterpreterNumbers.AllNames)}, or a number from 1 to 11");
            return 2;
        }

        var path = args[0];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"rezrov-tui: no such file: {path}");
            return 1;
        }

        var bytes = File.ReadAllBytes(path);
        BlorbFile? resources;

        switch (StoryFormatDetector.Detect(bytes))
        {
            case StoryFormat.Blorb:
                // [blorb 5] A resource file with the game inside it.
                if (ReadBlorb(bytes, path) is not { } packaged)
                {
                    return 1;
                }

                if (packaged.Executable is { ChunkType: "GLUL" } glulx)
                {
                    return PlayGlulx(glulx.Data.ToArray(), packaged, path, new TerminalFiles.Presets(transcript, record, save, commands), seed);
                }

                if (packaged.Executable is not { ChunkType: "ZCOD" } executable)
                {
                    Console.Error.WriteLine($"rezrov-tui: {Path.GetFileName(path)} has no game in it");
                    return 1;
                }

                bytes = executable.Data.ToArray();
                resources = packaged;
                break;
            case StoryFormat.ZMachine:
                resources = FindResources(path, blorb);
                break;
            case StoryFormat.Glulx:
                return PlayGlulx(bytes, FindResources(path, blorb), path, new TerminalFiles.Presets(transcript, record, save, commands), seed);
            default:
                Console.Error.WriteLine($"rezrov-tui: only Z-machine and Glulx story files can be run");
                return 1;
        }

        var memory = new ZMemory(bytes);
        StoryHeader header;
        try
        {
            header = new StoryHeader(memory);
        }
        catch (InvalidDataException e)
        {
            Console.Error.WriteLine($"rezrov-tui: {e.Message}");
            return 1;
        }

        return Play(memory, header, resources, Path.GetFileName(path), new TerminalFiles.Presets(transcript, record, save, commands), seed, machine);
    }

    private static int Play(ZMemory memory, StoryHeader header, BlorbFile? resources, string title, TerminalFiles.Presets presets, int? seed, InterpreterNumber? machine)
    {
        using var app = Application.Create().Init();

        var window = new Window { Title = title };
        var keys = new KeyMap(UnicodeTranslationTable.ForStory(header, memory));
        var view = new GameView(() => app.RequestStop());
        window.Add(view);

        // The terminal's size is only known once the view is drawn, and
        // [zm 8.4] the game must be told its size before it starts, so
        // the game is built and started from the view's first draw.
        Interpreter? interpreter = null;
        string? ending = null;

        view.Ready = (width, height) =>
        {
            // The screen needs the input for the [MORE] key and the input
            // needs the screen for its echo, so each reaches the other
            // through a variable filled in a moment later.
            TerminalInput? input = null;
            var screen = new TerminalScreen(
                width,
                height,
                cursorStartsAtBottom: header.Version <= ZMachineVersion.V4,
                repaint: () => app.Invoke(() => view.SetNeedsDraw()),
                waitForKey: () => input!.WaitForAnyKey());
            // [zm 10.3.2] Clicks are reported in screen units, which
            // are cells before Version 6 and the frontend's font size in
            // Version 6.
            input = header.Version == ZMachineVersion.V6
                ? new TerminalInput(screen, screen.FontWidth, screen.FontHeight)
                : new TerminalInput(screen);
            view.Picture = screen;
            view.KeyPressed = key =>
            {
                if (keys.ToZscii(key) is not { } zscii)
                {
                    return false;
                }

                input.Enqueue(zscii);
                return true;
            };
            view.Clicked = input.EnqueueClick;

            // [zm 2.4.2] A seed makes the game's random numbers
            // predictable, so a session can be played again the same way.
            interpreter = new Interpreter(
                memory,
                screen,
                input,
                seed is { } s ? new RandomGenerator(s) : null,
                files: new TerminalFiles(app, presets),
                sound: new TerminalSound(),
                interpreterNumber: machine);

            if (resources is not null)
            {
                try
                {
                    interpreter.UseResources(resources);
                }
                catch (InvalidDataException)
                {
                    // [blorb 6] The wrong game's resources are left alone.
                }
            }

            if (presets.Commands is not null && File.Exists(presets.Commands))
            {
                interpreter.PlayCommands(new StreamReader(presets.Commands));
            }

            var worker = new Thread(() =>
            {
                try
                {
                    interpreter.Run();
                }
                catch (NotSupportedException e)
                {
                    ending = $"Stopped: {e.Message}";
                }
                catch (Exception e) when (e is InvalidOperationException or InvalidDataException)
                {
                    ending = $"Error: {e.Message}";
                }

                // Whatever happened, say so on screen and wait for a key
                // before the terminal goes back to the shell.
                var notice = new ZMachine.Screen.TextAttributes(ZMachine.Screen.TextStyle.ReverseVideo, screen.DefaultForeground, screen.DefaultBackground, 1);
                screen.NewLine();
                if (ending is not null)
                {
                    screen.Print(ending, notice);
                    screen.NewLine();
                }

                screen.Print("[The game has ended. Press a key to leave.]", notice);
                input.WaitForAnyKey();
                app.Invoke(() => app.RequestStop());
            })
            {
                IsBackground = true,
                Name = "Z-machine",
            };

            worker.Start();
        };

        app.Run(window);
        window.Dispose();

        // [zm A] The undefined things the game did, once the terminal is
        // ordinary again.
        if (interpreter is not null)
        {
            foreach (var error in interpreter.RuntimeErrors)
            {
                Console.Error.WriteLine($"rezrov-tui: {error}");
            }
        }

        return ending is null || !ending.StartsWith("Stopped", StringComparison.Ordinal) ? 0 : 3;
    }

    /// <summary>
    /// Runs a Glulx game on the terminal: the Glk display over the
    /// screen model, files through dialogs, and the machine on a worker
    /// thread, the way the Z-machine runs.
    /// </summary>
    private static int PlayGlulx(byte[] bytes, BlorbFile? resources, string path, TerminalFiles.Presets presets, int? seed)
    {
        GlulxMemory memory;
        try
        {
            memory = new GlulxMemory(bytes);
        }
        catch (InvalidDataException e)
        {
            Console.Error.WriteLine($"rezrov-tui: {e.Message}");
            return 1;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
        using var app = Application.Create().Init();
        var window = new Window { Title = Path.GetFileName(path) };
        var view = new GameView(() => app.RequestStop());
        window.Add(view);

        GlkLibrary? glk = null;
        string? ending = null;
        view.Ready = (width, height) =>
        {
            var commands = presets.Commands is not null && File.Exists(presets.Commands) ? new StreamReader(presets.Commands) : null;
            var display = new TerminalGlkDisplay(width, height, () => app.Invoke(() => view.SetNeedsDraw()), commands);
            view.Picture = display;
            view.KeyPressed = key =>
            {
                if (GlkKeyMap.ToGlk(key) is not { } code)
                {
                    return false;
                }

                display.Enqueue(code);
                return true;
            };

            // [glk op:fileref_create_by_prompt] The player is asked
            // through the same dialogs the Z-machine uses, with the
            // usage as the title; files named up front are never asked
            // about.
            var dialogs = new TerminalFiles(app, presets);
            var files = new DiskGlkFileSystem(directory, (usage, mode) =>
            {
                var title = usage switch
                {
                    FileUsage.SavedGame => "Saved game",
                    FileUsage.Transcript => "Transcript file",
                    FileUsage.InputRecord => "Command record file",
                    _ => "Data file",
                };
                return mode == Rezrov.Glulx.Glk.FileMode.Read ? dialogs.AskForOpen(title) : dialogs.AskForSave(title);
            });
            if (presets.Transcript is not null)
            {
                files.NamedFiles[FileUsage.Transcript] = Path.GetFullPath(presets.Transcript);
            }

            if (presets.Record is not null)
            {
                files.NamedFiles[FileUsage.InputRecord] = Path.GetFullPath(presets.Record);
            }

            if (presets.Save is not null)
            {
                files.NamedFiles[FileUsage.SavedGame] = Path.GetFullPath(presets.Save);
            }

            var library = new GlkLibrary(display, files) { Resources = resources };
            glk = library;
            var machine = new GlulxMachine(memory, seed is { } s ? new GlulxRandom((uint)s) : null, library);

            var worker = new Thread(() =>
            {
                try
                {
                    machine.Run();
                }
                catch (NotSupportedException e)
                {
                    ending = $"Stopped: {e.Message}";
                }
                catch (GlulxException e)
                {
                    ending = $"Error: {e.Message}";
                }
                catch (EndOfStreamException)
                {
                    // The game waited on nothing, which is its end.
                }
                finally
                {
                    library.CloseFiles();
                    commands?.Dispose();
                }

                display.Notice(ending is null ? "[The game has ended. Press a key to leave.]" : $"{ending} Press a key to leave.");
                app.Invoke(() => app.RequestStop());
            })
            {
                IsBackground = true,
                Name = "Glulx",
            };
            worker.Start();
        };

        app.Run(window);
        window.Dispose();

        // What the library noticed, once the terminal is ordinary again.
        if (glk is not null)
        {
            foreach (var warning in glk.Warnings)
            {
                Console.Error.WriteLine($"rezrov-tui: glk: {warning}");
            }
        }

        return ending is null || !ending.StartsWith("Stopped", StringComparison.Ordinal) ? 0 : 3;
    }

    private static BlorbFile? ReadBlorb(byte[] bytes, string path)
    {
        try
        {
            return BlorbFile.Read(bytes);
        }
        catch (InvalidDataException e)
        {
            Console.Error.WriteLine($"rezrov-tui: {Path.GetFileName(path)}: {e.Message}");
            return null;
        }
    }

    private static BlorbFile? FindResources(string storyPath, string? blorbPath)
    {
        if (blorbPath is null)
        {
            foreach (var extension in new[] { ".blb", ".blorb", ".zblorb" })
            {
                var candidate = Path.ChangeExtension(storyPath, extension);
                if (File.Exists(candidate))
                {
                    blorbPath = candidate;
                    break;
                }
            }
        }

        return blorbPath is not null && File.Exists(blorbPath) ? ReadBlorb(File.ReadAllBytes(blorbPath), blorbPath) : null;
    }
}

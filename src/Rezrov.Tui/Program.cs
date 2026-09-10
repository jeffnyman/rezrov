using Rezrov.Core;
using Rezrov.Core.Blorb;
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
        string? blorb = null;
        string? commands = null;
        string? transcript = null;
        string? record = null;
        string? save = null;
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
                default:
                    usage = true;
                    break;
            }
        }

        if (usage)
        {
            Console.Error.WriteLine("usage: rezrov-tui <story-file> [--blorb <file>] [--commands <file>] [--transcript <file>] [--record <file>] [--save <file>]");
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

                if (packaged.Executable is not { ChunkType: "ZCOD" } executable)
                {
                    Console.Error.WriteLine($"rezrov-tui: {Path.GetFileName(path)} has no Z-code game in it");
                    return 1;
                }

                bytes = executable.Data.ToArray();
                resources = packaged;
                break;
            case StoryFormat.ZMachine:
                resources = FindResources(path, blorb);
                break;
            default:
                Console.Error.WriteLine($"rezrov-tui: only Z-machine story files can be run yet");
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

        if (header.Version == ZMachineVersion.V6)
        {
            Console.Error.WriteLine("rezrov-tui: Version 6 games need the Version 6 screen model, which is not implemented yet");
            return 3;
        }

        return Play(memory, header, resources, Path.GetFileName(path), new TerminalFiles.Presets(transcript, record, save, commands));
    }

    private static int Play(ZMemory memory, StoryHeader header, BlorbFile? resources, string title, TerminalFiles.Presets presets)
    {
        using var app = Application.Create().Init();

        var window = new Window { Title = title };
        var keys = new KeyMap(UnicodeTranslationTable.ForStory(header, memory));
        var view = new GameView(keys, () => app.RequestStop());
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
            input = new TerminalInput(screen);
            view.Screen = screen;
            view.Input = input;

            interpreter = new Interpreter(
                memory,
                screen,
                input,
                files: new TerminalFiles(app, presets),
                sound: new TerminalSound());

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

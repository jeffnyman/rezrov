using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Rezrov.Core;
using Rezrov.Core.Blorb;
using Rezrov.Glulx;
using Rezrov.Glulx.Execution;
using Rezrov.Glulx.Glk;

namespace Rezrov.Gui;

/// <summary>
/// The graphical frontend: the interpreter on its own thread, the
/// toolkit on the main one, and the Glk display between them.
/// </summary>
/// <remarks>
/// What is here so far is the text of a Glk game. Graphics windows, the
/// pictures a text buffer can hold, the Z-machine screen and its
/// Version 6 windows, sound, and the file dialogs all come after, each
/// against the seam the core already has for it.
/// </remarks>
internal static class Program
{
    /// <summary>
    /// [blorb 5] What a resource file beside a bare game is called.
    /// </summary>
    private static readonly string[] Extensions = [".blb", ".blorb"];

    private static string _path = "";
    private static byte[] _bytes = [];
    private static BlorbFile? _resources;
    private static int? _seed;
    private static int _result;

    [STAThread]
    internal static int Main(string[] args)
    {
        if (args is ["--version"])
        {
            Console.WriteLine($"rezrov-gui {ProgramVersion.Current}");
            return 0;
        }

        if (args is ["--help"] or ["-h"])
        {
            Help(Console.Out);
            return 0;
        }

        if (args is ["--probe"])
        {
            return Probe();
        }

        string? blorb = null;
        var usage = args.Length < 1;

        for (var i = 1; i < args.Length && !usage; i++)
        {
            switch (args[i])
            {
                case "--blorb" when i + 1 < args.Length:
                    blorb = args[++i];
                    break;
                case "--seed" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed) && parsed >= 1:
                    _seed = parsed;
                    i++;
                    break;
                default:
                    usage = true;
                    break;
            }
        }

        if (usage)
        {
            Help(Console.Error);
            return 2;
        }

        _path = args[0];
        if (!Load(blorb))
        {
            return 1;
        }

        AppBuilder.Configure<GameApp>()
            .UsePlatformDetect()

            // The GPU path on Windows is ANGLE, whose native library is
            // left out of the published folder to save five megabytes.
            // Drawing text and the occasional picture asks little enough
            // that the software renderer is ample, and saying so here
            // means the choice is made on purpose rather than by the
            // absence of a file.
            .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
            .StartWithClassicDesktopLifetime([]);

        return _result;
    }

    /// <summary>
    /// Prints what the fonts measure and leaves, with no window.
    /// </summary>
    /// <remarks>
    /// A window cannot be driven from a script, so everything about this
    /// program that a test could check is checked by a person looking at
    /// it. This is the part that does not have to be: the numbers the
    /// whole layout is built on, which are wrong in ways that are
    /// perfectly quiet on the screen. The first of them caught a space
    /// measuring zero, which ran every word into the next.
    /// </remarks>
    private static int Probe()
    {
        // The font manager comes from the builder, so it has to be set
        // up even though no window is ever shown.
        AppBuilder.Configure<GameApp>().UsePlatformDetect().SetupWithoutStarting();
        var glyphs = new Glyphs();

        Console.WriteLine($"cell: {glyphs.CellWidth} by {glyphs.CellHeight}");

        foreach (var style in new[] { GlkStyle.Normal, GlkStyle.Emphasized, GlkStyle.Preformatted, GlkStyle.Header })
        {
            var space = glyphs.Width(" ", style);
            var word = glyphs.Width("brown", style);
            var both = glyphs.Width("brown fox", style);
            var fox = glyphs.Width("fox", style);

            // A space has to be worth something, and the whole has to be
            // the sum of its parts, or the words will not line up.
            var adds = Math.Abs(word + space + fox - both) < 0.5;
            Console.WriteLine(
                $"{style}: space {space:F2}, line {glyphs.LineHeight(style):F2}, "
                + $"\"brown fox\" {both:F2}, parts add up {adds}");

            if (space <= 0 || !adds)
            {
                Console.Error.WriteLine($"rezrov-gui: the {style} font does not measure sensibly.");
                return 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// Reads the story, and the resource file beside it if there is one.
    /// </summary>
    private static bool Load(string? blorb)
    {
        if (!File.Exists(_path))
        {
            Console.Error.WriteLine($"rezrov-gui: {_path}: no such file.");
            return false;
        }

        var bytes = File.ReadAllBytes(_path);

        // [blorb 5] A packaged game carries its own resources; a bare
        // one may have a resource file beside it.
        if (StoryFormatDetector.Detect(bytes) == StoryFormat.Blorb)
        {
            var packaged = ReadBlorb(bytes);
            if (packaged?.Executable is not { } executable || executable.ChunkType != "GLUL")
            {
                Console.Error.WriteLine($"rezrov-gui: {_path}: this program plays Glulx games so far.");
                return false;
            }

            _bytes = executable.Data.ToArray();
            _resources = packaged;
            return true;
        }

        if (StoryFormatDetector.Detect(bytes) != StoryFormat.Glulx)
        {
            Console.Error.WriteLine($"rezrov-gui: {_path}: this program plays Glulx games so far; use rezrov or rezrov-tui.");
            return false;
        }

        _bytes = bytes;
        _resources = Beside(blorb);
        return true;
    }

    private static BlorbFile? Beside(string? blorb)
    {
        var named = blorb is not null
            ? Path.GetFullPath(blorb)
            : Extensions
                .Select(extension => Path.ChangeExtension(Path.GetFullPath(_path), extension))
                .FirstOrDefault(File.Exists);

        return named is not null && File.Exists(named) ? ReadBlorb(File.ReadAllBytes(named)) : null;
    }

    private static BlorbFile? ReadBlorb(byte[] bytes)
    {
        try
        {
            return BlorbFile.Read(bytes);
        }
        catch (InvalidDataException e)
        {
            Console.Error.WriteLine($"rezrov-gui: the resource file could not be read: {e.Message}");
            return null;
        }
    }

    private static void Help(TextWriter to)
    {
        to.WriteLine("""
            usage: rezrov-gui <story file> [options]

              --blorb <file>    take the pictures and sounds from this resource file
              --seed <number>   start the game's random numbers from here
              --version         print the version and leave
              --probe           print what the fonts measure and leave
              --help            print this and leave

            Glulx games only so far, from a .ulx or a .gblorb. The command
            line and terminal programs play the Z-machine as well.
            """);
    }

    /// <summary>
    /// Starts the game once the window is up and its size is known.
    /// </summary>
    private static void Start(Window window, Board board, Glyphs glyphs)
    {
        var memory = new GlulxMemory(_bytes);
        var display = new GuiGlkDisplay(
            glyphs,
            () => Dispatcher.UIThread.Post(board.InvalidateVisual),
            board.Bounds.Width,
            board.Bounds.Height);

        board.Display = display;

        // [glk op:fileref_create_by_prompt] Asking the player for a file
        // waits for the dialogs, so until then a game that asks is told
        // no file was chosen and carries on.
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path)) ?? Directory.GetCurrentDirectory();
        var files = new DiskGlkFileSystem(directory, (_, _) => null);
        var library = new GlkLibrary(display, files) { Resources = _resources };
        var machine = new GlulxMachine(memory, _seed is { } s ? new GlulxRandom((uint)s) : null, library);

        var worker = new Thread(() =>
        {
            try
            {
                machine.Run();
            }
            catch (NotSupportedException e)
            {
                Console.Error.WriteLine($"rezrov-gui: stopped: {e.Message}");
                _result = 3;
            }
            catch (GlulxException e)
            {
                Console.Error.WriteLine($"rezrov-gui: error: {e.Message}");
                _result = 3;
            }
            catch (EndOfStreamException)
            {
                // The game waited on nothing, which is its end.
            }
            finally
            {
                library.CloseFiles();
            }

            Dispatcher.UIThread.Post(window.Close);
        })
        {
            IsBackground = true,
            Name = "interpreter",
        };

        worker.Start();
    }

    /// <summary>The application the toolkit runs.</summary>
    private sealed class GameApp : Application
    {
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var glyphs = new Glyphs();
                var board = new Board(glyphs);
                var window = new Window
                {
                    Title = $"{Path.GetFileName(_path)} - rezrov",
                    Width = 800,
                    Height = 600,
                    Content = board,
                };

                window.Opened += (_, _) =>
                {
                    board.Focus();
                    Start(window, board, glyphs);
                };

                desktop.MainWindow = window;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}

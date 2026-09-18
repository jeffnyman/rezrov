using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using Rezrov.Core;
using Rezrov.Core.Audio;
using Rezrov.Core.Blorb;
using Rezrov.Glulx;
using Rezrov.Glulx.Execution;
using Rezrov.Glulx.Glk;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Sound;
using ZMemory = Rezrov.ZMachine.ZMemory;

namespace Rezrov.Gui;

/// <summary>
/// The graphical frontend: the interpreter on its own thread, the
/// toolkit on the main one, and the Glk display between them.
/// </summary>
/// <remarks>
/// Both machines play here, each against the seam the core already has
/// for it: a Glulx game through the Glk library's windows, graphics,
/// and sound, and a Z-machine game through the screen model and its
/// Version 6 windows. The same control draws either of them.
///
/// The interpreter waits on the toolkit when it asks the player for a
/// file, and the toolkit never waits on the interpreter, so the
/// dependency runs one way and neither can be left holding the other
/// up.
/// </remarks>
internal static class Program
{
    /// <summary>
    /// [blorb 5] What a resource file beside a bare game is called.
    /// </summary>
    private static readonly string[] Extensions = [".blb", ".blorb"];

    /// <summary>
    /// How much of the screen to leave around the window when there is
    /// not room for the size the text would like.
    /// </summary>
    private const double Margin = 64;

    private static string _path = "";
    private static byte[] _bytes = [];
    private static StoryFormat _format;
    private static BlorbFile? _resources;
    private static int? _seed;
    private static InterpreterNumber? _machine;
    private static bool _tandy;
    private static string _prose = Glyphs.ProseFamily;
    private static string _fixed = Glyphs.FixedFamily;
    private static double _size = Glyphs.OrdinarySize;
    private static TextRenderingMode _smoothing = TextRenderingMode.SubpixelAntialias;
    private static string? _blorb;
    private static int _result;
    private static string? _trouble;
    private static AudioEngine? _audio;

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

        // The probe takes the same font options as a game does, so a
        // player can measure a family before playing in it.
        if (args.Length > 0 && args[0] == "--probe")
        {
            if (!Options(args, 1))
            {
                Help(Console.Error);
                return 2;
            }

            return Probe();
        }

        if (args.Length < 1 || !Options(args, 1))
        {
            Help(Console.Error);
            return 2;
        }

        _path = args[0];
        if (!Load(_blorb))
        {
            _result = 1;
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
    /// Reads the options that follow the first argument, and says
    /// whether they all made sense.
    /// </summary>
    private static bool Options(string[] args, int from)
    {
        for (var i = from; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--blorb" when i + 1 < args.Length:
                    _blorb = args[++i];
                    break;
                case "--seed" when i + 1 < args.Length && int.TryParse(args[i + 1], out var seed) && seed >= 1:
                    _seed = seed;
                    i++;
                    break;
                case "--interpreter" when i + 1 < args.Length && InterpreterNumbers.TryParse(args[i + 1], out var number):
                    _machine = number;
                    i++;
                    break;
                case "--tandy":
                    _tandy = true;
                    break;
                case "--font" when i + 1 < args.Length:
                    _prose = args[++i];
                    break;
                case "--fixed" when i + 1 < args.Length:
                    _fixed = args[++i];
                    break;
                case "--size" when i + 1 < args.Length && Pixels(args[i + 1]) is { } size:
                    _size = size;
                    i++;
                    break;
                case "--smoothing" when i + 1 < args.Length && Smoothing(args[i + 1]) is { } mode:
                    _smoothing = mode;
                    i++;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A size in pixels, which has to be large enough to read and small
    /// enough to leave room for a line of it.
    /// </summary>
    private static double? Pixels(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
        && size is >= 6 and <= 72
            ? size
            : null;

    /// <summary>
    /// How the glyphs are rasterized, which is the one part of the look
    /// of the text that choosing a font cannot settle.
    /// </summary>
    private static TextRenderingMode? Smoothing(string value) => value switch
    {
        "subpixel" => TextRenderingMode.SubpixelAntialias,
        "grayscale" or "greyscale" => TextRenderingMode.Antialias,
        "none" => TextRenderingMode.Alias,
        _ => null,
    };

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
        var glyphs = new Glyphs(_size, _prose, _fixed);

        Console.WriteLine($"prose: {_prose}");
        Console.WriteLine($"fixed: {_fixed}");
        Console.WriteLine($"size: {_size}, smoothing: {_smoothing}");
        var page = Page(glyphs);
        Console.WriteLine($"cell: {glyphs.CellWidth} by {glyphs.CellHeight}");
        Console.WriteLine(
            $"page: {Screenful.Columns} by {Screenful.Rows} characters, {page.Width} by {page.Height} pixels, "
            + $"or as much of that as the screen has room for");

        foreach (var style in new[] { GlkStyle.Normal, GlkStyle.Emphasized, GlkStyle.Preformatted, GlkStyle.Header })
        {
            var space = glyphs.Width(" ", style);
            var word = glyphs.Width("brown", style);
            var both = glyphs.Width("brown fox", style);
            var fox = glyphs.Width("fox", style);

            // A space has to be worth something, and the whole has to be
            // the sum of its parts, or the words will not line up.
            var adds = Math.Abs(word + space + fox - both) < 0.5;

            // [glk #graphics_textbuf] The baseline is what the pieces
            // of a line are stood on and what a picture in the run of
            // the text is aligned against, so it has to fall somewhere
            // inside the line rather than at the top of it or past the
            // bottom.
            var height = glyphs.LineHeight(style);
            var baseline = glyphs.Baseline(style);
            var sits = baseline > 0 && baseline <= height;

            Console.WriteLine(
                $"{style}: space {space:F2}, line {height:F2}, "
                + $"baseline {baseline:F2}, "
                + $"\"brown fox\" {both:F2}, parts add up {adds}");

            if (space <= 0 || !adds || !sits)
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
            return Trouble($"{_path}: no such file.");
        }

        var bytes = File.ReadAllBytes(_path);

        // [blorb 5] A packaged game carries its own resources; a bare
        // one may have a resource file beside it.
        if (StoryFormatDetector.Detect(bytes) == StoryFormat.Blorb)
        {
            var packaged = ReadBlorb(bytes);
            if (packaged?.Executable is not { } executable)
            {
                return Trouble($"{Path.GetFileName(_path)} is a resource file with no game in it. The game is the story file beside it, and this finds the resources from that.");
            }

            _bytes = executable.Data.ToArray();
            _resources = packaged;
            _format = executable.ChunkType == "GLUL" ? StoryFormat.Glulx : StoryFormat.ZMachine;
            return true;
        }

        _format = StoryFormatDetector.Detect(bytes);
        if (_format is not (StoryFormat.Glulx or StoryFormat.ZMachine))
        {
            return Trouble($"{Path.GetFileName(_path)} is not a story file this can play.");
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

    /// <summary>
    /// Remembers what went wrong and says so on the error stream.
    /// </summary>
    /// <remarks>
    /// A window program on Windows has no console to write to when it
    /// is started from one, so a complaint printed here and nowhere
    /// else would leave the player with a program that did nothing at
    /// all. It is shown in a window as well.
    /// </remarks>
    private static bool Trouble(string what)
    {
        _trouble = what;
        Console.Error.WriteLine($"rezrov-gui: {what}");
        return false;
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
        var names = InterpreterNumbers.AllNames.ToList();
        to.WriteLine($"""
            usage: rezrov-gui <story file> [options]

              --blorb <file>           take the pictures and sounds from this resource file
              --seed <number>          start the game's random numbers from here
              --interpreter <machine>  tell the game which machine it is running on
              --tandy                  set the Tandy bit for a Version 1 to 3 game
              --font <family>          set the prose in this family
              --fixed <family>         set the grids and preformatted text in this one
              --size <pixels>          the size of ordinary text, from 6 to 72
              --smoothing <s>          subpixel, grayscale, or none
              --version                print the version and leave
              --probe                  print what the fonts measure and leave
              --help                   print this and leave

            machines: {string.Join(", ", names.Take(6))},
                      {string.Join(", ", names.Skip(6))}, or a number from 1 to 11

            A family may be a list, in which case the first of them the
            machine actually has is the one used. The defaults are:

              --font "Georgia, Palatino, Times New Roman, serif"
              --fixed "Consolas, Menlo, DejaVu Sans Mono, monospace"
              --size 16 --smoothing subpixel

            Both machines: a Z-machine game from a .z3 to a .z8 or a
            .zblorb, and a Glulx game from a .ulx or a .gblorb.
            """);
    }

    /// <summary>
    /// How large to open the window: a comfortable page of text, or as
    /// much of one as the screen has room for.
    /// </summary>
    /// <remarks>
    /// The size is worked out in characters rather than in pixels,
    /// since characters are what the window holds. A hundred and twenty
    /// columns is a wide but readable measure for prose, and forty rows
    /// leaves a screenful of it under [zm 8.8.6] the artwork a Version 6
    /// game draws above. A screen with less room than that gives what it
    /// has, less a margin so the window does not open edge to edge, and
    /// the floor is the smallest the games are playable in rather than
    /// the smallest a window can be.
    ///
    /// A player who wants another size resizes the window, and both
    /// machines are told about it: [glk #arrange_events] a Glk game
    /// lays its windows out again, and [zm 8.4] a Z-machine game is
    /// told its screen changed.
    /// </remarks>
    private static (double Width, double Height) Opening(Window window, Glyphs glyphs)
    {
        var wanted = Page(glyphs);
        var least = (
            Width: glyphs.CellWidth * (Screenful.Columns / 2),
            Height: glyphs.CellHeight * (Screenful.Rows / 2));

        if (window.Screens?.Primary is { } screen)
        {
            // The working area is in the screen's own pixels and a
            // window is measured in the toolkit's, which are the same
            // pixels divided by whatever the display is scaled by.
            var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
            var room = (
                Width: (screen.WorkingArea.Width / scaling) - Margin,
                Height: (screen.WorkingArea.Height / scaling) - Margin);

            wanted = (
                Math.Max(Math.Min(wanted.Width, room.Width), least.Width),
                Math.Max(Math.Min(wanted.Height, room.Height), least.Height));
        }

        var (columns, rows) = Screenful.Fit(
            wanted.Width,
            wanted.Height,
            glyphs.CellWidth,
            glyphs.CellHeight,
            Drawn());

        return (columns * glyphs.CellWidth, rows * glyphs.CellHeight);
    }

    /// <summary>
    /// [blorb 11.2] The shape of screen a game's artwork was drawn for,
    /// as a width divided by a height, or null for a game that says
    /// nothing about it.
    /// </summary>
    private static double? Drawn()
    {
        if (_format != StoryFormat.ZMachine
            || _bytes.Length == 0
            || _bytes[0] != (byte)ZMachineVersion.V6
            || _resources is not { } resources)
        {
            return null;
        }

        try
        {
            return BlorbPictures.From(resources).StandardWindow is { Height: > 0 } standard
                ? (double)standard.Width / standard.Height
                : null;
        }
        catch (InvalidDataException)
        {
            // A malformed picture header is the catalog's problem to
            // complain about later, not a reason to fail to open.
            return null;
        }
    }

    /// <summary>
    /// The size a page of text comes to, before the screen has a say.
    /// </summary>
    private static (double Width, double Height) Page(Glyphs glyphs) =>
        (glyphs.CellWidth * Screenful.Columns, glyphs.CellHeight * Screenful.Rows);

    /// <summary>
    /// Starts the game once the window is up and its size is known.
    /// </summary>
    private static void Start(Window window, Board board, Glyphs glyphs)
    {
        if (_format == StoryFormat.ZMachine)
        {
            StartZMachine(window, board, glyphs);
            return;
        }

        if (_machine is not null)
        {
            Console.Error.WriteLine("rezrov-gui: the interpreter option does not apply to Glulx");
        }

        if (_tandy)
        {
            Console.Error.WriteLine("rezrov-gui: the tandy option does not apply to Glulx");
        }

        var memory = new GlulxMemory(_bytes);
        var display = new GuiGlkDisplay(
            glyphs,
            () => Dispatcher.UIThread.Post(board.InvalidateVisual),
            board.Bounds.Width,
            board.Bounds.Height);

        board.Display = display;

        // [glk op:fileref_create_by_prompt] The player is asked for a
        // file through the toolkit's own dialogs.
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path)) ?? Directory.GetCurrentDirectory();
        var dialogs = new GuiFiles(window, directory);
        var files = new DiskGlkFileSystem(directory, dialogs.AskForGlkFile);

        // [glk #sound] The machine's audio output, or nothing on a
        // machine with none, which the gestalt answers then report.
        var library = new GlkLibrary(display, files, sound: new EngineGlkSound(_audio))
        {
            Resources = _resources,
        };
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

    /// <summary>
    /// [zm 8] The other machine: one grid of cells, which the screen
    /// model wraps and pages for, and whose size is the window's own
    /// divided by a character.
    /// </summary>
    private static void StartZMachine(Window window, Board board, Glyphs glyphs)
    {
        var memory = new ZMemory(_bytes);
        var header = new StoryHeader(memory);

        var columns = Math.Max((int)(board.Bounds.Width / glyphs.CellWidth), 1);
        var rows = Math.Max((int)(board.Bounds.Height / glyphs.CellHeight), 1);

        // The screen needs the input for the [MORE] key and the input
        // needs the screen for its echo, so each reaches the other
        // through a variable filled in a moment later.
        BufferedInput? input = null;
        var screen = new BufferedScreen(
            columns,
            rows,
            cursorStartsAtBottom: header.Version <= ZMachineVersion.V4,
            repaint: () => Dispatcher.UIThread.Post(board.InvalidateVisual),
            waitForKey: () => input!.WaitForAnyKey(),

            // [zm 8.8.1] A screen of pixels measures in pixels, so a
            // unit is a pixel and a character is as many of them as the
            // font makes it. That is what the Version 6 games were drawn
            // for.
            fontWidth: (int)glyphs.CellWidth,
            fontHeight: (int)glyphs.CellHeight,

            // [zm 16] The character graphics font is shown as the
            // nearest Unicode characters, as on the terminal, and
            // [zm 8.8.6] pictures are drawn, which is what a Version 6
            // game has been waiting to hear. Saying so sends Infocom's
            // Version 6 games down their graphical paths instead of
            // their text-only ones, and [zm 11.1.3.1] makes the
            // interpreter report a machine that had pictures.
            capabilities: ScreenCapabilities.StatusLine | ScreenCapabilities.UpperWindow
                | ScreenCapabilities.Colors | ScreenCapabilities.Bold | ScreenCapabilities.Italic
                | ScreenCapabilities.FixedPitch | ScreenCapabilities.FixedGrid
                | ScreenCapabilities.CharacterGraphicsFont | ScreenCapabilities.Pictures);

        // [zm 10.3.2] Clicks are reported in screen units, which are
        // cells before Version 6 and the font's size in Version 6.
        input = header.Version == ZMachineVersion.V6
            ? new BufferedInput(screen, screen.FontWidth, screen.FontHeight)
            : new BufferedInput(screen);

        board.Screen = screen;
        board.Keys = input;

        var pictures = new GuiPictures(_resources);
        board.Pictures = pictures;

        var directory = Path.GetDirectoryName(Path.GetFullPath(_path)) ?? Directory.GetCurrentDirectory();
        var interpreter = new Interpreter(
            memory,
            screen,
            input,
            _seed is { } s ? new RandomGenerator(s) : null,
            files: new GuiFiles(window, directory),

            // [zm 9.1] The machine's audio output, or nothing on a
            // machine with none, which the header then tells the game.
            sound: new EngineSound(_audio),

            // [zm 11.1.3] The machine the game is told it is running on
            // and [zm 11.1] the Tandy bit, for the games that read one
            // or the other and behave differently.
            interpreterNumber: _machine,
            tandy: _tandy);

        // The title screen of the one story that has one the game
        // never draws: the picture fills the window until the player
        // presses a key. Nothing is offered when the resource file it
        // lives in is missing, since the game would then wait for a key
        // with nothing on the screen to explain why.
        var title = TitleScreen.Picture(interpreter.Header);
        if (title != 0 && pictures.Has(title))
        {
            interpreter.ShowTitle = picture =>
            {
                board.Title = picture;
                Dispatcher.UIThread.Post(board.InvalidateVisual);
                input.WaitForAnyKey();
                board.Title = 0;
                Dispatcher.UIThread.Post(board.InvalidateVisual);
            };
        }

        if (_resources is not null)
        {
            try
            {
                interpreter.UseResources(_resources);
            }
            catch (InvalidDataException e)
            {
                // [blorb 6] Complain righteously, then carry on without.
                Console.Error.WriteLine($"rezrov-gui: {e.Message}");
            }
        }

        var worker = new Thread(() =>
        {
            try
            {
                interpreter.Run();
            }
            catch (Exception e) when (e is NotSupportedException or InvalidDataException)
            {
                Console.Error.WriteLine($"rezrov-gui: stopped: {e.Message}");
                _result = 3;
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
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && _trouble is { } trouble)
            {
                desktop.MainWindow = new Window
                {
                    Title = "rezrov",
                    SizeToContent = SizeToContent.WidthAndHeight,
                    CanResize = false,
                    Content = new TextBlock
                    {
                        Text = trouble,
                        Margin = new Thickness(24),
                        MaxWidth = 460,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    },
                };
            }
            else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime playing)
            {
                var glyphs = new Glyphs(_size, _prose, _fixed);
                var board = new Board(glyphs, _smoothing);
                var window = new Window
                {
                    Title = $"{Path.GetFileName(_path)} - rezrov",
                    Content = board,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                };

                var (width, height) = Opening(window, glyphs);
                window.Width = width;
                window.Height = height;

                window.Opened += (_, _) =>
                {
                    board.Focus();
                    _audio = AudioEngine.Create();
                    Start(window, board, glyphs);
                };

                window.Closed += (_, _) =>
                {
                    _audio?.Dispose();
                    _audio = null;
                };

                playing.MainWindow = window;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}

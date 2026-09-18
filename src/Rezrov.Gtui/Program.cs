using Rezrov.Core;
using Rezrov.Core.Blorb;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Screen;

namespace Rezrov.Gtui;

/// <summary>
/// The grid frontend: a window this program opens for itself, with a
/// grid of characters drawn into it from a font of its own.
/// </summary>
/// <remarks>
/// The four programs are meant to be read in order. The command line
/// program shows what an interpreter needs at its barest, a stream of
/// text. The terminal program adds the screen model and lets the
/// terminal do the drawing. The graphical program hands the window and
/// the drawing to a toolkit. This one is the same game in a window with
/// nothing underneath it: no package is referenced, the window comes
/// from the operating system directly, and every pixel is one this
/// program decided on.
///
/// The interpreter runs on a thread of its own, because the thread that
/// makes a window is the thread that has to answer its messages, and a
/// game waiting for a key must not stop the window from painting or
/// closing. The two meet at exactly two places: the game fills a screen
/// of characters and asks for a repaint, and the window hands keys to
/// the input queue.
/// </remarks>
internal static class Program
{
    // A comfortable page to start at. The window can be any size after
    // that, and the grid is worked out from whatever the drawing area
    // turns out to be.
    private const int Columns = 80;
    private const int Rows = 30;

    private static int? _seed;
    private static InterpreterNumber? _machine;
    private static bool _tandy;

    internal static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args is ["--version"])
        {
            Console.WriteLine($"rezrov-gtui {ProgramVersion.Current}");
            return 0;
        }

        if (args is ["--help"] or ["-h"])
        {
            Help(Console.Out);
            return 0;
        }

        if (args is ["--glyphs"])
        {
            Glyphs(Console.Out);
            return 0;
        }

        if (args is ["--glyphs", var specimen])
        {
            Specimen(Console.Out, specimen);
            return 0;
        }

        if (args.Length < 1 || !Options(args, 1))
        {
            Help(Console.Error);
            return 2;
        }

        return Play(args[0]);
    }

    /// <summary>
    /// Reads the options that follow the story file, and says whether
    /// they all made sense.
    /// </summary>
    private static bool Options(string[] args, int from)
    {
        for (var i = from; i < args.Length; i++)
        {
            switch (args[i])
            {
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
                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Opens a window and plays the game in it.
    /// </summary>
    private static int Play(string path)
    {
        if (Story(path) is not { } bytes)
        {
            return 1;
        }

        var memory = new ZMemory(bytes);
        var header = new StoryHeader(memory);

        using var window = new Win32Window();
        window.Open(
            $"{Path.GetFileName(path)} - rezrov",
            Columns * Paint.CellWidth,
            Rows * Paint.CellHeight);

        var (columns, rows) = Paint.Fits(window.Surface.Width, window.Surface.Height);

        // The screen needs the input for the [MORE] key and the input
        // needs the screen for its echo, so each reaches the other
        // through a variable filled in a moment later.
        BufferedInput? input = null;
        var screen = new BufferedScreen(
            columns,
            rows,
            cursorStartsAtBottom: header.Version <= ZMachineVersion.V4,
            repaint: window.Redraw,
            waitForKey: () => input!.WaitForAnyKey(),

            // [zm 8.8.1] A screen of pixels measures in pixels, and a
            // character is as many of them as the font makes it.
            fontWidth: Paint.CellWidth,
            fontHeight: Paint.CellHeight,

            // [zm 8.1.2] The character graphics font is drawn from the
            // shapes the standard gives, which this program carries, so
            // it says it has one. [zm 8.8.6] Pictures are another
            // matter and it does not draw them yet, so it does not
            // claim them and a Version 6 game takes its text path.
            capabilities: ScreenCapabilities.StatusLine | ScreenCapabilities.UpperWindow
                | ScreenCapabilities.Colors | ScreenCapabilities.Bold | ScreenCapabilities.Italic
                | ScreenCapabilities.FixedPitch | ScreenCapabilities.FixedGrid
                | ScreenCapabilities.CharacterGraphicsFont);

        input = new BufferedInput(screen);

        // Painting happens on the thread that owns the window while the
        // game fills the screen on its own, so the screen is read under
        // its lock rather than caught halfway through a line.
        window.Painting = surface =>
        {
            lock (screen.Sync)
            {
                Paint.Screen(surface, screen);
            }
        };

        window.Typed = character =>
        {
            if (GridKeys.FromCharacter(character) is var zscii and not 0)
            {
                input.Enqueue(zscii);
            }
        };

        window.Pressed = key =>
        {
            if (GridKeys.FromKey(key) is var zscii and not 0)
            {
                input.Enqueue(zscii);
            }
        };

        // [zm 8.4] A window that changes size changes the screen, and
        // the game is told so it can lay its own windows out again.
        window.Resized = () =>
        {
            var (across, down) = Paint.Fits(window.Surface.Width, window.Surface.Height);
            screen.Resize(across, down);
        };

        var result = 0;
        var interpreter = new Interpreter(
            memory,
            screen,
            input,
            _seed is { } seed ? new RandomGenerator(seed) : null,
            interpreterNumber: _machine,
            tandy: _tandy);

        var worker = new Thread(() =>
        {
            try
            {
                interpreter.Run();
            }
            catch (Exception e) when (e is NotSupportedException or InvalidDataException)
            {
                Console.Error.WriteLine($"rezrov-gtui: stopped: {e.Message}");
                result = 3;
            }

            window.Close();
        })
        {
            IsBackground = true,
            Name = "interpreter",
        };

        worker.Start();
        window.Run();

        return result;
    }

    /// <summary>
    /// The story itself, taken out of a resource file when the game is
    /// packaged in one, or null after saying what was wrong with it.
    /// </summary>
    private static byte[]? Story(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"rezrov-gtui: no such file: {path}");
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        var format = StoryFormatDetector.Detect(bytes);

        // [blorb 5] A packaged game carries the story inside it.
        if (format == StoryFormat.Blorb)
        {
            try
            {
                if (BlorbFile.Read(bytes).Executable is not { ChunkType: "ZCOD" } executable)
                {
                    Console.Error.WriteLine(
                        $"rezrov-gtui: {Path.GetFileName(path)} has no Z-machine game in it.");
                    return null;
                }

                return executable.Data.ToArray();
            }
            catch (InvalidDataException e)
            {
                Console.Error.WriteLine($"rezrov-gtui: the resource file could not be read: {e.Message}");
                return null;
            }
        }

        if (format != StoryFormat.ZMachine)
        {
            Console.Error.WriteLine(
                $"rezrov-gtui: {Path.GetFileName(path)} is not a Z-machine story, and this program plays that machine only.");
            return null;
        }

        return bytes;
    }

    private static void Help(TextWriter to)
    {
        var names = InterpreterNumbers.AllNames.ToList();
        to.WriteLine($"""
            usage: rezrov-gtui <story file> [options]
                   rezrov-gtui --glyphs [text]
                   rezrov-gtui --version
                   rezrov-gtui --help

            Plays a Z-machine game in a window of its own, as a grid of
            characters drawn from a font this program carries. Nothing is
            used that did not come with the system: the window, the keys,
            and every pixel are this program's own doing.

            options:
              --seed <number>          seed the game's random numbers
              --interpreter <machine>  tell the game which machine it is on
              --tandy                  set the Tandy bit for a Version 1 to 3 game

            machines: {string.Join(", ", names.Take(6))},
                      {string.Join(", ", names.Skip(6))}, or a number from 1 to 11

            Saving and restoring are not wired up here yet, and neither
            are sounds or pictures. The other three programs have them.

            The fonts can be read without playing anything:

              --glyphs          print both fonts, character by character
              --glyphs <text>   print that text in the ordinary font
            """);
    }

    /// <summary>
    /// Both fonts, character by character. [zm 16.1] Font 3 is laid out
    /// four to a group the way the standard lays it out, so the program
    /// and the document can be read side by side, and the ordinary font
    /// follows in the same shape.
    /// </summary>
    private static void Glyphs(TextWriter to)
    {
        Table(
            to,
            $"font 3: {CharacterGraphics.Width} by {CharacterGraphics.Height}, codes 32 to 126",
            CharacterGraphics.Height,
            code => CharacterGraphics.Bitmap(code));

        Table(
            to,
            $"ordinary text: {TextFont.Width} by {TextFont.Height}, codes 32 to 126",
            TextFont.Height,
            TextFont.Bitmap);
    }

    /// <summary>
    /// A line of text in the ordinary font, which is the only way to
    /// tell whether the shapes read well together.
    /// </summary>
    private static void Specimen(TextWriter to, string text)
    {
        for (var row = 0; row < TextFont.Height; row++)
        {
            var line = new char[text.Length * TextFont.Width];

            for (var at = 0; at < text.Length; at++)
            {
                var bits = TextFont.Bitmap(text[at])[row];

                for (var cell = 0; cell < TextFont.Width; cell++)
                {
                    line[(at * TextFont.Width) + cell] =
                        (bits & (1 << (TextFont.Width - 1 - cell))) != 0 ? '#' : ' ';
                }
            }

            to.WriteLine(new string(line).TrimEnd());
        }
    }

    private static void Table(TextWriter to, string title, int height, Func<char, ReadOnlySpan<byte>> font)
    {
        const int Across = 4;

        to.WriteLine(title);
        to.WriteLine();

        for (var first = ' '; first <= '~'; first = (char)(first + Across))
        {
            var group = new List<char>();

            for (var code = first; code <= '~' && code < first + Across; code++)
            {
                group.Add(code);
            }

            to.WriteLine(string.Join(
                "   ",
                group.Select(code => $"{(int)code,3}({code}):  76543210")));

            for (var row = 0; row < height; row++)
            {
                to.WriteLine("       " + string.Join(
                    "   ",
                    group.Select((code, at) =>
                        (at == 0 ? string.Empty : "      ") + $"{row,2}" + Cells(font(code)[row]))));
            }

            to.WriteLine();
        }
    }

    /// <summary>
    /// One row of a character, a hash for a cell that is on. [zm 16.1]
    /// The leftmost cell is the top bit, in both fonts.
    /// </summary>
    private static string Cells(byte row)
    {
        var cells = new char[CharacterGraphics.Width];

        for (var cell = 0; cell < cells.Length; cell++)
        {
            cells[cell] = (row & (1 << (CharacterGraphics.Width - 1 - cell))) != 0 ? '#' : '.';
        }

        return new string(cells);
    }
}

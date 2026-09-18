using Rezrov.Core;
using Rezrov.ZMachine.Screen;

namespace Rezrov.Gtui;

/// <summary>
/// The grid frontend, which is not yet a frontend: so far it is the
/// font it will draw with, and a way to look at it.
/// </summary>
/// <remarks>
/// A frontend that owns its pixels has to answer a question the other
/// two never face, which is what a character actually looks like. The
/// console hands that to the terminal and the graphical program hands
/// it to Avalonia. This one has to draw it, so it starts with the two
/// fonts it needs: [zm 16.1] font 3, the character graphics font, whose
/// shapes the standard prints in full, and an ordinary font for
/// everything else, which is drawn here rather than taken from
/// anywhere.
/// </remarks>
internal static class Program
{
    internal static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args is ["--version"])
        {
            Console.WriteLine($"rezrov-gtui {ProgramVersion.Current}");
            return 0;
        }

        if (args is ["--glyphs"])
        {
            Glyphs(Console.Out);
            return 0;
        }

        Help(args is ["--help"] or ["-h"] ? Console.Out : Console.Error);
        return args is ["--help"] or ["-h"] ? 0 : 2;
    }

    private static void Help(TextWriter to)
    {
        to.WriteLine("""
            usage: rezrov-gtui --glyphs
                   rezrov-gtui --version
                   rezrov-gtui --help

            The grid frontend, which cannot play a game yet. What it has
            so far is the font it will draw with.

              --glyphs    print the font as the standard prints it
              --version   print the version and leave
              --help      print this and leave
            """);
    }

    /// <summary>
    /// [zm 16.1] Font 3, laid out four characters to a group the way
    /// the standard lays it out, so the two can be read side by side.
    /// </summary>
    private static void Glyphs(TextWriter to)
    {
        const int Across = 4;

        to.WriteLine($"font 3: {CharacterGraphics.Width} by {CharacterGraphics.Height}, codes 32 to 126");
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

            for (var row = 0; row < CharacterGraphics.Height; row++)
            {
                to.WriteLine("        " + string.Join(
                    "   ",
                    group.Select((code, at) =>
                        (at == 0 ? string.Empty : "      ") + row + Cells(CharacterGraphics.Bitmap(code)[row]))));
            }

            to.WriteLine();
        }
    }

    /// <summary>
    /// One row of a character, a hash for a cell that is on. [zm 16.1]
    /// The leftmost cell is the top bit.
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

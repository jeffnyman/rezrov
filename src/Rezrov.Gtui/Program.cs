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

        if (args is ["--glyphs", var specimen])
        {
            Specimen(Console.Out, specimen);
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
            so far is the fonts it will draw with: [zm 16.1] font 3, the
            character graphics font, whose shapes the standard gives,
            and the ordinary font, which is drawn in this program.

              --glyphs          print both fonts, character by character
              --glyphs <text>   print that text in the ordinary font
              --version         print the version and leave
              --help            print this and leave

            The shapes of the ordinary font were drawn by eye and no
            test can say whether they look right, so the way to judge
            them is to read them:

              rezrov-gtui --glyphs "The quick brown fox"
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

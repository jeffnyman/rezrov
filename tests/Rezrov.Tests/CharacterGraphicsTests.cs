using System.Globalization;
using System.Text.RegularExpressions;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Screen;
using static Rezrov.Tests.Assembler;

namespace Rezrov.Tests;

/// <summary>
/// [zm 16] Font 3 as the nearest Unicode characters, and how a game
/// gets it.
/// </summary>
public class CharacterGraphicsTests
{
    [Theory]
    [InlineData('(', '│')]
    [InlineData('&', '─')]
    [InlineData('/', '┌')]
    [InlineData('1', '┘')]
    [InlineData('[', '┼')]
    [InlineData('6', '█')]
    [InlineData('9', '▌')]
    [InlineData('"', '→')]
    [InlineData('\\', '↑')]
    [InlineData('a', 'ᚪ')]
    [InlineData('z', 'ᛟ')]
    [InlineData(' ', ' ')]
    public void EachFont3CharacterHasANearestUnicodeShape(char code, char shape)
    {
        // [zm 16.1] Lines, corners, blocks, arrows, and runes.
        Assert.Equal(shape, CharacterGraphics.ToUnicode(code));
    }

    [Fact]
    public void CodesOutsideTheFontComeBackAsTheyAre()
    {
        Assert.Equal('é', CharacterGraphics.ToUnicode('é'));
        Assert.Equal('', CharacterGraphics.ToUnicode(''));
    }
}

public partial class InterpreterTests
{
    [Fact]
    public void Font3IsOfferedWhereTheFrontendCanShowItAndDrawnAsUnicode()
    {
        var screen = new RecordingScreen(20, 5, ScreenCapabilities.FixedGrid | ScreenCapabilities.CharacterGraphicsFont);
        var offered = Execute(
            new Assembler()
                .Ext(4, Small(3)).Store(G0)
                .Variable(Op.PrintChar, true, Small('('))
                .Ext(4, Small(1)).Store(G1)
                .Variable(Op.PrintChar, true, Small('('))
                .Quit(),
            screen: screen);

        // [zm op:set_font] The previous font comes back, the text carries
        // the font, and [zm 8.1.5.1] the header keeps its bit 3 in
        // Version 5.
        Assert.Equal(1, offered.Global(G0));
        Assert.Equal(3, offered.Global(G1));
        Assert.Equal([3, 1], screen.Runs.Select(r => r.Attributes.Font));

        var writer = new StringWriter();
        Execute(
            new Assembler()
                .Ext(4, Small(3)).Store(G0)
                .Variable(Op.PrintChar, true, Small('('))
                .Variable(Op.PrintChar, true, Small('/'))
                .Quit(),
            screen: new TextWriterScreen(writer));

        // A text stream shows the font as the nearest Unicode shapes.
        Assert.Equal("│┌", writer.ToString());
    }

    [Fact]
    public void Font3IsRefusedWhereTheFrontendCannotShowIt()
    {
        var screen = new RecordingScreen(20, 5, ScreenCapabilities.FixedGrid);
        var run = Execute(
            new Assembler()
                .Ext(4, Small(3)).Store(G0)
                .Variable(Op.PrintChar, true, Small('('))
                .Quit(),
            setup: story => story.PutWord(0x10, 0x0008),
            screen: screen);

        // [zm op:set_font] 0 for an unavailable font, the text stays in
        // font 1, and [zm 8.1.5.1] the header's bit 3 is cleared.
        Assert.Equal(0, run.Global(G0));
        Assert.Equal(1, screen.Runs.Single().Attributes.Font);
        Assert.False(run.Interpreter.Header.Flags2.HasFlag(Flags2.WantsPictures));
    }

    [Fact]
    public void TheFontIsEightByEightWithTheLeftmostCellInTheTopBit()
    {
        // [zm 16.1] Code 54 is the full block and code 32 is blank, so
        // the two of them pin both ends of the table.
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, CharacterGraphics.Bitmap('6').ToArray());
        Assert.Equal(new byte[8], CharacterGraphics.Bitmap(' ').ToArray());

        // Code 55 is the upper block, five rows of eight and then
        // nothing, which says which end of the table is the top.
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00 }, CharacterGraphics.Bitmap('7').ToArray());

        // Codes 57 and 58 are the left and right blocks, five cells of
        // the eight either way, which says which end of a row is the
        // top bit. A packing that read a row backwards would swap them.
        Assert.All(CharacterGraphics.Bitmap('9').ToArray(), row => Assert.Equal(0xF8, row));
        Assert.All(CharacterGraphics.Bitmap(':').ToArray(), row => Assert.Equal(0x1F, row));

        // Code 39 is the horizontal line drawn high, one full row with
        // nothing above or below it.
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0x00 }, CharacterGraphics.Bitmap('\'').ToArray());

        // And code 95 is the hollow box, which has an edge on all four
        // sides and so would fail under any transposition at all.
        Assert.Equal(new byte[] { 0xFF, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0xFF }, CharacterGraphics.Bitmap('_').ToArray());
    }

    [Fact]
    public void EveryCharacterOfTheFontHasAShapeAndNothingElseHasOne()
    {
        // [zm 16.1] Codes 32 and 37 are the two blanks the font
        // deliberately carries; every other code draws something.
        for (var code = ' '; code <= '~'; code++)
        {
            var rows = CharacterGraphics.Bitmap(code).ToArray();

            Assert.Equal(CharacterGraphics.Height, rows.Length);

            if (code is not (' ' or '%'))
            {
                Assert.True(rows.Any(row => row != 0), $"Code {(int)code} draws nothing.");
            }
        }

        // Outside the font there is no glyph at all, rather than some
        // other character's shape.
        Assert.Equal(new byte[8], CharacterGraphics.Bitmap('\u001F').ToArray());
        Assert.Equal(new byte[8], CharacterGraphics.Bitmap('\u007F').ToArray());
        Assert.Equal(new byte[8], CharacterGraphics.Bitmap('\u00E9').ToArray());
    }

    [Fact]
    public void TheFontMatchesTheListingInTheStandard()
    {
        // The table in the code was read out of the standard rather
        // than typed in, so this reads it again and compares. It is the
        // only check that can say the shapes are really Infocom's and
        // not a plausible invention.
        var root = Corpus.FindRepositoryRoot();
        var listing = root is null
            ? null
            : Path.Combine(root, "entharion", "specs", "Z-Machine-Standard-1.1", "sect16.html");

        Assert.SkipUnless(listing is not null && File.Exists(listing), "The standard is not beside the tests.");

        var expected = Section16(File.ReadAllText(listing!));

        Assert.Equal(95, expected.Count);

        foreach (var (code, rows) in expected)
        {
            Assert.Equal(rows, CharacterGraphics.Bitmap((char)code).ToArray());
        }
    }

    /// <summary>
    /// [zm 16.1] The standard prints font 3 four characters to a group:
    /// a line naming each code and its bit order, then eight rows of
    /// eight cells. The heading says which column each character's
    /// cells begin at, so the rows are sliced rather than counted, and
    /// every row is checked against the row number printed beside it.
    /// </summary>
    private static Dictionary<int, byte[]> Section16(string html)
    {
        var text = Regex.Replace(html, "<[^>]*>", string.Empty)
            .Replace("&amp;", "&", StringComparison.Ordinal)
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal);

        var lines = text.Split('\n');
        var found = new Dictionary<int, byte[]>();

        for (var i = 0; i < lines.Length; i++)
        {
            foreach (Match heading in Regex.Matches(lines[i], @"(\d+)\(.\):\s*76543210"))
            {
                var code = int.Parse(heading.Groups[1].Value, CultureInfo.InvariantCulture);
                var at = lines[i].IndexOf("76543210", heading.Index, StringComparison.Ordinal);
                var rows = new byte[CharacterGraphics.Height];

                for (var row = 0; row < rows.Length; row++)
                {
                    var line = lines[i + 1 + row];

                    Assert.Equal(row.ToString(CultureInfo.InvariantCulture), line.Substring(at - 1, 1));

                    var cells = line[at..].PadRight(CharacterGraphics.Width)[..CharacterGraphics.Width];
                    var bits = 0;

                    for (var cell = 0; cell < CharacterGraphics.Width; cell++)
                    {
                        Assert.True(cells[cell] is '#' or ' ', $"Code {code} row {row} has {cells[cell]} in it.");

                        if (cells[cell] == '#')
                        {
                            bits |= 1 << (CharacterGraphics.Width - 1 - cell);
                        }
                    }

                    rows[row] = (byte)bits;
                }

                found[code] = rows;
            }
        }

        return found;
    }
}

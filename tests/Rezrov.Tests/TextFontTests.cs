using Rezrov.Gtui;
using Rezrov.ZMachine.Screen;

namespace Rezrov.Tests;

/// <summary>
/// The font the grid frontend sets ordinary text in.
/// </summary>
/// <remarks>
/// Whether a letter looks like itself is not something a test can say,
/// and the program prints the font so that a person can judge it. What
/// is left is everything measurable, and the measurable parts are where
/// a hand-drawn font goes wrong: a body one row too tall, a stem in the
/// gap between characters, a letter left out.
/// </remarks>
public class TextFontTests
{
    // Everything that stands on the baseline: capitals, digits, and the
    // lowercase letters with nothing below it.
    private const string Standing =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
        + "abcehiklmnorstuvwxz"
        + "0123456789";

    // The five letters that hang below it.
    private const string Hanging = "gjpqy";

    // The letters that rise to the height of a capital.
    private const string Rising = "bdfhkl";

    private static int Lowest(char character)
    {
        var rows = TextFont.Bitmap(character);

        for (var row = rows.Length - 1; row >= 0; row--)
        {
            if (rows[row] != 0)
            {
                return row;
            }
        }

        return -1;
    }

    private static int Highest(char character)
    {
        var rows = TextFont.Bitmap(character);

        for (var row = 0; row < rows.Length; row++)
        {
            if (rows[row] != 0)
            {
                return row;
            }
        }

        return -1;
    }

    [Fact]
    public void EveryPrintableCharacterHasAShape()
    {
        for (var character = TextFont.First; character <= TextFont.Last; character++)
        {
            var rows = TextFont.Bitmap(character);

            Assert.Equal(TextFont.Height, rows.Length);

            if (character != ' ')
            {
                Assert.True(Lowest(character) >= 0, $"{character} draws nothing.");
            }
        }

        Assert.Equal(new byte[TextFont.Height], TextFont.Bitmap(' ').ToArray());
    }

    [Fact]
    public void EverythingThatStandsOnTheBaselineStandsOnTheSameOne()
    {
        // This is the check that matters most in a font drawn by hand.
        // The first draft gave the single story lowercase letters a body
        // one row too tall, so a, c, e, n, o and the rest sat a pixel
        // below the line the capitals stood on, which is the kind of
        // thing that is obvious in a specimen and invisible in a table.
        foreach (var character in Standing)
        {
            Assert.Equal(TextFont.Baseline - 1, Lowest(character));
        }
    }

    [Fact]
    public void TheDescendersHangBelowItAndNothingElseDoes()
    {
        foreach (var character in Hanging)
        {
            Assert.True(
                Lowest(character) >= TextFont.Baseline,
                $"{character} does not reach below the baseline.");
        }

        // And the cell keeps a clear row at the bottom, so a line of
        // text does not touch the line beneath it.
        for (var character = TextFont.First; character <= TextFont.Last; character++)
        {
            Assert.True(
                Lowest(character) < TextFont.Height - 1,
                $"{character} reaches the last row of the cell.");
        }
    }

    [Fact]
    public void TheLettersThatRiseReachTheHeightOfACapital()
    {
        foreach (var character in Rising)
        {
            Assert.Equal(TextFont.Cap, Highest(character));
        }

        // A lowercase body starts where it says it does, taking one of
        // the round letters, which is the shape the rest are built to.
        Assert.Equal(TextFont.Body, Highest('o'));
        Assert.Equal(TextFont.Cap, Highest('O'));
    }

    [Fact]
    public void NothingStraysIntoTheGapBetweenCharacters()
    {
        // The last column of the cell is the space to the next
        // character. A letter that reached into it would run into its
        // neighbor, and text is drawn character by character with no
        // spacing of its own, so nothing else would catch it.
        for (var character = TextFont.First; character <= TextFont.Last; character++)
        {
            foreach (var row in TextFont.Bitmap(character).ToArray())
            {
                Assert.True(
                    (row & (1 << (TextFont.Width - 1 - TextFont.Gap))) == 0,
                    $"{character} draws in the gap column.");
            }
        }
    }

    [Fact]
    public void ACharacterTheFontDoesNotDrawComesBackBlank()
    {
        Assert.Equal(new byte[TextFont.Height], TextFont.Bitmap('\u001F').ToArray());
        Assert.Equal(new byte[TextFont.Height], TextFont.Bitmap('\u007F').ToArray());
        Assert.Equal(new byte[TextFont.Height], TextFont.Bitmap('\u00E9').ToArray());
    }

    [Fact]
    public void BothFontsAreReadTheSameWay()
    {
        // One piece of drawing code serves both, so a row of either has
        // to mean the same thing: eight cells, leftmost in the top bit.
        Assert.Equal(CharacterGraphics.Width, TextFont.Width);

        // Font 3 is drawn immediately next to its neighbors, with no gap
        // column, which is exactly what makes a box join up. The
        // ordinary font keeps one, which is what makes words readable.
        Assert.Equal(0xFF, CharacterGraphics.Bitmap('6')[0]);

        // M is the widest letter there is, so if anything reaches the
        // gap column it does. Its first drawn row is a stem at either
        // side of the seven columns it is allowed.
        Assert.Equal(0x82, TextFont.Bitmap('M')[TextFont.Cap]);
    }
}

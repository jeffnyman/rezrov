using Rezrov.Gtui;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

/// <summary>
/// The grid frontend's drawing: characters into pixels.
/// </summary>
/// <remarks>
/// Nothing here needs a window. The drawing fills an array of pixels and
/// the tests read that array, which is the reason the drawing was kept
/// apart from the window in the first place.
/// </remarks>
public class GridPaintTests
{
    private static BufferedScreen Screen(int columns, int rows) =>
        new(
            columns,
            rows,
            cursorStartsAtBottom: false,
            repaint: () => { },
            waitForKey: () => Zscii.Newline,
            fontWidth: Paint.CellWidth,
            fontHeight: Paint.CellHeight,
            capabilities: ScreenCapabilities.StatusLine | ScreenCapabilities.UpperWindow
                | ScreenCapabilities.Colors | ScreenCapabilities.Bold | ScreenCapabilities.Italic
                | ScreenCapabilities.FixedPitch | ScreenCapabilities.FixedGrid
                | ScreenCapabilities.CharacterGraphicsFont);

    private static TextAttributes Plain =>
        new(TextStyle.Roman, ScreenColor.White, ScreenColor.Black, TextAttributes.NormalFont);

    // How many pixels of a cell are the ink color.
    private static int Ink(Surface surface, int column, int row, uint ink)
    {
        var count = 0;

        for (var y = 0; y < Paint.CellHeight; y++)
        {
            for (var x = 0; x < Paint.CellWidth; x++)
            {
                if (surface[(column * Paint.CellWidth) + x, (row * Paint.CellHeight) + y] == ink)
                {
                    count++;
                }
            }
        }

        return count;
    }

    [Fact]
    public void ASurfaceClipsRatherThanFallingOver()
    {
        var surface = new Surface(4, 4);

        // A game is free to ask for something half off the screen, and a
        // frontend that threw when it did would be the one at fault.
        surface.Fill(-2, -2, 3, 3, Surface.White);

        Assert.Equal(Surface.White, surface[0, 0]);
        Assert.Equal(Surface.Black, surface[1, 0]);

        surface.Fill(3, 3, 100, 100, Surface.White);
        Assert.Equal(Surface.White, surface[3, 3]);

        // And reading outside it is black rather than a mistake.
        Assert.Equal(Surface.Black, surface[-1, 0]);
        Assert.Equal(Surface.Black, surface[4, 4]);
    }

    [Fact]
    public void AGlyphShorterThanItsCellHasEveryRowDrawnTwice()
    {
        // [zm 16.1] Font 3 is eight rows and the cell is sixteen. The
        // standard has its characters printed immediately next to each
        // other in all four directions, so a box drawn out of them has
        // to join up, which it cannot do if half the cell is empty.
        var surface = new Surface(8, 16);
        byte[] solid = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

        surface.Glyph(solid, 0, 0, 8, 16, Surface.White);

        for (var y = 0; y < 16; y++)
        {
            Assert.Equal(Surface.White, surface[0, y]);
            Assert.Equal(Surface.White, surface[7, y]);
        }
    }

    [Fact]
    public void AGlyphNeverReachesPastTheRoomItIsGiven()
    {
        // The width is what stops bold and italic together from pushing
        // a character into the cell beside it.
        var surface = new Surface(16, 16);
        byte[] solid = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

        surface.Glyph(solid, 0, 0, 7, 16, Surface.White);

        Assert.Equal(Surface.White, surface[6, 0]);
        Assert.Equal(Surface.Black, surface[7, 0]);
        Assert.Equal(Surface.Black, surface[8, 0]);
    }

    [Fact]
    public void ASlantedGlyphLeansTheTopHalfAndKeepsTheBottomWhereItIs()
    {
        var surface = new Surface(16, 16);
        byte[] stem = new byte[8];
        Array.Fill(stem, (byte)0x80);

        surface.Glyph(stem, 0, 0, 8, 16, Surface.White, slant: true);

        // The upper half stands one to the right of the lower half,
        // which is the whole of what italic means in a font with one
        // shape for each character.
        Assert.Equal(Surface.White, surface[1, 0]);
        Assert.Equal(Surface.Black, surface[0, 0]);
        Assert.Equal(Surface.White, surface[0, 15]);
        Assert.Equal(Surface.Black, surface[1, 15]);
    }

    [Fact]
    public void TheGridIsWhateverWholeCharactersFitInTheWindow()
    {
        Assert.Equal((80, 30), Paint.Fits(80 * Paint.CellWidth, 30 * Paint.CellHeight));

        // A part of a character at the edge is a strip of nothing, so
        // it is not counted, and there is always at least one of them.
        Assert.Equal((80, 30), Paint.Fits((80 * Paint.CellWidth) + 7, (30 * Paint.CellHeight) + 15));
        Assert.Equal((1, 1), Paint.Fits(0, 0));
    }

    [Fact]
    public void TextIsDrawnWhereTheScreenSaysItIs()
    {
        var screen = Screen(8, 2);
        screen.Print("Hi", Plain);

        var surface = new Surface(8 * Paint.CellWidth, 2 * Paint.CellHeight);
        Paint.Screen(surface, screen);

        var white = Paint.Pixel(ScreenColor.White, Surface.White);

        Assert.True(Ink(surface, 0, 0, white) > 0, "The first character drew nothing.");
        Assert.True(Ink(surface, 1, 0, white) > 0, "The second character drew nothing.");

        // Nothing was written past them, nor on the line below.
        Assert.Equal(0, Ink(surface, 3, 0, white));
        Assert.Equal(0, Ink(surface, 0, 1, white));

        // The cell after the text is where the cursor sits, drawn as a
        // line under it two pixels deep so that it hides nothing. That
        // is the whole of what is in that cell.
        Assert.Equal(Paint.CellWidth * 2, Ink(surface, 2, 0, white));

        for (var x = 0; x < Paint.CellWidth; x++)
        {
            Assert.Equal(white, surface[(2 * Paint.CellWidth) + x, Paint.CellHeight - 1]);
            Assert.Equal(Surface.Black, surface[(2 * Paint.CellWidth) + x, Paint.CellHeight - 3]);
        }
    }

    [Fact]
    public void ReverseVideoSwapsTheTwoColorsForThatCellAlone()
    {
        // [zm 8.7.1] This is how every status line in the corpus is
        // drawn, so it had better be right.
        var screen = Screen(4, 1);
        screen.Print("A", Plain with { Style = TextStyle.ReverseVideo });
        screen.Print("A", Plain);

        var surface = new Surface(4 * Paint.CellWidth, Paint.CellHeight);
        Paint.Screen(surface, screen);

        var white = Paint.Pixel(ScreenColor.White, Surface.White);
        var black = Paint.Pixel(ScreenColor.Black, Surface.Black);

        // Reversed, the cell is mostly white with black letters; plain,
        // it is mostly black with white ones.
        Assert.True(Ink(surface, 0, 0, white) > Ink(surface, 0, 0, black), "The reversed cell is not mostly white.");
        Assert.True(Ink(surface, 1, 0, black) > Ink(surface, 1, 0, white), "The plain cell is not mostly black.");
    }

    [Fact]
    public void BoldDrawsMoreInkThanPlainAndItalicDrawsTheSameAmount()
    {
        var screen = Screen(4, 1);
        screen.Print("M", Plain);
        screen.Print("M", Plain with { Style = TextStyle.Bold });
        screen.Print("M", Plain with { Style = TextStyle.Italic });

        var surface = new Surface(4 * Paint.CellWidth, Paint.CellHeight);
        Paint.Screen(surface, screen);

        var white = Paint.Pixel(ScreenColor.White, Surface.White);

        Assert.True(
            Ink(surface, 1, 0, white) > Ink(surface, 0, 0, white),
            "Bold is no heavier than plain.");

        // Leaning a character moves ink about rather than adding any,
        // and none of it leaves the cell.
        Assert.Equal(Ink(surface, 0, 0, white), Ink(surface, 2, 0, white));
    }

    [Fact]
    public void TheCharacterGraphicsFontIsDrawnFromTheStandardsOwnShapes()
    {
        // [zm 16] Code 54 of font 3 is the full block, which no letter
        // in the ordinary font could be mistaken for: every pixel of
        // the cell is ink.
        var screen = Screen(2, 1);
        screen.Print("6", Plain with { Font = TextAttributes.CharacterGraphicsFont });
        screen.Print("6", Plain);

        var surface = new Surface(2 * Paint.CellWidth, Paint.CellHeight);
        Paint.Screen(surface, screen);

        var white = Paint.Pixel(ScreenColor.White, Surface.White);

        Assert.Equal(Paint.CellWidth * Paint.CellHeight, Ink(surface, 0, 0, white));
        Assert.True(Ink(surface, 1, 0, white) < Paint.CellWidth * Paint.CellHeight, "The digit filled the cell.");
    }

    [Fact]
    public void AColorIsTheOneTheStandardGives()
    {
        // [zm 8.3.7] Five bits a channel, spread over eight, so that
        // white comes out white rather than very nearly white.
        Assert.Equal(0x00FFFFFFu, Paint.Pixel(ScreenColor.White, 0));
        Assert.Equal(0x00000000u, Paint.Pixel(ScreenColor.Black, 0xFFFFFF));

        // Red has all of one channel and none of the others.
        var red = Paint.Pixel(ScreenColor.Red, 0);
        Assert.True((red & 0x00FF0000) > 0, "Red has no red in it.");
        Assert.Equal(0u, red & 0x0000FF00);

        // A color that is not a color at all falls back to what the
        // screen was already using.
        Assert.Equal(0x00ABCDEFu, Paint.Pixel(ScreenColor.Current, 0x00ABCDEF));
        Assert.Equal(0x00ABCDEFu, Paint.Pixel(ScreenColor.Default, 0x00ABCDEF));
    }

    [Fact]
    public void AKeyIsTakenFromTheCharacterWhenItHasOneAndTheKeyWhenItDoesNot()
    {
        // [zm 3.8] Letters come from the character, so every keyboard
        // layout works without this program knowing about layouts.
        Assert.Equal('a', GridKeys.FromCharacter('a'));
        Assert.Equal('~', GridKeys.FromCharacter('~'));
        Assert.Equal(Zscii.Newline, GridKeys.FromCharacter('\r'));
        Assert.Equal(Zscii.Newline, GridKeys.FromCharacter('\n'));
        Assert.Equal(Zscii.Delete, GridKeys.FromCharacter('\b'));
        Assert.Equal(Zscii.Escape, GridKeys.FromCharacter(''));

        // A character with no place in the Z-machine is dropped rather
        // than sent as something else.
        Assert.Equal(0, GridKeys.FromCharacter('é'));

        // [zm 3.8.3] The keys that send no character come from the key.
        Assert.Equal(Zscii.CursorUp, GridKeys.FromKey(0x26));
        Assert.Equal(Zscii.CursorDown, GridKeys.FromKey(0x28));
        Assert.Equal(Zscii.CursorLeft, GridKeys.FromKey(0x25));
        Assert.Equal(Zscii.CursorRight, GridKeys.FromKey(0x27));
        Assert.Equal(Zscii.F1, GridKeys.FromKey(0x70));
        Assert.Equal(Zscii.F12, GridKeys.FromKey(0x7B));

        // And a letter key is not taken twice, since its character has
        // already been dealt with.
        Assert.Equal(0, GridKeys.FromKey('A'));
    }

    [Fact]
    public void X11NamesTheSameKeysDifferentlyAndTheyMeanTheSameThing()
    {
        // The two window systems number their keys differently and the
        // game must not be able to tell which one it is running under.
        Assert.Equal(GridKeys.FromKey(0x26), GridKeys.FromKeySym(0xFF52));
        Assert.Equal(GridKeys.FromKey(0x28), GridKeys.FromKeySym(0xFF54));
        Assert.Equal(GridKeys.FromKey(0x25), GridKeys.FromKeySym(0xFF51));
        Assert.Equal(GridKeys.FromKey(0x27), GridKeys.FromKeySym(0xFF53));

        // All twelve function keys, at both ends and in between.
        Assert.Equal(Zscii.F1, GridKeys.FromKeySym(0xFFBE));
        Assert.Equal(Zscii.F12, GridKeys.FromKeySym(0xFFC9));

        for (ulong symbol = 0xFFBE; symbol <= 0xFFC9; symbol++)
        {
            Assert.Equal(GridKeys.FromKey(0x70 + (int)(symbol - 0xFFBE)), GridKeys.FromKeySym(symbol));
        }

        // A symbol the Z-machine has no name for is dropped, not sent
        // as something else. Caps lock is the obvious one to press by
        // accident.
        Assert.Equal(0, GridKeys.FromKeySym(0xFFE5));
        Assert.Equal(0, GridKeys.FromKeySym(0));
    }
}

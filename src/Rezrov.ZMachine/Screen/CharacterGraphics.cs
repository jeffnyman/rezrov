namespace Rezrov.ZMachine.Screen;

/// <summary>
/// [zm 16] Font 3, the character graphics font: what each of its
/// characters looks like, as the nearest Unicode character a frontend
/// made of text can show.
/// </summary>
/// <remarks>
/// [zm 16.1] The standard gives font 3 as a table of 8 by 8 bitmaps
/// for character codes 32 to 126: lines and corners for drawing
/// boxes, arrows, blocks and part-blocks for maps and gauges, and a
/// runic alphabet for Beyond Zork's inscriptions. A frontend with a
/// bitmap font would draw those bitmaps; a frontend made of cells
/// picks the box-drawing, block, arrow, and runic characters that
/// come closest, which is what this table is. The blocks are kept to
/// the ones the old IBM character set had, whole and half blocks,
/// since every terminal font has those and few have the quarter and
/// eighth blocks, which came out blank in practice. The remarks on
/// section 16 give two Infocom drawings of the runes; these follow the
/// Amiga set the standard draws, mapped onto the late Anglian runes it
/// names.
///
/// [zm 8.1.2] Font 3 need only be available in Roman and reverse
/// video, and [zm 16.1] its characters are printed immediately next
/// to each other, which cells do by nature.
/// </remarks>
public static class CharacterGraphics
{
    // Codes 32 to 126, in order, from the bitmaps of [zm 16.1].
    private const string Glyphs =
        " "          // 32: blank
        + "←"   // 33: arrow left
        + "→"   // 34: arrow right
        + "╱"   // 35: diagonal rising
        + "╲"   // 36: diagonal falling
        + " "        // 37: blank
        + "─"   // 38: horizontal line, low
        + "─"   // 39: horizontal line, high
        + "│"   // 40: vertical line, right of center
        + "│"   // 41: vertical line, left of center
        + "┴"   // 42: up and horizontal
        + "┬"   // 43: down and horizontal
        + "├"   // 44: vertical and right
        + "┤"   // 45: vertical and left
        + "└"   // 46: up and right
        + "┌"   // 47: down and right
        + "┐"   // 48: down and left
        + "┘"   // 49: up and left
        + "╱"   // 50: a vertical line turning into a diagonal down and left
        + "┌"   // 51: down and right, with a stub to the upper left
        + "┐"   // 52: down and left, with a stub to the upper right
        + "┘"   // 53: up and left, with a stub to the lower right
        + "█"   // 54: full block
        + "▀"   // 55: upper block
        + "▄"   // 56: lower block
        + "▌"   // 57: left block
        + "▐"   // 58: right block
        + "▄"   // 59: lower block with a stem above
        + "▀"   // 60: upper block with a stem below
        + "▌"   // 61: left block with a bar across
        + "▐"   // 62: right block with a bar across
        + "▀"   // 63: upper right quadrant, as the upper half
        + "▄"   // 64: lower right quadrant, as the lower half
        + "▄"   // 65: lower left quadrant, as the lower half
        + "▀"   // 66: upper left quadrant, as the upper half
        + "▀"   // 67: upper right quadrant with a diagonal
        + "▄"   // 68: lower right quadrant with a diagonal
        + "▄"   // 69: lower left quadrant with a diagonal
        + "▀"   // 70: upper left quadrant with a diagonal
        + "'"        // 71: dot, top right
        + "."        // 72: dot, bottom right
        + "."        // 73: dot, bottom left
        + "'"        // 74: dot, top left
        + "▀"   // 75: top edge
        + "▄"   // 76: bottom edge
        + "▌"   // 77: left edge
        + "▐"   // 78: right edge
        + "═"   // 79: double horizontal line
        + "▌"   // 80: gauge, one eighth
        + "▌"   // 81: gauge, two eighths
        + "▌"   // 82: gauge, three eighths
        + "▌"   // 83: gauge, four eighths
        + "█"   // 84: gauge, five eighths
        + "█"   // 85: gauge, six eighths
        + "█"   // 86: gauge, seven eighths
        + "█"   // 87: gauge, full
        + "▐"   // 88: right edge, short
        + "▌"   // 89: left edge, short
        + "╳"   // 90: diagonal cross
        + "┼"   // 91: vertical and horizontal
        + "↑"   // 92: arrow up
        + "↓"   // 93: arrow down
        + "↕"   // 94: arrow up and down
        + "□"   // 95: box
        + "?"        // 96: question mark
        + "ᚪ"   // 97: rune a
        + "ᛒ"   // 98: rune b
        + "ᛇ"   // 99: rune eo, for c
        + "ᛞ"   // 100: rune d
        + "ᛖ"   // 101: rune e
        + "ᚠ"   // 102: rune f
        + "ᚷ"   // 103: rune g
        + "ᚻ"   // 104: rune h
        + "ᛁ"   // 105: rune i
        + "ᛄ"   // 106: rune j
        + "ᛤ"   // 107: rune other k, for k
        + "ᛚ"   // 108: rune l
        + "ᛗ"   // 109: rune m
        + "ᚾ"   // 110: rune n
        + "ᚩ"   // 111: rune o
        + "ᛈ"   // 112: rune p
        + "ᚳ"   // 113: rune k, for q
        + "ᚱ"   // 114: rune r
        + "ᛋ"   // 115: rune s
        + "ᛏ"   // 116: rune t
        + "ᚢ"   // 117: rune u
        + "ᛠ"   // 118: rune ea, for v
        + "ᚹ"   // 119: rune w
        + "ᛉ"   // 120: rune z, for x
        + "ᚣ"   // 121: rune y
        + "ᛟ"   // 122: rune oe, for z
        + "▓"   // 123: dark shade
        + "▓"   // 124: dark shade
        + "▒"   // 125: medium shade
        + "░";  // 126: light shade

    /// <summary>
    /// The Unicode character nearest to a font 3 character. Codes
    /// outside 32 to 126 have no glyph in the font and come back as
    /// they are.
    /// </summary>
    public static char ToUnicode(char character) =>
        character is >= ' ' and <= '~' ? Glyphs[character - ' '] : character;
}

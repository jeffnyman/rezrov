using Rezrov.ZMachine.Screen;

namespace Rezrov.Tui;

/// <summary>
/// The terminal as the screen model's frontend: a
/// <see cref="BufferedScreen"/> of the terminal's size, with the shape
/// and the abilities a terminal has.
/// </summary>
public sealed class TerminalScreen : BufferedScreen, ITerminalPicture
{
    public TerminalScreen(int width, int height, bool cursorStartsAtBottom, Action repaint, Func<ushort> waitForKey)
        : base(
            width,
            height,
            cursorStartsAtBottom,
            repaint,
            waitForKey,

            // [zm 8.8.1] A Version 6 screen is measured in units, and
            // the unit is the interpreter's to choose. A cell here is 4
            // units wide and 1 unit high, which is not a shape any pixel
            // screen has but is the shape Infocom's games assume when
            // they have no pictures: they measure text sideways in
            // pixels, through output stream 3, with constants that want
            // a screen at least 320 wide, and they count downward in
            // lines. An 80 by 24 terminal is then 320 by 24, and Zork
            // Zero, Journey, and Shogun lay themselves out as they were
            // meant to.
            fontWidth: 4,
            fontHeight: 1,

            // Everything a terminal can do: the status line and upper
            // window are drawn, styles and colors shown, [zm 16] the
            // character graphics font shown as the nearest Unicode
            // characters, and [zm 7.2] wrapping and [zm 8.4.1] paging
            // are the model's to do on this fixed grid.
            capabilities: ScreenCapabilities.StatusLine | ScreenCapabilities.UpperWindow
                | ScreenCapabilities.Colors | ScreenCapabilities.Bold | ScreenCapabilities.Italic
                | ScreenCapabilities.FixedPitch | ScreenCapabilities.FixedGrid
                | ScreenCapabilities.CharacterGraphicsFont)
    {
    }
}

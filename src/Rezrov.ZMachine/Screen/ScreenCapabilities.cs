namespace Rezrov.ZMachine.Screen;

/// <summary>
/// What a frontend's screen can do, which the interpreter reports to
/// the game through the header and uses to answer set_font.
/// </summary>
/// <remarks>
/// Each flag maps to something the standard asks the interpreter to
/// declare: [zm 8.2] a status line, [zm 8.6.1.2] an upper window,
/// [zm 8.3.2] colors, [zm 8.7.1.1] bold and italic, [zm 8.1.2] the
/// fixed pitch font, and [zm 8.1.5] the character graphics font of
/// section 16. <see cref="FixedGrid"/> is about the frontend rather
/// than the game: a grid of cells that the model should wrap text for
/// and page with [MORE], as opposed to a stream or a proportional font
/// window that wraps and pages for itself.
/// </remarks>
[Flags]
public enum ScreenCapabilities
{
    None = 0,

    /// <summary>[zm 8.2] Versions 1 to 3 can show a status line.</summary>
    StatusLine = 1 << 0,

    /// <summary>
    /// [zm 8.6.1.2] The upper window is shown, so screen splitting is
    /// available to the game.
    /// </summary>
    UpperWindow = 1 << 1,

    /// <summary>[zm 8.3.3] Colors can be shown.</summary>
    Colors = 1 << 2,

    /// <summary>[zm 8.7.1.1] Boldface can be shown.</summary>
    Bold = 1 << 3,

    /// <summary>[zm 8.7.1.1] Italic can be shown.</summary>
    Italic = 1 << 4,

    /// <summary>
    /// [zm 8.1.2] A fixed pitch font is available beside the normal
    /// one, which matters only when the normal font is proportional.
    /// </summary>
    FixedPitch = 1 << 5,

    /// <summary>[zm 8.1.5] Font 3, the character graphics font.</summary>
    CharacterGraphicsFont = 1 << 6,

    /// <summary>
    /// The normal font is proportional, which [zm 11.1.4] bit 6 of
    /// Flags 1 reports in Versions 1 to 3.
    /// </summary>
    ProportionalFont = 1 << 7,

    /// <summary>
    /// The lower window is a fixed grid of characters, so the model
    /// should [zm 7.2] wrap words at the width and [zm 8.4.1] pause for
    /// [MORE] at the height. A stream leaves both to whatever shows it.
    /// </summary>
    FixedGrid = 1 << 8,

    /// <summary>
    /// [zm 8.8.6] Pictures can be shown, so a Version 6 game is told
    /// they are available and asked to lay out around them. Without
    /// this the game is told there are none, and the Infocom games
    /// run in their text-only modes, which suits a screen of cells.
    /// </summary>
    Pictures = 1 << 9,
}

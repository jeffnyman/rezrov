namespace Rezrov.ZMachine;

/// <summary>
/// The bits of Flags 1, header byte $01, as they are defined in Versions 1
/// to 3.
/// </summary>
/// <remarks>
/// [zm 11.1.4] Flags 1 means different things before and after Version 4,
/// which is why there are two enums for the same byte. See
/// <see cref="Flags1FromVersion4"/> for the later layout.
///
/// [zm 11.1.4] Bits 0 and 7 are unused in these versions.
/// </remarks>
[Flags]
public enum Flags1Versions1To3 : byte
{
    None = 0,

    /// <summary>
    /// Bit 1. The status line shows hours and minutes rather than score
    /// and turns. Set by the game, never by the interpreter.
    /// </summary>
    TimeStatusLine = 1 << 1,

    /// <summary>
    /// Bit 2. The story file was split across two discs. This only ever
    /// described the Atari 800 releases and means nothing today.
    /// </summary>
    SplitAcrossTwoDiscs = 1 << 2,

    /// <summary>
    /// Bit 3. The "Tandy" bit, which a few early games consult to tone
    /// down their prose. Conventional only; an interpreter need not
    /// understand it.
    /// </summary>
    Tandy = 1 << 3,

    /// <summary>
    /// Bit 4. Set by the interpreter when it cannot provide a status
    /// line. Note the polarity: set means unavailable.
    /// </summary>
    StatusLineUnavailable = 1 << 4,

    /// <summary>
    /// Bit 5. Set by the interpreter when it can split the screen.
    /// </summary>
    ScreenSplittingAvailable = 1 << 5,

    /// <summary>
    /// Bit 6. Set by the interpreter when its default font is variable
    /// pitch.
    /// </summary>
    VariablePitchFontDefault = 1 << 6,
}

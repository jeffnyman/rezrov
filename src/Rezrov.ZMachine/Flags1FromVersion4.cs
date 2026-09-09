namespace Rezrov.ZMachine;

/// <summary>
/// The bits of Flags 1, header byte $01, as they are defined from Version
/// 4 onward.
/// </summary>
/// <remarks>
/// [zm 11.1.4] Flags 1 means different things before and after Version 4,
/// which is why there are two enums for the same byte. See
/// <see cref="Flags1Versions1To3"/> for the earlier layout.
///
/// [zm 1.2.4] Versions 7 and 8 follow Version 5 here, so this layout
/// applies to them too.
///
/// Every bit in this layout is set by the interpreter to advertise a
/// capability, and must be set correctly again after a restart or a
/// restore. [zm 11.1.4] Bit 6 is unused.
/// </remarks>
[Flags]
public enum Flags1FromVersion4 : byte
{
    None = 0,

    /// <summary>Bit 0. Colors are available. Version 5 and later.</summary>
    ColorsAvailable = 1 << 0,

    /// <summary>
    /// Bit 1. In Version 6, pictures can be displayed. In Versions 4, 5,
    /// 7, and 8 the Inform library reuses this bit for the status line
    /// type, carrying over its Version 3 meaning, and the standard tells
    /// interpreters to ignore it there.
    /// </summary>
    PicturesAvailable = 1 << 1,

    /// <summary>Bit 2. Boldface is available.</summary>
    BoldfaceAvailable = 1 << 2,

    /// <summary>Bit 3. Italic is available.</summary>
    ItalicAvailable = 1 << 3,

    /// <summary>Bit 4. A fixed-space style is available.</summary>
    FixedSpaceAvailable = 1 << 4,

    /// <summary>
    /// Bit 5. Sound effects are available. Version 6 only.
    /// </summary>
    SoundEffectsAvailable = 1 << 5,

    /// <summary>
    /// Bit 7. Timed keyboard input is available. [zm 11.1.4.1] This
    /// meaning was new in the 1.0 standard.
    /// </summary>
    TimedInputAvailable = 1 << 7,
}

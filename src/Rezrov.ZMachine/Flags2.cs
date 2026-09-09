namespace Rezrov.ZMachine;

/// <summary>
/// The bits of Flags 2, the header word at $10.
/// </summary>
/// <remarks>
/// [zm 11.1.2] Unlike Flags 1, this is a word, and the standard numbers
/// its bits as a word: bit 0 is bit 0 of the second byte, and bit 8 is
/// bit 0 of the first byte. Reading the word big-endian and shifting by
/// the bit number gives exactly the standard's numbering.
///
/// Most of these are set by the game to say it wants a feature. For bits
/// 3, 4, 5, 7, and 8, the interpreter clears the bit again if it cannot
/// provide what was asked for, so a game can check after startup.
///
/// [zm 11.1.2] Bits 9 and 11 through 15 are unused.
/// </remarks>
[Flags]
public enum Flags2 : ushort
{
    None = 0,

    /// <summary>
    /// Bit 0. Transcripting is on. [zm 11.1.2.1] The game may change this
    /// directly to turn transcripting on or off, and the interpreter must
    /// change it whenever output stream 2 changes, so that the bit always
    /// reflects the truth. It survives a restart or a restore.
    /// </summary>
    Transcripting = 1 << 0,

    /// <summary>
    /// Bit 1. The game wants everything printed in a fixed-pitch font.
    /// Version 3 and later.
    /// </summary>
    ForceFixedPitch = 1 << 1,

    /// <summary>
    /// Bit 2. The interpreter sets this to ask the game to redraw the
    /// screen, and the game clears it when it has complied. Version 6.
    /// </summary>
    RequestScreenRedraw = 1 << 2,

    /// <summary>
    /// Bit 3. The game wants to use pictures. Version 5 and later.
    /// </summary>
    WantsPictures = 1 << 3,

    /// <summary>
    /// Bit 4. The game wants to use the undo opcodes. Version 5 and later.
    /// The Amiga release of The Lurking Horror, a Version 3 game, also sets
    /// this bit, presumably for sound, but that is a curiosity rather
    /// than a rule.
    /// </summary>
    WantsUndo = 1 << 4,

    /// <summary>
    /// Bit 5. The game wants to use a mouse. Version 5 and later.
    /// </summary>
    WantsMouse = 1 << 5,

    /// <summary>
    /// Bit 6. The game wants to use colors. Version 5 and later.
    /// </summary>
    WantsColors = 1 << 6,

    /// <summary>
    /// Bit 7. The game wants to use sound effects. Version 5 and later.
    /// </summary>
    WantsSoundEffects = 1 << 7,

    /// <summary>Bit 8. The game wants to use menus. Version 6.</summary>
    WantsMenus = 1 << 8,

    /// <summary>
    /// Bit 10. Possibly set by an interpreter to report a printer error
    /// during transcription. The standard itself marks this one with a
    /// question mark, so treat it as conventional.
    /// </summary>
    PrinterError = 1 << 10,
}

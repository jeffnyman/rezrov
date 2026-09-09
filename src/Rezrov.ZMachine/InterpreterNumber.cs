namespace Rezrov.ZMachine;

/// <summary>
/// The interpreter numbers Infocom assigned to its own platforms, stored
/// in header byte $1E from Version 4 onward.
/// </summary>
/// <remarks>
/// [zm 11.1.3] An interpreter should pick the number most suitable for the
/// machine it runs on. Through Version 5 that barely matters, except that
/// Beyond Zork changes its character graphics based on it. In Version 6
/// it matters a great deal, because Infocom's Version 6 games check it
/// in many places and some were built for one machine only.
///
/// [zm 11.1.3.1] Modern games are strongly discouraged from testing this
/// value for anything that changes gameplay.
/// </remarks>
public enum InterpreterNumber : byte
{
    Unspecified = 0,

    /// <summary>Infocom's in-house mainframe.</summary>
    DecSystem20 = 1,
    AppleIIe = 2,
    Macintosh = 3,
    Amiga = 4,
    AtariST = 5,
    IbmPc = 6,
    Commodore128 = 7,
    Commodore64 = 8,
    AppleIIc = 9,
    AppleIIgs = 10,
    TandyColor = 11,
}

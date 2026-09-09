namespace Rezrov.ZMachine;

/// <summary>
/// The bits of Flags 3, which lives in word 4 of the header extension
/// table rather than in the header proper.
/// </summary>
/// <remarks>
/// [zm 11.1.7.3] Flags 3 was added by the 1.1 standard. [zm 11.1.7.4] The
/// game sets a bit to request a feature, and the interpreter must clear
/// it if it cannot provide that feature. [zm 11.1.7.4.1] The interpreter
/// must also clear every bit that is unused.
/// </remarks>
[Flags]
public enum Flags3 : ushort
{
    None = 0,

    /// <summary>
    /// Bit 0. The game wants to use transparency. Version 6.
    /// </summary>
    WantsTransparency = 1 << 0,
}

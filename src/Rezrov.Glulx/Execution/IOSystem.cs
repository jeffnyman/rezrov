namespace Rezrov.Glulx.Execution;

/// <summary>
/// [glulx op:setiosys] The I/O systems output can go through.
/// </summary>
/// <remarks>
/// [glulx #input-and-output] No input or output is built into the
/// machine. The output opcodes send their characters to whichever
/// system is current, and the machine starts with the null system.
/// The values 140 to 14F are reserved for extensions and are not here.
/// </remarks>
public enum IOSystem : uint
{
    /// <summary>0: all output is discarded.</summary>
    Null = 0,

    /// <summary>
    /// 1: the game's own function, named by the rock, is called with
    /// each character.
    /// </summary>
    Filter = 1,

    /// <summary>2: output goes to the current Glk stream.</summary>
    Glk = 2,

    /// <summary>
    /// 20: the FyreVM channel system, which is not supported.
    /// </summary>
    FyreVM = 20,
}

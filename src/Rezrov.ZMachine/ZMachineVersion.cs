namespace Rezrov.ZMachine;

/// <summary>
/// The Z-Machine version a story file was compiled for.
/// </summary>
/// <remarks>
/// [zm 11.1] The version is byte $00 of the header, and it is the first
/// thing an interpreter has to know, since almost every other rule in the
/// standard is qualified by version.
///
/// Versions 1 through 6 are Infocom's. Versions 7 and 8 were proposed later
/// by Graham Nelson to allow larger story files for the Inform compiler,
/// and no Infocom game uses them.
/// </remarks>
public enum ZMachineVersion : byte
{
    V1 = 1,
    V2 = 2,
    V3 = 3,
    V4 = 4,
    V5 = 5,
    V6 = 6,
    V7 = 7,
    V8 = 8,
}

namespace Rezrov.Core;

/// <summary>
/// The container or virtual machine format a story file is written in.
/// </summary>
public enum StoryFormat
{
    /// <summary>
    /// The leading bytes match no format Rezrov recognizes.
    /// </summary>
    Unknown,

    /// <summary>Bare Z-code, as in a .z3 or .z5 file.</summary>
    ZMachine,

    /// <summary>Bare Glulx, as in a .ulx file.</summary>
    Glulx,

    /// <summary>
    /// A Blorb container, which wraps a story file together with its
    /// pictures and sounds, as in a .zblorb or .gblorb file.
    /// </summary>
    Blorb,
}

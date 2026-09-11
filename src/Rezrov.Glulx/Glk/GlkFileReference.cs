namespace Rezrov.Glulx.Glk;

/// <summary>
/// [glk #fileref] A file reference: a file in permanent storage that a
/// stream can be opened on, with its usage and whether it is text.
/// </summary>
/// <remarks>
/// Only the object itself is here so far, so that a game can iterate
/// the class at startup, as the Glulx specification tells it to, and
/// find it empty. Creating references and opening file streams on them
/// come with the file functions.
/// </remarks>
public sealed class GlkFileReference : GlkObject
{
    internal GlkFileReference(uint rock, uint usage)
        : base(rock)
    {
        Usage = usage;
    }

    /// <summary>
    /// [glk #fileref] The fileusage value the reference was made with.
    /// </summary>
    public uint Usage { get; }
}

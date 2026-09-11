namespace Rezrov.Glulx.Glk;

/// <summary>
/// [glk #fileref] A file reference: the name of a file in permanent
/// storage that a stream can be opened on, with its usage and whether
/// it is text or binary.
/// </summary>
/// <remarks>
/// [glk #fileref] A reference need not be to a file that exists; it is
/// the name that is kept, and the file is made when a stream is opened
/// on it for writing. The name is what the frontend's
/// <see cref="IGlkFileSystem"/> knows the file by: for a reference made
/// by name, the specification's recommended form of the game's name
/// with the usage's suffix, and for one the player chose or a
/// temporary one, whatever the file system gave.
/// </remarks>
public sealed class GlkFileReference : GlkObject
{
    /// <summary>
    /// [glk #fileref] The bits of a usage that are its type.
    /// </summary>
    public const uint TypeMask = 0x0F;

    /// <summary>
    /// [glk #fileref] The usage bit for a text file; without it the
    /// file is binary.
    /// </summary>
    public const uint TextModeUsage = 0x100;

    // [glk op:fileref_create_by_name] The characters the specification
    // names as illegal in a file name, plus whatever the platform adds.
    private static readonly char[] Illegal = [.. "/\\<>:\"|?*", .. Path.GetInvalidFileNameChars()];

    internal GlkFileReference(uint rock, uint usage, string name)
        : base(rock)
    {
        Usage = usage;
        Name = name;
    }

    /// <summary>
    /// [glk #fileref] The fileusage value the reference was made with.
    /// </summary>
    public uint Usage { get; }

    /// <summary>[glk #fileref] The kind of file.</summary>
    public FileUsage Type => (FileUsage)(Usage & TypeMask);

    /// <summary>[glk #fileref] Whether the file is text.</summary>
    public bool IsText => (Usage & TextModeUsage) != 0;

    /// <summary>The name the file system knows the file by.</summary>
    public string Name { get; }

    /// <summary>
    /// [glk op:fileref_create_by_prompt] The recommended suffix for a
    /// usage: .glksave for a saved game, .txt for a transcript or a
    /// record of commands, and .glkdata for anything else.
    /// </summary>
    public static string Suffix(uint usage) => (FileUsage)(usage & TypeMask) switch
    {
        FileUsage.SavedGame => ".glksave",
        FileUsage.Transcript or FileUsage.InputRecord => ".txt",
        _ => ".glkdata",
    };

    /// <summary>
    /// [glk op:fileref_create_by_name] The specification's recommended
    /// file name for a name the game gave: illegal characters deleted,
    /// the name cut at its first period, "null" if nothing is left, and
    /// the usage's suffix appended.
    /// </summary>
    public static string SafeName(string name, uint usage)
    {
        ArgumentNullException.ThrowIfNull(name);

        var period = name.IndexOf('.', StringComparison.Ordinal);
        var stem = period < 0 ? name : name[..period];
        var kept = new string(stem.Where(c => Array.IndexOf(Illegal, c) < 0).ToArray());
        return (kept.Length == 0 ? "null" : kept) + Suffix(usage);
    }

    /// <summary>
    /// [glk op:fileref_create_by_prompt] A name the player chose, with
    /// the usage's suffix added if the name has no extension of its own.
    /// </summary>
    public static string WithSuffix(string chosen, uint usage)
    {
        ArgumentNullException.ThrowIfNull(chosen);

        var lastSeparator = chosen.LastIndexOfAny(['/', '\\']);
        var file = lastSeparator < 0 ? chosen : chosen[(lastSeparator + 1)..];
        return file.Contains('.', StringComparison.Ordinal) ? chosen : chosen + Suffix(usage);
    }
}

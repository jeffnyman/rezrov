namespace Rezrov.Glulx.Glk;

/// <summary>
/// What a frontend provides for Glk's files: a way to ask the player
/// for a file, names for temporary files, and the files themselves by
/// name.
/// </summary>
/// <remarks>
/// [glk #fileref] A file reference holds platform-specific information
/// about the name and location of a file, which is exactly what the
/// library should not know about, so the library keeps to names and
/// the frontend keeps the files. A name is either one the library
/// built from what the game asked for, which is a bare file name with
/// a suffix, or one the frontend gave back from a prompt or for a
/// temporary file, which the frontend may make as specific as it
/// likes. [glk #vmio] A frontend may keep its files anywhere, or in
/// memory, or nowhere at all.
/// </remarks>
public interface IGlkFileSystem
{
    /// <summary>
    /// [glk op:fileref_create_by_prompt] Asks the player for a file of
    /// a usage, to be opened in a mode, and returns the name chosen, or
    /// null if the player declined. The library adds the usage's suffix
    /// to a name with no extension.
    /// </summary>
    string? AskForFile(FileUsage usage, FileMode mode);

    /// <summary>
    /// [glk op:fileref_create_temp] A name for a new temporary file,
    /// somewhere out of the player's way.
    /// </summary>
    string TemporaryName();

    /// <summary>
    /// [glk op:fileref_does_file_exist] Whether the file exists.
    /// </summary>
    bool Exists(string name);

    /// <summary>[glk op:fileref_delete_file] Deletes the file.</summary>
    void Delete(string name);

    /// <summary>
    /// [glk op:stream_open_file] Opens the file in a mode: for reading,
    /// which needs the file to exist; for writing, which empties it or
    /// makes it; for both, which makes it if it is missing and begins
    /// at the start; or for appending, which makes it if it is missing
    /// and begins at the end. Null when the file cannot be opened so.
    /// </summary>
    Stream? Open(string name, FileMode mode);
}

/// <summary>
/// A file system kept in memory: files are byte arrays by name, and a
/// prompt is answered from a queue of names or declined.
/// </summary>
/// <remarks>
/// This is the library's file system when a frontend gives it none, so
/// a game can save and restore within a session without touching the
/// disk, and it is what tests use, since what a file holds can be read
/// straight out of <see cref="Files"/>.
/// </remarks>
public sealed class MemoryGlkFileSystem : IGlkFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private int _temporaries;

    /// <summary>
    /// The files, by name, as they were when last closed or flushed.
    /// </summary>
    public IReadOnlyDictionary<string, byte[]> Files => _files;

    /// <summary>
    /// The names to answer prompts with, in order; a null answer, or an
    /// empty queue, declines.
    /// </summary>
    public Queue<string?> Answers { get; } = new();

    /// <summary>Every prompt asked, with its usage and mode.</summary>
    public List<(FileUsage Usage, FileMode Mode)> Prompts { get; } = [];

    /// <summary>Puts a file in place, replacing any of the name.</summary>
    public void Put(string name, byte[] contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        _files[name] = contents;
    }

    public string? AskForFile(FileUsage usage, FileMode mode)
    {
        Prompts.Add((usage, mode));
        return Answers.Count > 0 ? Answers.Dequeue() : null;
    }

    public string TemporaryName() => $"temporary{++_temporaries}";

    public bool Exists(string name) => _files.ContainsKey(name);

    public void Delete(string name) => _files.Remove(name);

    public Stream? Open(string name, FileMode mode)
    {
        var exists = _files.TryGetValue(name, out var contents);
        if (mode == FileMode.Read && !exists)
        {
            return null;
        }

        if (mode is not (FileMode.Read or FileMode.Write or FileMode.ReadWrite or FileMode.WriteAppend))
        {
            return null;
        }

        var file = new FileImage(this, name);
        if (mode != FileMode.Write && exists)
        {
            file.Write(contents);
            file.Position = mode == FileMode.WriteAppend ? file.Length : 0;
        }

        // Opening for writing makes the file, as it does on a disk.
        file.Flush();
        return file;
    }

    // A memory stream that puts its contents back under its name
    // whenever it is flushed or closed.
    private sealed class FileImage : MemoryStream
    {
        private readonly MemoryGlkFileSystem _owner;
        private readonly string _name;

        public FileImage(MemoryGlkFileSystem owner, string name)
        {
            _owner = owner;
            _name = name;
        }

        public override void Flush()
        {
            base.Flush();
            _owner._files[_name] = ToArray();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _owner._files[_name] = ToArray();
            }

            base.Dispose(disposing);
        }
    }
}

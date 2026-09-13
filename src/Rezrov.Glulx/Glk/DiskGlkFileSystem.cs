namespace Rezrov.Glulx.Glk;

/// <summary>
/// The file system of a program with a disk and a console: files live
/// in one directory unless the player names a path, and a prompt is a
/// question written to one text stream and answered on another.
/// </summary>
/// <remarks>
/// [glk op:fileref_create_by_name] A named file is kept in a fixed
/// location relevant to the program, which is the directory given,
/// usually the game's own. [glk op:fileref_create_by_prompt] The
/// library may simply prompt the player to type a name, which is what
/// a console can do; a name typed without a directory lands in the
/// same place, and a name given up front in <see cref="NamedFiles"/>
/// answers a prompt for its usage without asking, which is what a
/// scripted run wants.
/// </remarks>
public sealed class DiskGlkFileSystem : IGlkFileSystem
{
    private readonly string _directory;
    private readonly TextReader _reader;
    private readonly TextWriter _prompt;
    private readonly Func<FileUsage, FileMode, string?>? _ask;

    /// <param name="directory">Where files without a path live.</param>
    /// <param name="reader">
    /// Where the player's answers come from, or none to decline every
    /// prompt.
    /// </param>
    /// <param name="prompt">
    /// Where the questions go, or none for questions asked silently.
    /// </param>
    public DiskGlkFileSystem(string directory, TextReader? reader = null, TextWriter? prompt = null)
    {
        ArgumentNullException.ThrowIfNull(directory);
        _directory = directory;
        _reader = reader ?? TextReader.Null;
        _prompt = prompt ?? TextWriter.Null;
    }

    /// <summary>
    /// A file system that asks the player through
    /// <paramref name="ask"/>, which a frontend with dialogs supplies,
    /// rather than through a prompt and a reader.
    /// </summary>
    public DiskGlkFileSystem(string directory, Func<FileUsage, FileMode, string?> ask)
        : this(directory)
    {
        ArgumentNullException.ThrowIfNull(ask);
        _ask = ask;
    }

    /// <summary>
    /// Names given up front for each usage, used for a prompt of that
    /// usage instead of asking.
    /// </summary>
    public Dictionary<FileUsage, string> NamedFiles { get; } = [];

    public string? AskForFile(FileUsage usage, FileMode mode)
    {
        if (NamedFiles.TryGetValue(usage, out var named))
        {
            return named;
        }

        if (_ask is not null)
        {
            return _ask(usage, mode);
        }

        // [glk op:fileref_create_by_prompt] The prompt is inferred from
        // the usage, and says which way the file is going.
        var kind = usage switch
        {
            FileUsage.SavedGame => "Saved game",
            FileUsage.Transcript => "Transcript file",
            FileUsage.InputRecord => "Command record file",
            _ => "Data file",
        };

        _prompt.Write($"{kind} {(mode == FileMode.Read ? "to load" : "to store")}: ");
        _prompt.Flush();

        var answer = _reader.ReadLine()?.Trim();
        return string.IsNullOrEmpty(answer) ? null : answer;
    }

    public string TemporaryName() => Path.GetTempFileName();

    public bool Exists(string name) => File.Exists(Resolve(name));

    public void Delete(string name)
    {
        try
        {
            File.Delete(Resolve(name));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public Stream? Open(string name, FileMode mode)
    {
        var path = Resolve(name);
        try
        {
            switch (mode)
            {
                case FileMode.Read:
                    return File.Exists(path) ? new FileStream(path, System.IO.FileMode.Open, FileAccess.Read) : null;
                case FileMode.Write:
                    return new FileStream(path, System.IO.FileMode.Create, FileAccess.Write);
                case FileMode.ReadWrite:
                    return new FileStream(path, System.IO.FileMode.OpenOrCreate, FileAccess.ReadWrite);
                case FileMode.WriteAppend:
                {
                    var file = new FileStream(path, System.IO.FileMode.OpenOrCreate, FileAccess.Write);
                    file.Seek(0, SeekOrigin.End);
                    return file;
                }

                default:
                    return null;
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string Resolve(string name) => Path.IsPathRooted(name) ? name : Path.Combine(_directory, name);
}

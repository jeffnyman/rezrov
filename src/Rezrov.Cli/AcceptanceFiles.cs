using Rezrov.ZMachine.Streams;

namespace Rezrov.Cli;

/// <summary>
/// Files for an acceptance run, none of which touch the disk.
/// </summary>
/// <remarks>
/// [zm 7.6] A script has to be able to save and restore, since the
/// remarks on section 7 single that out as the thing a long test wants,
/// so a saved game is kept in memory and handed back on the next
/// restore. A transcript or command record the game turns on goes
/// nowhere, since the run's own output is the transcript. Auxiliary
/// files are kept by name for the length of the run. Nothing is ever
/// asked, because there is nobody to ask.
/// </remarks>
internal sealed class AcceptanceFiles : IFileChooser, IDisposable
{
    private readonly Dictionary<string, MemoryStream> _auxiliary = new(StringComparer.Ordinal);
    private MemoryStream? _saved;

    public TextWriter? OpenTranscript() => TextWriter.Null;

    public TextWriter? OpenCommandRecord() => TextWriter.Null;

    public TextReader? OpenCommandFile() => null;

    public Stream? OpenSaveFile()
    {
        _saved = new MemoryStream();
        return _saved;
    }

    public Stream? OpenRestoreFile()
    {
        // The interpreter closes the stream it saved to, and a closed
        // MemoryStream still answers ToArray.
        var bytes = _saved?.ToArray();
        return bytes is { Length: > 0 } ? new MemoryStream(bytes, writable: false) : null;
    }

    public Stream? OpenAuxiliaryFile(string name, bool forWriting, bool prompt)
    {
        if (forWriting)
        {
            var stream = new MemoryStream();
            _auxiliary[name] = stream;
            return stream;
        }

        return _auxiliary.TryGetValue(name, out var written)
            ? new MemoryStream(written.ToArray(), writable: false)
            : null;
    }

    public void Dispose()
    {
        _saved?.Dispose();
        foreach (var stream in _auxiliary.Values)
        {
            stream.Dispose();
        }
    }
}

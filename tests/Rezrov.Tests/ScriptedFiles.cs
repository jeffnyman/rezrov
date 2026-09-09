using Rezrov.ZMachine.Streams;

namespace Rezrov.Tests;

/// <summary>
/// A file chooser for tests: hands over whatever writers and readers
/// the test set up, and counts how often it was asked.
/// </summary>
internal sealed class ScriptedFiles : IFileChooser
{
    /// <summary>Where the transcript goes, or null to decline.</summary>
    public StringWriter? Transcript { get; set; }

    /// <summary>Where commands are recorded, or null to decline.</summary>
    public StringWriter? Record { get; set; }

    /// <summary>The file of commands handed over, once.</summary>
    public TextReader? CommandFile { get; set; }

    /// <summary>
    /// Where a saved game is written, or null to decline.
    /// </summary>
    public MemoryStream? SaveFile { get; set; }

    /// <summary>
    /// The saved game to hand back on restore, or null to fall back to
    /// whatever was last saved, or to decline if nothing was.
    /// </summary>
    public byte[]? RestoreFile { get; set; }

    /// <summary>Auxiliary files written, by name.</summary>
    public Dictionary<string, MemoryStream> AuxiliaryWritten { get; } = new(StringComparer.Ordinal);

    /// <summary>Auxiliary files available for reading, by name.</summary>
    public Dictionary<string, byte[]> AuxiliaryReadable { get; } = new(StringComparer.Ordinal);

    /// <summary>The prompt flags auxiliary requests came with.</summary>
    public List<bool> AuxiliaryPrompts { get; } = [];

    public int TranscriptRequests { get; private set; }

    public int RecordRequests { get; private set; }

    public int SaveRequests { get; private set; }

    public int RestoreRequests { get; private set; }

    public Stream? OpenSaveFile()
    {
        SaveRequests++;
        return SaveFile;
    }

    public Stream? OpenRestoreFile()
    {
        RestoreRequests++;
        // The interpreter closes the stream it saved to, and a closed
        // MemoryStream still answers ToArray.
        var bytes = RestoreFile ?? (SaveFile?.ToArray() is { Length: > 0 } saved ? saved : null);
        return bytes is null ? null : new MemoryStream(bytes, writable: false);
    }

    public Stream? OpenAuxiliaryFile(string name, bool forWriting, bool prompt)
    {
        AuxiliaryPrompts.Add(prompt);
        if (forWriting)
        {
            var stream = new MemoryStream();
            AuxiliaryWritten[name] = stream;
            return stream;
        }

        return AuxiliaryReadable.TryGetValue(name, out var bytes) ? new MemoryStream(bytes, writable: false) : null;
    }

    public TextWriter? OpenTranscript()
    {
        TranscriptRequests++;
        return Transcript;
    }

    public TextWriter? OpenCommandRecord()
    {
        RecordRequests++;
        return Record;
    }

    public TextReader? OpenCommandFile()
    {
        var file = CommandFile;
        CommandFile = null;
        return file;
    }
}

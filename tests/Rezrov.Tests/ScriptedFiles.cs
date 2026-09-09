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

    public int TranscriptRequests { get; private set; }

    public int RecordRequests { get; private set; }

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

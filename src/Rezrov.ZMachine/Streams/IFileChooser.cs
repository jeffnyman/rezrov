namespace Rezrov.ZMachine.Streams;

/// <summary>
/// How a frontend chooses the files a game asks for: the transcript,
/// the record of commands, and the file of commands to play back.
/// </summary>
/// <remarks>
/// [zm 7.6] File handling is the interpreter's, and the standard leaves
/// the choosing of names to it: [zm 10.2.3] "any method" for a file of
/// commands, and for a transcript [zm 7.1.1.2] a question asked at most
/// once per session. A console asks the player, a GUI opens a dialog,
/// a test hands over a string, and a frontend without files at all
/// returns null, which [zm 7.6.5] the standard allows. Saved games will
/// join this interface when they arrive.
/// </remarks>
public interface IFileChooser
{
    /// <summary>
    /// [zm 7.1.1] Where output stream 2, the transcript, is written, or
    /// null if there is nowhere. Asked only the first time the stream
    /// is turned on.
    /// </summary>
    TextWriter? OpenTranscript();

    /// <summary>
    /// [zm 7.1.2.3] Where output stream 4, the player's commands, is
    /// written, or null if there is nowhere.
    /// </summary>
    TextWriter? OpenCommandRecord();

    /// <summary>
    /// [zm 10.2.3] The file of commands to play as input stream 1, or
    /// null if there is none, in which case the keyboard stays current.
    /// </summary>
    TextReader? OpenCommandFile();
}

/// <summary>
/// A frontend with no files: every request is declined.
/// </summary>
/// <remarks>
/// [zm 7.6.5] Interpreters are allowed not to support external files,
/// and [zm 7.6.5.2] a game's attempt to use one should then produce a
/// warning and otherwise do nothing, which the interpreter sees to.
/// </remarks>
public sealed class NoFileChooser : IFileChooser
{
    public static NoFileChooser Instance { get; } = new();

    public TextWriter? OpenTranscript() => null;

    public TextWriter? OpenCommandRecord() => null;

    public TextReader? OpenCommandFile() => null;
}

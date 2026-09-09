using Rezrov.ZMachine.Streams;

namespace Rezrov.Cli;

/// <summary>
/// Chooses files by asking on the console, unless the command line
/// already named them.
/// </summary>
/// <remarks>
/// [zm 7.6] The simplest of the methods the standard allows: a prompt
/// on standard error, so that it never lands in a transcript of
/// standard output, and a path typed back. A name given up front with
/// <c>--transcript</c>, <c>--record</c>, or <c>--commands</c> is used
/// without asking, which is what a scripted run wants.
/// </remarks>
internal sealed class ConsoleFiles : IFileChooser
{
    private readonly string? _transcript;
    private readonly string? _record;

    public ConsoleFiles(string? transcript, string? record)
    {
        _transcript = transcript;
        _record = record;
    }

    public TextWriter? OpenTranscript() =>
        OpenForWriting(_transcript ?? Ask("Transcript file: "));

    public TextWriter? OpenCommandRecord() =>
        OpenForWriting(_record ?? Ask("Record commands to: "));

    public TextReader? OpenCommandFile()
    {
        var path = Ask("Command file: ");
        if (path is null)
        {
            return null;
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"rezrov: no such file: {path}");
            return null;
        }

        return new StreamReader(path);
    }

    private static string? Ask(string prompt)
    {
        Console.Error.Write(prompt);
        var path = Console.In.ReadLine()?.Trim();
        return string.IsNullOrEmpty(path) ? null : path;
    }

    private static StreamWriter? OpenForWriting(string? path)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            // [zm 7.6.4] File errors are reported to the player directly.
            return new StreamWriter(path, append: true);
        }
        catch (IOException e)
        {
            Console.Error.WriteLine($"rezrov: cannot write {path}: {e.Message}");
            return null;
        }
        catch (UnauthorizedAccessException e)
        {
            Console.Error.WriteLine($"rezrov: cannot write {path}: {e.Message}");
            return null;
        }
    }
}

using Rezrov.ZMachine.Saves;
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
/// <c>--transcript</c>, <c>--record</c>, <c>--save</c>, or
/// <c>--commands</c> is used without asking, which is what a scripted
/// run wants; the remarks on section 7 note how useful it is to save
/// partway through a long test script. Otherwise a saved game is asked
/// for every time, since a player saves under many names, and an
/// auxiliary file is asked about only when the game wants confirmation.
/// </remarks>
internal sealed class ConsoleFiles : IFileChooser
{
    private readonly string? _transcript;
    private readonly string? _record;
    private readonly string? _save;

    public ConsoleFiles(string? transcript, string? record, string? save)
    {
        _transcript = transcript;
        _record = record;
        _save = save;
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

    public Stream? OpenSaveFile() => OpenStream(_save ?? Ask("Save to: "), forWriting: true);

    public Stream? OpenRestoreFile() => OpenStream(_save ?? Ask("Restore from: "), forWriting: false);

    public Stream? OpenAuxiliaryFile(string name, bool forWriting, bool prompt)
    {
        // [zm 7.6.1.1] The older text's ".AUX" extension, which is as
        // good as any and tells a glance what the file is.
        var path = name + ".AUX";
        if (prompt)
        {
            Console.Error.Write($"File name [{path}]: ");
            var typed = Console.In.ReadLine()?.Trim();
            if (typed is null)
            {
                return null;
            }

            if (typed.Length > 0)
            {
                path = AuxiliaryFileName.Sanitize(typed) + ".AUX";
            }
        }

        return OpenStream(path, forWriting);
    }

    private static FileStream? OpenStream(string? path, bool forWriting)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            // [zm 7.6.4] File errors are reported to the player directly,
            // except that a file not found on reading is only a failure.
            return forWriting
                ? new FileStream(path, FileMode.Create, FileAccess.Write)
                : new FileStream(path, FileMode.Open, FileAccess.Read);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (IOException e)
        {
            Console.Error.WriteLine($"rezrov: cannot open {path}: {e.Message}");
            return null;
        }
        catch (UnauthorizedAccessException e)
        {
            Console.Error.WriteLine($"rezrov: cannot open {path}: {e.Message}");
            return null;
        }
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

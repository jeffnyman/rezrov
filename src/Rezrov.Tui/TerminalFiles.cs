using Rezrov.ZMachine.Streams;
using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace Rezrov.Tui;

/// <summary>
/// Chooses files with Terminal.Gui's dialogs, unless the command line
/// already named them.
/// </summary>
/// <remarks>
/// [zm 7.6] The interpreter chooses file names by whatever means suit
/// it. Here that is a dialog on the UI thread, which the interpreter's
/// thread waits on, or a name given up front for a scripted run, as the
/// command line frontend also allows.
/// </remarks>
public sealed class TerminalFiles : IFileChooser
{
    private readonly IApplication _app;
    private readonly string? _transcript;
    private readonly string? _record;
    private readonly string? _save;
    private readonly string? _commands;

    public TerminalFiles(IApplication app, Presets presets)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(presets);

        _app = app;
        _transcript = presets.Transcript;
        _record = presets.Record;
        _save = presets.Save;
        _commands = presets.Commands;
    }

    /// <summary>
    /// File names given on the command line, used without asking.
    /// </summary>
    public sealed record Presets(string? Transcript, string? Record, string? Save, string? Commands);

    public TextWriter? OpenTranscript() =>
        OpenWriter(_transcript ?? AskForSave("Transcript file"));

    public TextWriter? OpenCommandRecord() =>
        OpenWriter(_record ?? AskForSave("Record commands to"));

    public TextReader? OpenCommandFile()
    {
        var path = _commands ?? AskForOpen("Command file");
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        return new StreamReader(path);
    }

    public Stream? OpenSaveFile() => OpenStream(_save ?? AskForSave("Save game as"), forWriting: true);

    public Stream? OpenRestoreFile() => OpenStream(_save ?? AskForOpen("Restore game from"), forWriting: false);

    public Stream? OpenAuxiliaryFile(string name, bool forWriting, bool prompt)
    {
        var path = name + ".AUX";
        if (prompt)
        {
            path = forWriting ? AskForSave("File name", path) : AskForOpen("File name", path);
            if (path is null)
            {
                return null;
            }
        }

        return OpenStream(path, forWriting);
    }

    // Runs a dialog on the UI thread and waits here for its answer.
    private string? AskForSave(string title, string? suggested = null) =>
        OnUiThread(() =>
        {
            using var dialog = new SaveDialog { Title = title };
            if (suggested is not null)
            {
                dialog.Path = suggested;
            }

            _app.Run(dialog);
            return dialog.Canceled ? null : dialog.Path;
        });

    private string? AskForOpen(string title, string? suggested = null) =>
        OnUiThread(() =>
        {
            using var dialog = new OpenDialog { Title = title };
            if (suggested is not null)
            {
                dialog.Path = suggested;
            }

            _app.Run(dialog);
            return dialog.Canceled ? null : dialog.Path;
        });

    private string? OnUiThread(Func<string?> ask)
    {
        string? answer = null;
        using var done = new ManualResetEventSlim();
        _app.Invoke(() =>
        {
            try
            {
                answer = ask();
            }
            finally
            {
                done.Set();
            }
        });

        done.Wait();
        return string.IsNullOrWhiteSpace(answer) ? null : answer;
    }

    private static StreamWriter? OpenWriter(string? path)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            return new StreamWriter(path, append: true);
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

    private static FileStream? OpenStream(string? path, bool forWriting)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            return forWriting
                ? new FileStream(path, FileMode.Create, FileAccess.Write)
                : new FileStream(path, FileMode.Open, FileAccess.Read);
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
}

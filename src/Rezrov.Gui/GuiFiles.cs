using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Rezrov.Glulx.Glk;
using Rezrov.ZMachine.Streams;
using GlkFileMode = Rezrov.Glulx.Glk.FileMode;

namespace Rezrov.Gui;

/// <summary>
/// The file dialogs, as both machines ask for them: saved games,
/// transcripts, command records, and whatever else a game wants to keep.
/// </summary>
/// <remarks>
/// [zm 10.6] and [glk op:fileref_create_by_prompt] Both machines ask the
/// player for a file by name, and both ask from inside the game's own
/// execution, which is the interpreter's thread. The toolkit will only
/// show a dialog on its own thread, so the ask is handed over and the
/// interpreter waits for the answer. That is safe because the toolkit's
/// thread never waits on the interpreter's: it paints what it is given
/// and nothing more.
/// </remarks>
internal sealed class GuiFiles(Window window, string directory) : IFileChooser
{
    /// <summary>
    /// [quetzal 1] A saved game is a Quetzal file whatever machine wrote
    /// it, and .sav is what it is usually called. The other two names
    /// are used as well, so a player who has one is not left hunting for
    /// it behind a filter that hides it.
    /// </summary>
    private static readonly FilePickerFileType SavedGame =
        new("Saved game") { Patterns = ["*.sav", "*.qzl", "*.glksave"] };

    private static readonly FilePickerFileType Transcript =
        new("Transcript") { Patterns = ["*.txt", "*.log"] };

    /// <summary>
    /// [zm 7.1.2.1] The commands of a session, played back into another,
    /// which Inform's own tools call a .rec file.
    /// </summary>
    private static readonly FilePickerFileType Commands =
        new("Recorded commands") { Patterns = ["*.rec", "*.txt"] };

    /// <summary>
    /// [glk op:fileref_create_by_prompt] Whatever a game keeps for
    /// itself. The Glulx checkers write .glkdata files, which is the
    /// name the Glk libraries have settled on.
    /// </summary>
    private static readonly FilePickerFileType Data =
        new("Game data") { Patterns = ["*.glkdata", "*.dat"] };

    /// <summary>
    /// Offered after the others, since a player may well have a file
    /// that was named something else entirely, and a filter that hides
    /// it is worse than no filter at all.
    /// </summary>
    private static readonly FilePickerFileType Anything =
        new("All files") { Patterns = ["*"] };

    private readonly Window _window = window;
    private readonly string _directory = directory;

    public TextWriter? OpenTranscript() => Writer("Transcript file", Transcript);

    public TextWriter? OpenCommandRecord() => Writer("Record commands to", Commands);

    public TextReader? OpenCommandFile()
    {
        var path = Ask("Play back commands from", Commands, writing: false);
        return path is null ? null : Reading(() => new StreamReader(path));
    }

    public Stream? OpenSaveFile()
    {
        var path = Ask("Save game as", SavedGame, writing: true);
        return path is null ? null : Reading(() => File.Create(path));
    }

    public Stream? OpenRestoreFile()
    {
        var path = Ask("Restore game from", SavedGame, writing: false);
        return path is null ? null : Reading(() => File.OpenRead(path));
    }

    /// <summary>
    /// [zm op:save] The auxiliary files a game keeps for itself, which
    /// it names rather than asking about unless it says otherwise.
    /// </summary>
    public Stream? OpenAuxiliaryFile(string name, bool forWriting, bool prompt)
    {
        var path = prompt
            ? Ask("Data file", Data, forWriting)
            : Path.Combine(_directory, Path.GetFileName(name));

        if (path is null)
        {
            return null;
        }

        return Reading(() => forWriting ? File.Create(path) : File.OpenRead(path));
    }

    /// <summary>
    /// [glk op:fileref_create_by_prompt] The same dialogs, for Glk,
    /// which asks for a name rather than for something already open.
    /// </summary>
    public string? AskForGlkFile(FileUsage usage, GlkFileMode mode) => Ask(
        usage switch
        {
            FileUsage.SavedGame => "Saved game",
            FileUsage.Transcript => "Transcript file",
            FileUsage.InputRecord => "Command record file",
            _ => "Data file",
        },
        usage switch
        {
            FileUsage.SavedGame => SavedGame,
            FileUsage.Transcript => Transcript,
            FileUsage.InputRecord => Commands,
            _ => Data,
        },
        mode != GlkFileMode.Read);

    private StreamWriter? Writer(string title, FilePickerFileType kind)
    {
        var path = Ask(title, kind, writing: true);
        return path is null ? null : Reading(() => new StreamWriter(path, append: true));
    }

    /// <summary>
    /// A file the player could not be given, for whatever reason the
    /// system had, is no file rather than a stop: [zm 10.6] a failed
    /// save is one the game is told about and carries on from.
    /// </summary>
    private static T? Reading<T>(Func<T> open)
        where T : class
    {
        try
        {
            return open();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private string? Ask(string title, FilePickerFileType kind, bool writing) => Wait(async () =>
    {
        var start = await _window.StorageProvider.TryGetFolderFromPathAsync(_directory);

        // The first pattern of the kind, without its star, is what a new
        // file is called when the player types a bare name.
        var extension = kind.Patterns is { Count: > 0 } patterns ? patterns[0].TrimStart('*', '.') : "dat";

        if (writing)
        {
            var chosen = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                DefaultExtension = extension,
                FileTypeChoices = [kind, Anything],
                SuggestedStartLocation = start,
            });

            return chosen?.TryGetLocalPath();
        }

        var opened = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [kind, Anything],
            SuggestedStartLocation = start,
        });

        return opened.Count > 0 ? opened[0].TryGetLocalPath() : null;
    });

    /// <summary>
    /// Runs something on the toolkit's thread and waits for it, which is
    /// what turns a dialog into the plain answer the interpreter is
    /// written to expect.
    /// </summary>
    private static T? Wait<T>(Func<Task<T?>> ask)
        where T : class
    {
        var answered = new TaskCompletionSource<T?>();

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                answered.SetResult(await ask());
            }
            catch (Exception e)
            {
                answered.SetException(e);
            }
        });

        return answered.Task.GetAwaiter().GetResult();
    }
}

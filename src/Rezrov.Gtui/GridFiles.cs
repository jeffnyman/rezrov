using System.Text;
using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Screen;
using Rezrov.ZMachine.Streams;
using Rezrov.ZMachine.Text;

namespace Rezrov.Gtui;

/// <summary>
/// [zm 7.6] The files a game asks for, with the name typed into the
/// game's own window.
/// </summary>
/// <remarks>
/// The other two window programs open the system's file dialog, and
/// this one cannot: a dialog is a toolkit, and the whole point of this
/// program is that there is no toolkit under it. Windows has a common
/// dialog that could be called, but X11 has nothing of the kind and
/// Cocoa would mean talking to the Objective-C runtime, so a dialog
/// here would be three separate pieces of work with one of them barely
/// possible.
///
/// So the question is asked where the game already is. A prompt is
/// printed into the screen and the answer is read a key at a time from
/// the same queue the game reads, which needs nothing from any
/// operating system and is what Infocom's own interpreters did. It also
/// means the asking can be tested, since a test can put keys in the
/// queue and read the screen afterwards.
///
/// Every name is taken relative to the folder the story file is in, so
/// a saved game lands beside the game it came from.
/// </remarks>
public sealed class GridFiles : IFileChooser
{
    private readonly BufferedScreen _screen;
    private readonly BufferedInput _input;
    private readonly string _directory;
    private readonly string _suggestion;

    public GridFiles(BufferedScreen screen, BufferedInput input, string directory, string suggestion)
    {
        _screen = screen;
        _input = input;
        _directory = directory;
        _suggestion = suggestion;
    }

    public TextWriter? OpenTranscript()
    {
        var path = Ask("Write the transcript to", Named(".txt"));
        return path is null ? null : Writer(path);
    }

    public TextWriter? OpenCommandRecord()
    {
        var path = Ask("Record the commands to", Named(".cmd"));
        return path is null ? null : Writer(path);
    }

    public TextReader? OpenCommandFile()
    {
        var path = Ask("Take commands from", Named(".cmd"));

        return path is not null && File.Exists(path) ? new StreamReader(path) : null;
    }

    public Stream? OpenSaveFile()
    {
        var path = Ask("Save the game as", Named(".sav"));
        return path is null ? null : Writing(path);
    }

    public Stream? OpenRestoreFile()
    {
        var path = Ask("Restore the game from", Named(".sav"));
        return path is not null && File.Exists(path) ? Reading(path) : null;
    }

    public Stream? OpenAuxiliaryFile(string name, bool forWriting, bool prompt)
    {
        // [zm 7.6] The name has already been made safe; the player is
        // asked to confirm it only when the game says to.
        var path = Path.Combine(_directory, name + ".aux");

        if (prompt)
        {
            if (Ask("File name", name + ".aux") is not { } answer)
            {
                return null;
            }

            path = answer;
        }

        return forWriting ? Writing(path) : File.Exists(path) ? Reading(path) : null;
    }

    /// <summary>
    /// Prints the question into the game's screen and reads the answer
    /// a key at a time, or returns null if the player pressed escape or
    /// gave no name at all.
    /// </summary>
    private string? Ask(string question, string suggestion)
    {
        var plain = new TextAttributes(
            TextStyle.Roman,
            _screen.DefaultForeground,
            _screen.DefaultBackground,
            TextAttributes.NormalFont);

        _screen.NewLine();
        _screen.Print($"{question}: ", plain);
        _screen.Print(suggestion, plain);

        var typed = new StringBuilder(suggestion);

        while (true)
        {
            var key = _input.WaitForAnyKey();

            if (key == Zscii.Newline)
            {
                _screen.EchoNewLine();
                break;
            }

            // [zm 3.8.2.2] Escape means the player has changed their
            // mind, and a save or a restore that is declined fails,
            // which the game is told about in the ordinary way.
            if (key == Zscii.Escape)
            {
                _screen.EchoNewLine();
                return null;
            }

            if (key == Zscii.Delete)
            {
                if (typed.Length > 0)
                {
                    typed.Length--;
                    _screen.EchoBackspace();
                }

                continue;
            }

            if (key is >= ' ' and <= '~')
            {
                typed.Append((char)key);
                _screen.Echo((char)key);
            }
        }

        var name = typed.ToString().Trim();
        return name.Length == 0 ? null : Resolve(name);
    }

    /// <summary>
    /// A name the player typed, taken relative to the story's own
    /// folder unless they gave a path of their own.
    /// </summary>
    private string Resolve(string name) =>
        Path.IsPathRooted(name) ? name : Path.Combine(_directory, name);

    /// <summary>
    /// What to suggest: the story's name with another extension, so
    /// that pressing return alone is the obvious thing to do.
    /// </summary>
    private string Named(string extension) => _suggestion + extension;

    private static StreamWriter? Writer(string path)
    {
        try
        {
            return new StreamWriter(path, append: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static FileStream? Writing(string path)
    {
        try
        {
            return File.Create(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static FileStream? Reading(string path)
    {
        try
        {
            return File.OpenRead(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

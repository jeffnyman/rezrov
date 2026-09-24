using System.Globalization;
using System.Security.Cryptography;

using Rezrov.Mapping;

namespace Rezrov.Watching;

/// <summary>
/// Where a game's map is kept between one session and the next.
/// </summary>
/// <remarks>
/// A map is what the player has worked out about a game, and it is
/// worth more than the session it was made in. Closing the window and
/// opening it again, restarting, restoring a save from last week: none
/// of those are reasons to throw away what they learned, so none of
/// them do. Starting a fresh map is something they ask for.
///
/// One map for each story rather than one for each save. A map is
/// knowledge about a world, not a fact about a particular moment in
/// it, and tying it to a save would mean a player who quit without
/// saving lost the lot.
///
/// The story is known by a hash of its own bytes, so two releases of
/// the same game keep their own maps, a renamed file keeps the map it
/// had, and nothing depends on a title that two games might share.
/// </remarks>
public sealed class MapStore
{
    /// <param name="story">The story file's bytes.</param>
    /// <param name="folder">
    /// Where maps are kept, or null for the ordinary place, which is
    /// this machine's own application data.
    /// </param>
    public MapStore(byte[] story, string? folder = null)
    {
        ArgumentNullException.ThrowIfNull(story);

        Kept = Path.Combine(folder ?? Ordinary(), Named(story) + ".map");
    }

    /// <summary>The file this story's map is kept in.</summary>
    public string Kept { get; }

    /// <summary>
    /// The map as it was last written, so that a turn which found
    /// nothing new does not write the same file again.
    /// </summary>
    private string? _written;

    /// <summary>
    /// The map kept for this story, or null where there is none yet or
    /// it cannot be read.
    /// </summary>
    /// <remarks>
    /// A map that cannot be read is not worth stopping a game over.
    /// The player gets an empty one and fills it in by playing, which
    /// is exactly what they would have had.
    /// </remarks>
    public RoomGraph? Load()
    {
        try
        {
            using var reader = new StreamReader(Kept);

            return MapFile.Read(reader);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Keeps the map, or says why it could not be kept.
    /// </summary>
    /// <returns>Null where it was written, or what went wrong.</returns>
    public string? Save(RoomGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        // Nothing found means nothing worth keeping, and writing an
        // empty file over a real map would lose it.
        if (graph.Rooms.Count == 0)
        {
            return null;
        }

        var text = MapFile.Written(graph);

        // Called once a turn, and most turns find no new room: the
        // player picks something up, reads it, and puts it down again.
        // Comparing what would be written against what was written
        // means the disk is touched when the map actually grows rather
        // than once for every command typed. The file is checked too,
        // so that a map deleted from underneath comes back.
        if (text == _written && File.Exists(Kept))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Kept)!);
            File.WriteAllText(Kept, text);

            _written = text;

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }

    /// <summary>Throws the kept map away, when the player asks.</summary>
    public void Forget()
    {
        _written = null;

        try
        {
            File.Delete(Kept);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing to tell the player: the map in front of them has
            // gone either way, and the file will be written over the
            // next time this game is closed.
        }
    }

    /// <summary>
    /// This machine's own place for the things a program keeps for
    /// itself, which is where maps go.
    /// </summary>
    private static string Ordinary() => Path.Combine(Kind(), "rezrov", "maps");

    /// <summary>
    /// The folder this machine keeps a program's own files in.
    /// </summary>
    /// <remarks>
    /// Windows and Linux are both answered correctly by asking the
    /// runtime for local application data: it gives %LOCALAPPDATA% on
    /// one, and XDG_DATA_HOME or ~/.local/share on the other, which is
    /// what each platform expects.
    ///
    /// macOS is the one that has to be said out loud. The runtime
    /// answers ~/.local/share there as well, because it shares the
    /// Unix implementation, but that is not where a Mac keeps these
    /// things and a file left there is somewhere no Mac user would
    /// think to look. The Apple convention is Application Support
    /// under the user's own Library, so that is what is used.
    /// </remarks>
    private static string Kind() =>
        Folder(
            OperatingSystem.IsMacOS(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create));

    /// <summary>
    /// Which of the two a machine of the given kind wants.
    /// </summary>
    /// <remarks>
    /// Taken apart from the machine it runs on so that the choice can
    /// be checked anywhere. The Apple answer is the one that needs
    /// checking and is the one that cannot be run here.
    /// </remarks>
    /// <param name="apple">Whether this is a Mac.</param>
    /// <param name="home">The user's own folder.</param>
    /// <param name="local">What the runtime offers for this.</param>
    public static string Folder(bool apple, string home, string local) =>
        apple && !string.IsNullOrEmpty(home)
            ? Path.Combine(home, "Library", "Application Support")
            : local;

    /// <summary>
    /// What this story is called among the kept maps: a hash of its
    /// bytes, which tells two releases apart and does not care what
    /// the file on disk is named.
    /// </summary>
    private static string Named(byte[] story)
    {
        var hash = SHA256.HashData(story);

        return string.Concat(hash.Take(8).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }
}

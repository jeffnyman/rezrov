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

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Kept)!);

            using var writer = new StreamWriter(Kept);

            MapFile.Write(graph, writer);

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
    private static string Ordinary() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create),
            "rezrov",
            "maps");

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

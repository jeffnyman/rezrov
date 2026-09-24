using System.Globalization;
using System.Text;

namespace Rezrov.Mapping;

/// <summary>
/// A map written out as text, and read back.
/// </summary>
/// <remarks>
/// A map is what the player has learned about a game, and losing it
/// because they closed the window would be like losing the notes they
/// made beside it. So it is kept, and this is the shape it is kept in.
///
/// Text rather than anything packed, because it is small, it can be
/// read by whoever opens it, and a line that comes out wrong can be
/// seen to be wrong. A file from a later version is refused outright
/// rather than guessed at: a map is cheap to rebuild by playing, and a
/// half-understood one would be worse than none.
///
/// Directions are written as their own names rather than as the
/// abbreviations a player types, because the words a game is played in
/// are a matter for the parser and a file should not change meaning
/// when that vocabulary grows.
/// </remarks>
public static class MapFile
{
    private const string Marker = "rezrov map";
    private const int Version = 1;

    /// <summary>Writes the map as it stands.</summary>
    public static void Write(RoomGraph graph, TextWriter to)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(to);

        to.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{Marker} {Version}"));

        foreach (var room in graph.Rooms)
        {
            var (x, y) = room.Position is { } cell
                ? (cell.X.ToString(CultureInfo.InvariantCulture), cell.Y.ToString(CultureInfo.InvariantCulture))
                : ("-", "-");

            // A room known only by its name keys on the name itself,
            // so there is no number to write and none to read back.
            // The name being absent is what says there is an object,
            // since the number of a room without one is simply zero
            // and zero is a room a game could really have.
            var number = room.Key.Name is null
                ? room.Key.Number.ToString(CultureInfo.InvariantCulture)
                : "-";

            to.WriteLine(string.Create(CultureInfo.InvariantCulture, $"room {room.Id} {x} {y} {number} {room.Name}"));
        }

        foreach (var room in graph.Rooms)
        {
            foreach (var (direction, destination) in room.Exits)
            {
                to.WriteLine(string.Create(CultureInfo.InvariantCulture, $"link {room.Id} {direction} {destination}"));
            }

            foreach (var direction in room.Tried)
            {
                to.WriteLine(string.Create(CultureInfo.InvariantCulture, $"wall {room.Id} {direction}"));
            }
        }

        if (graph.Current is { } here)
        {
            to.WriteLine(string.Create(CultureInfo.InvariantCulture, $"here {here.Id}"));
        }
    }

    /// <summary>
    /// Reads a map back, or returns null where the text is not one
    /// this version knows how to read.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception, because a map that cannot be
    /// read is not a failure of the run. The player gets an empty map
    /// and fills it in again by playing, which is what they would have
    /// had anyway.
    /// </remarks>
    public static RoomGraph? Read(TextReader from)
    {
        ArgumentNullException.ThrowIfNull(from);

        if (from.ReadLine() is not { } head
            || !head.StartsWith(Marker, StringComparison.Ordinal)
            || !int.TryParse(head[Marker.Length..].Trim(), CultureInfo.InvariantCulture, out var version)
            || version != Version)
        {
            return null;
        }

        var graph = new RoomGraph();
        var rooms = new List<Room>();

        while (from.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var words = line.Split(' ', 6);

            switch (words[0])
            {
                case "room" when words.Length == 6 && Numbered(words[1], out var id) && id == rooms.Count:
                    rooms.Add(graph.Reopen(
                        Numbered(words[4], out var number) ? RoomKey.ForObject(number) : RoomKey.ForName(words[5]),
                        words[5],
                        Numbered(words[2], out var x) && Numbered(words[3], out var y) ? (x, y) : null));
                    break;

                case "link" when words.Length >= 4
                    && Held(rooms, words[1]) is { } from_
                    && Enum.TryParse<Direction>(words[2], out var walked)
                    && Numbered(words[3], out var to)
                    && to >= 0 && to < rooms.Count:
                    from_.Add(walked, to);
                    break;

                case "wall" when words.Length >= 3
                    && Held(rooms, words[1]) is { } at
                    && Enum.TryParse<Direction>(words[2], out var tried):
                    at.MarkTried(tried);
                    break;

                case "here" when words.Length >= 2 && Held(rooms, words[1]) is { } standing:
                    graph.StandIn(standing);
                    break;

                default:

                    // A line this version does not know is skipped
                    // rather than refused, so that a map written by a
                    // later one is still worth most of what it holds.
                    break;
            }
        }

        return graph;
    }

    private static bool Numbered(string word, out int number) =>
        int.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);

    private static Room? Held(List<Room> rooms, string word) =>
        Numbered(word, out var id) && id >= 0 && id < rooms.Count ? rooms[id] : null;

    /// <summary>The whole map as text, for a file or a test.</summary>
    public static string Written(RoomGraph graph)
    {
        var text = new StringBuilder();
        using var writer = new StringWriter(text, CultureInfo.InvariantCulture);

        Write(graph, writer);

        return text.ToString();
    }
}

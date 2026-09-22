namespace Rezrov.Mapping;

/// <summary>
/// The words a player types for a direction, and the geometry each one
/// implies.
/// </summary>
/// <remarks>
/// One table, read by everything: there is no second list of words to
/// keep in step with this one.
///
/// A map is built from what the player typed, so a command that names no
/// direction has to be refused rather than guessed at. Refusing leaves a
/// gap in the map, which is honest; guessing mints a passage that was
/// never walked, which is a lie the player has no way to spot. That is
/// the rule the whole of this project follows, and it starts here.
/// </remarks>
public static class Directions
{
    /// <summary>Every direction, once, in the enum's own order.</summary>
    public static IReadOnlyList<Direction> All { get; } =
    [
        Direction.North,
        Direction.Northeast,
        Direction.East,
        Direction.Southeast,
        Direction.South,
        Direction.Southwest,
        Direction.West,
        Direction.Northwest,
        Direction.Up,
        Direction.Down,
        Direction.In,
        Direction.Out,
    ];

    /// <summary>
    /// The verbs a player may put in front of a direction, which are
    /// stepped over before the direction itself is read.
    /// </summary>
    private static readonly string[] MovementVerbs =
        ["go", "walk", "run", "head", "travel", "proceed", "geh", "gehe"];

    /// <summary>
    /// Reads the direction a typed command names, if it names one.
    /// </summary>
    /// <remarks>
    /// Only the command's first word counts, after an optional verb of
    /// motion. "north", "n", and "go north" are all north; "take lamp"
    /// and "open the trap door" are nothing at all, and neither is a
    /// bare "go".
    ///
    /// A line holding several commands names no direction either, and
    /// that matters more than it looks. These parsers let a player write
    /// "n. n. u" and walk three rooms on one turn, and the only thing
    /// seen afterwards is where they ended up. Reading the first word
    /// would draw a passage north from where they started to somewhere
    /// three rooms away, which is a lie no one could spot from the map.
    /// </remarks>
    /// <param name="command">What the player typed.</param>
    /// <param name="direction">The direction named, if any.</param>
    /// <returns>Whether the command named a direction.</returns>
    public static bool TryParse(string? command, out Direction direction)
    {
        direction = default;

        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var line = command.ToLowerInvariant();
        if (Single(line) is not { } only)
        {
            return false;
        }

        var words = only.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var at = 0;
        while (at < words.Length && MovementVerbs.Contains(words[at]))
        {
            at++;
        }

        return at < words.Length && TryReadWord(words[at], out direction);
    }

    /// <summary>
    /// The one command a line holds, or null where it holds none or more
    /// than one.
    /// </summary>
    /// <remarks>
    /// A period, a comma and the word "then" all end a command, which is
    /// what lets "n. n. u" be three of them. A single trailing period is
    /// not a second command, so "n." is still north.
    /// </remarks>
    private static string? Single(string line)
    {
        var commands = line
            .Replace(" then ", ".", StringComparison.Ordinal)
            .Split(['.', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return commands.Length == 1 ? commands[0] : null;
    }

    /// <summary>
    /// The one table of direction words. Everything else here reads it.
    /// </summary>
    /// <remarks>
    /// The nautical words are for the games played aboard a ship, where
    /// the bow points north. Only their unambiguous spellings are here:
    /// a game that means "port" by "p" also has a hundred other uses for
    /// a single letter, and a wrong direction draws a passage that does
    /// not exist, which costs more than the one it misses.
    ///
    /// The German words are the ones Infocom's own translation of Zork
    /// answers to, read out of that story's own dictionary rather than
    /// guessed at, in both the spellings it takes: a story may be typed
    /// at with umlauts or with the e that stands in for one.
    ///
    /// One of its words is deliberately missing. "no" is northeast in
    /// German and the answer to a question in English, and nothing here
    /// knows which language it is reading. A German player still has
    /// "nordost" and "nordosten", and neither of those means anything in
    /// English.
    /// </remarks>
    private static bool TryReadWord(string word, out Direction direction)
    {
        Direction? found = word switch
        {
            "n" or "north" => Direction.North,
            "s" or "south" => Direction.South,
            "e" or "east" => Direction.East,
            "w" or "west" => Direction.West,
            "ne" or "northeast" => Direction.Northeast,
            "nw" or "northwest" => Direction.Northwest,
            "se" or "southeast" => Direction.Southeast,
            "sw" or "southwest" => Direction.Southwest,
            "u" or "up" => Direction.Up,
            "d" or "down" => Direction.Down,
            "in" or "inside" or "enter" => Direction.In,
            "out" or "outside" or "exit" => Direction.Out,
            "fore" or "forward" or "bow" => Direction.North,
            "aft" or "stern" => Direction.South,
            "port" => Direction.West,
            "starboard" => Direction.East,
            "nord" or "norden" => Direction.North,
            "sued" or "süd" or "sueden" or "süden" => Direction.South,
            "o" or "ost" or "osten" => Direction.East,
            "westen" => Direction.West,
            "nordost" or "nordosten" => Direction.Northeast,
            "nordwest" or "nordwesten" => Direction.Northwest,
            "so" or "suedost" or "südost" or "suedosten" or "südosten" =>
                Direction.Southeast,
            "suedwest" or "südwest" or "suedwesten" or "südwesten" =>
                Direction.Southwest,
            "rauf" or "hinauf" or "hoch" => Direction.Up,
            "runter" or "hinunter" or "herab" or "herunter" => Direction.Down,
            "hinein" => Direction.In,
            "raus" => Direction.Out,
            _ => null,
        };

        direction = found ?? default;
        return found is not null;
    }

    /// <summary>The direction that would undo this one.</summary>
    public static Direction Opposite(Direction direction) => direction switch
    {
        Direction.North => Direction.South,
        Direction.Northeast => Direction.Southwest,
        Direction.East => Direction.West,
        Direction.Southeast => Direction.Northwest,
        Direction.South => Direction.North,
        Direction.Southwest => Direction.Northeast,
        Direction.West => Direction.East,
        Direction.Northwest => Direction.Southeast,
        Direction.Up => Direction.Down,
        Direction.Down => Direction.Up,
        Direction.In => Direction.Out,
        Direction.Out => Direction.In,
        _ => direction,
    };

    /// <summary>
    /// Where this direction points on the map, or null where it points
    /// nowhere a flat map can show.
    /// </summary>
    /// <remarks>
    /// Y grows southward, as a screen's rows do, so north is negative.
    /// Up, down, in and out have no answer here: a staircase is not a
    /// step north, and this is what says so. See <see cref="Cell"/> for
    /// the separate question of where a room discovered that way is put.
    /// </remarks>
    public static (int X, int Y)? Offset(Direction direction) => direction switch
    {
        Direction.North => (0, -1),
        Direction.Northeast => (1, -1),
        Direction.East => (1, 0),
        Direction.Southeast => (1, 1),
        Direction.South => (0, 1),
        Direction.Southwest => (-1, 1),
        Direction.West => (-1, 0),
        Direction.Northwest => (-1, -1),
        _ => null,
    };

    /// <summary>
    /// Which cell a room reached this way should be put in, or null where
    /// the direction gives no clue and the room has to go wherever there
    /// is room for it.
    /// </summary>
    /// <remarks>
    /// This differs from <see cref="Offset"/> in one place, and it is a
    /// deliberate untruth in the service of a readable map: a room up a
    /// staircase is placed in the cell to the north, and one down a
    /// staircase to the south. Nothing else puts a whole cellar in a
    /// sensible place on a single sheet, and the drawing keeps the two
    /// apart anyway by giving a staircase its own kind of line rather
    /// than the one a passage north gets.
    /// </remarks>
    public static (int X, int Y)? Cell(Direction direction) => direction switch
    {
        Direction.Up => (0, -1),
        Direction.Down => (0, 1),
        _ => Offset(direction),
    };

    /// <summary>The short form of the direction's name.</summary>
    public static string Abbreviation(Direction direction) => direction switch
    {
        Direction.North => "n",
        Direction.Northeast => "ne",
        Direction.East => "e",
        Direction.Southeast => "se",
        Direction.South => "s",
        Direction.Southwest => "sw",
        Direction.West => "w",
        Direction.Northwest => "nw",
        Direction.Up => "u",
        Direction.Down => "d",
        Direction.In => "in",
        Direction.Out => "out",
        _ => "?",
    };
}

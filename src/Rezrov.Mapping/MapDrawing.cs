using System.Text;

namespace Rezrov.Mapping;

/// <summary>
/// The map as plain text: a box for every room, a line for every passage
/// that joins two neighbors, and a list of the ones that do not.
/// </summary>
/// <remarks>
/// Plain ASCII, deliberately. This drawing is meant to be written to a
/// file, read in whatever happens to open it, and compared character for
/// character in a test, and box drawing characters make all three worse
/// for a picture that is no clearer. A frontend with pixels can draw
/// something better from the same graph.
///
/// The drawing shows what the grid can show honestly and refuses to
/// improvise the rest. A passage whose two rooms did not end up next to
/// each other is named underneath rather than drawn as a line that would
/// have to run through somewhere else, and a passage with nowhere to go
/// on a flat grid, which is what in and out are, is named there too.
/// Directions that were tried and came to nothing are not drawn at all;
/// they are in the graph for something with more room to show them.
/// </remarks>
public static class MapDrawing
{
    private const int InnerWidth = 18;
    private const int BoxWidth = InnerWidth + 2;
    private const int BoxHeight = 3;
    private const int GapWidth = 5;
    private const int GapHeight = 1;
    private const int PitchX = BoxWidth + GapWidth;
    private const int PitchY = BoxHeight + GapHeight;

    /// <summary>Draws the graph.</summary>
    public static string Draw(RoomGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var placed = graph.Rooms.Where(room => room.Position is not null).ToList();
        if (placed.Count == 0)
        {
            return "No rooms have been mapped.\n";
        }

        var minX = placed.Min(room => room.Position!.Value.X);
        var minY = placed.Min(room => room.Position!.Value.Y);
        var across = placed.Max(room => room.Position!.Value.X) - minX + 1;
        var down = placed.Max(room => room.Position!.Value.Y) - minY + 1;

        var canvas = new char[((down - 1) * PitchY) + BoxHeight][];
        for (var row = 0; row < canvas.Length; row++)
        {
            canvas[row] = new string(' ', ((across - 1) * PitchX) + BoxWidth).ToCharArray();
        }

        var truncated = new List<Room>();
        foreach (var room in placed)
        {
            if (DrawBox(canvas, room, ReferenceEquals(room, graph.Current), minX, minY))
            {
                truncated.Add(room);
            }
        }

        var occupied = placed.Select(room => room.Position!.Value).ToHashSet();

        var undrawn = new List<string>();
        foreach (var room in placed)
        {
            DrawExits(canvas, graph, room, minX, minY, occupied, undrawn);
        }

        return Assemble(canvas, truncated, undrawn);
    }

    /// <summary>
    /// Draws one room's box, and says whether its name had to be cut to
    /// fit.
    /// </summary>
    private static bool DrawBox(char[][] canvas, Room room, bool current, int minX, int minY)
    {
        var (left, top) = Corner(room, minX, minY);

        canvas[top][left] = '+';
        canvas[top][left + BoxWidth - 1] = '+';
        canvas[top + 2][left] = '+';
        canvas[top + 2][left + BoxWidth - 1] = '+';

        for (var i = 1; i < BoxWidth - 1; i++)
        {
            canvas[top][left + i] = '-';
            canvas[top + 2][left + i] = '-';
        }

        canvas[top + 1][left] = '|';
        canvas[top + 1][left + BoxWidth - 1] = '|';

        // The room the player is standing in is marked where the name
        // would otherwise begin, so the mark costs a character of name
        // only in the room it is actually on.
        var label = (current ? "*" : " ") + room.Name;
        var cut = label.Length > InnerWidth;
        if (cut)
        {
            label = label[..InnerWidth];
        }

        for (var i = 0; i < label.Length; i++)
        {
            canvas[top + 1][left + 1 + i] = label[i];
        }

        return cut;
    }

    private static void DrawExits(
        char[][] canvas,
        RoomGraph graph,
        Room room,
        int minX,
        int minY,
        HashSet<(int X, int Y)> occupied,
        List<string> undrawn)
    {
        foreach (var direction in Directions.All)
        {
            if (!room.Exits.TryGetValue(direction, out var id))
            {
                continue;
            }

            var destination = graph.Rooms[id];
            if (Reason(room, destination, direction, occupied) is { } reason)
            {
                undrawn.Add(Describe(room, direction, destination, reason));
                continue;
            }

            var run = Run(room, destination, direction, occupied)!.Value;
            if (!DrawPassage(canvas, room, destination, direction, minX, minY, run))
            {
                undrawn.Add(Describe(room, direction, destination, "line already taken"));
            }
        }
    }

    /// <summary>
    /// How many cells of line it takes to reach the destination, or null
    /// where a line cannot reach it at all.
    /// </summary>
    /// <remarks>
    /// Two rooms need not be neighbors to be joined. A room placed early
    /// drifts away from the one next to it as the map is shoved aside to
    /// make space elsewhere, and a straight line across the empty cells
    /// between them is still the truth: it goes the way the player
    /// walked and passes through nothing. What is refused is a line that
    /// would have to bend, or one that would run over a room that has
    /// nothing to do with it.
    ///
    /// A diagonal is one character in the corner between four boxes and
    /// has nowhere to run to, so it has to be a neighbor or nothing.
    /// </remarks>
    private static int? Run(
        Room room,
        Room destination,
        Direction direction,
        HashSet<(int X, int Y)> occupied)
    {
        if (Directions.Cell(direction) is not { } step
            || room.Position is not { } here
            || destination.Position is not { } there)
        {
            return null;
        }

        var span = step.X != 0
            ? (there.X - here.X) / step.X
            : (there.Y - here.Y) / step.Y;

        if (span < 1 || (here.X + (step.X * span), here.Y + (step.Y * span)) != there)
        {
            return null;
        }

        if (span > 1 && Directions.Offset(direction) is { X: not 0, Y: not 0 })
        {
            return null;
        }

        for (var i = 1; i < span; i++)
        {
            if (occupied.Contains((here.X + (step.X * i), here.Y + (step.Y * i))))
            {
                return null;
            }
        }

        return span;
    }

    /// <summary>
    /// Why this passage cannot be a line between two boxes, or null when
    /// it can.
    /// </summary>
    private static string? Reason(
        Room room,
        Room destination,
        Direction direction,
        HashSet<(int X, int Y)> occupied)
    {
        if (ReferenceEquals(room, destination))
        {
            return "loops back";
        }

        if (Directions.Cell(direction) is null)
        {
            return "nowhere to point on a grid";
        }

        if (room.Position is null || destination.Position is null)
        {
            return "not placed";
        }

        if (Run(room, destination, direction, occupied) is null)
        {
            return "no clear line";
        }

        // A diagonal is one character long, with no room on it for an
        // arrowhead, so a one-way diagonal has to be said in words.
        if (Directions.Offset(direction) is { } offset
            && offset.X != 0
            && offset.Y != 0
            && !IsTwoWay(room, destination, direction))
        {
            return "one way";
        }

        return null;
    }

    /// <summary>
    /// Whether the room at the far end names a passage back along the
    /// direction this one was walked.
    /// </summary>
    private static bool IsTwoWay(Room room, Room destination, Direction direction) =>
        destination.Exits.TryGetValue(Directions.Opposite(direction), out var back)
        && back == room.Id;

    private static bool DrawPassage(
        char[][] canvas,
        Room room,
        Room destination,
        Direction direction,
        int minX,
        int minY,
        int run)
    {
        var (left, top) = Corner(room, minX, minY);
        var center = left + (BoxWidth / 2);
        var twoWay = IsTwoWay(room, destination, direction);

        switch (direction)
        {
            // A staircase gets a dotted line rather than the solid one a
            // passage north or south gets, because the room above it was
            // put there to be readable and is not really north of here.
            case Direction.North:
            case Direction.Up:
                return PutLine(
                    canvas,
                    top - (run * PitchY) + BoxHeight,
                    top - GapHeight,
                    center,
                    vertical: true,
                    direction == Direction.Up ? ':' : '|',
                    twoWay || direction == Direction.Up ? null : '^',
                    headFirst: true);

            case Direction.South:
            case Direction.Down:
                return PutLine(
                    canvas,
                    top + BoxHeight,
                    top + (run * PitchY) - GapHeight,
                    center,
                    vertical: true,
                    direction == Direction.Down ? ':' : '|',
                    twoWay || direction == Direction.Down ? null : 'v',
                    headFirst: false);

            case Direction.East:
                return PutLine(
                    canvas,
                    left + BoxWidth,
                    left + (run * PitchX) - 1,
                    top + 1,
                    vertical: false,
                    '-',
                    twoWay ? null : '>',
                    headFirst: false);

            case Direction.West:
                return PutLine(
                    canvas,
                    left - (run * PitchX) + BoxWidth,
                    left - 1,
                    top + 1,
                    vertical: false,
                    '-',
                    twoWay ? null : '<',
                    headFirst: true);

            case Direction.Northeast:
                return Put(canvas, top - GapHeight, left + BoxWidth + (GapWidth / 2), '/');

            case Direction.Northwest:
                return Put(canvas, top - GapHeight, left - GapWidth + (GapWidth / 2), '\\');

            case Direction.Southeast:
                return Put(canvas, top + BoxHeight, left + BoxWidth + (GapWidth / 2), '\\');

            case Direction.Southwest:
                return Put(canvas, top + BoxHeight, left - GapWidth + (GapWidth / 2), '/');

            default:
                return false;
        }
    }

    /// <summary>
    /// Writes one character, unless something else is already there.
    /// </summary>
    /// <remarks>
    /// Two passages can want the same line: a room reached both north and
    /// up from here is in one cell, with one gap between the boxes to
    /// draw in. The first one drawn keeps the line and the second is
    /// named underneath, which is the same choice made everywhere else
    /// here. Writing the same character twice is not a clash, since a
    /// passage walked both ways is drawn from each end.
    /// </remarks>
    private static bool Put(char[][] canvas, int row, int column, char glyph)
    {
        if (row < 0 || row >= canvas.Length || column < 0 || column >= canvas[row].Length)
        {
            return false;
        }

        if (canvas[row][column] == glyph)
        {
            return true;
        }

        if (canvas[row][column] != ' ')
        {
            return false;
        }

        canvas[row][column] = glyph;
        return true;
    }

    /// <summary>
    /// Writes a whole line between two boxes, from
    /// <paramref name="first"/> to <paramref name="last"/> along one
    /// axis, with an arrowhead at the end nearest the room it points at
    /// for a passage known to run one way.
    /// </summary>
    /// <remarks>
    /// All of it or none of it. A line that would break where something
    /// already crosses it is not drawn at all and is named underneath
    /// instead, because half a line reads as a passage to somewhere it
    /// does not go.
    /// </remarks>
    private static bool PutLine(
        char[][] canvas,
        int first,
        int last,
        int fixedAt,
        bool vertical,
        char glyph,
        char? head,
        bool headFirst)
    {
        var headAt = headFirst ? first : last;

        for (var pass = 0; pass < 2; pass++)
        {
            for (var at = first; at <= last; at++)
            {
                var row = vertical ? at : fixedAt;
                var column = vertical ? fixedAt : at;
                var wanted = at == headAt && head is { } arrow ? arrow : glyph;

                if (pass == 0 && !Clear(canvas, row, column, wanted))
                {
                    return false;
                }

                if (pass == 1)
                {
                    canvas[row][column] = wanted;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Whether that cell is on the canvas and free for the character
    /// wanted, which a cell already holding it is.
    /// </summary>
    private static bool Clear(char[][] canvas, int row, int column, char glyph) =>
        row >= 0
        && row < canvas.Length
        && column >= 0
        && column < canvas[row].Length
        && (canvas[row][column] == ' ' || canvas[row][column] == glyph);

    private static (int Left, int Top) Corner(Room room, int minX, int minY)
    {
        var position = room.Position!.Value;
        return ((position.X - minX) * PitchX, (position.Y - minY) * PitchY);
    }

    /// <summary>
    /// One line of the list underneath, in columns, with both names in
    /// full however long they are.
    /// </summary>
    private static string Describe(Room room, Direction direction, Room destination, string reason) =>
        "  "
        + room.Name.PadRight(InnerWidth)
        + "  " + Directions.Abbreviation(direction).PadRight(3)
        + "  " + destination.Name.PadRight(InnerWidth)
        + "  (" + reason + ")";

    private static string Assemble(char[][] canvas, List<Room> truncated, List<string> undrawn)
    {
        var text = new StringBuilder();

        foreach (var row in canvas)
        {
            text.Append(new string(row).TrimEnd()).Append('\n');
        }

        if (truncated.Count > 0)
        {
            text.Append("\nNames cut to fit:\n");
            foreach (var room in truncated)
            {
                text.Append("  ").Append(room.Name).Append('\n');
            }
        }

        if (undrawn.Count > 0)
        {
            text.Append("\nPassages not drawn:\n");
            foreach (var line in undrawn)
            {
                text.Append(line).Append('\n');
            }
        }

        return text.ToString();
    }
}

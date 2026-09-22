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
/// A passage is drawn straight wherever it can be, and bent round
/// whatever is in the way when it cannot. The gaps between the boxes
/// make a lattice across the whole picture, since no box ever occupies
/// the columns to the right of one or the row beneath one, so a line has
/// somewhere to go even when the two rooms have drifted well apart.
/// Every straight line is drawn before any bent one, because a bent line
/// has the run of the picture while a straight one has only the gap
/// between two boxes.
///
/// What is left is named underneath rather than improvised: a passage
/// with nowhere to go on a flat grid, which is what in and out are, and
/// one the lattice is too crowded to get a line through. Directions that
/// were tried and came to nothing are not drawn at all; they are in the
/// graph for something with more room to show them.
///
/// One thing is drawn and named: a diagonal that runs one way. It is a
/// single character with nowhere to put an arrowhead, and a line the
/// reader has to look up beats no line at all, since the line is the
/// only thing that shows the two rooms are joined.
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
        var oneWay = new List<string>();
        var bent = new List<string>();

        // Straight lines first, every one of them, before any bent line
        // is allowed to take up room. A bent line has the whole picture
        // to find its way through; a straight one has only the gap
        // between two boxes, and losing that to a passage which had
        // somewhere else to go would be a poor trade.
        var crooked = new List<(Room Room, Direction Direction, Room To)>();
        foreach (var room in placed)
        {
            DrawExits(canvas, graph, room, minX, minY, occupied, undrawn, oneWay, crooked);
        }

        // The nearest first: a short line has few ways through and a
        // long one has many, so letting the long ones go first spends
        // the narrow channels on passages that had alternatives.
        crooked.Sort((left, right) => Apart(left).CompareTo(Apart(right)));

        var joined = new HashSet<(int, int)>();
        foreach (var (room, direction, destination) in crooked)
        {
            // One bent line between two rooms is enough. The way back
            // along it is the same line, and so is a second passage
            // between the same pair, so those are named rather than
            // drawn over the top of what is already there.
            if (joined.Contains(Pair(room, destination)))
            {
                bent.Add(Describe(room, direction, destination, "on that same line"));
            }
            else if (Bend(canvas, room, destination, direction, minX, minY, joined))
            {
                bent.Add(Describe(room, direction, destination, "bends"));
            }
            else
            {
                undrawn.Add(Describe(room, direction, destination, NoLine));
            }
        }

        return Assemble(canvas, truncated, undrawn, oneWay, bent);
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
        List<string> undrawn,
        List<string> oneWay,
        List<(Room Room, Direction Direction, Room To)> crooked)
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
                // A straight line cannot reach it, but a bent one often
                // can: most of the rooms that drift apart end up two or
                // three cells away, not across the map. That is tried
                // once every straight line has been drawn.
                if (reason == NoLine)
                {
                    crooked.Add((room, direction, destination));
                }
                else
                {
                    undrawn.Add(Describe(room, direction, destination, reason));
                }

                continue;
            }

            var run = Run(room, destination, direction, occupied)!.Value;
            if (!DrawPassage(canvas, room, destination, direction, minX, minY, run))
            {
                undrawn.Add(Describe(room, direction, destination, "line already taken"));
                continue;
            }

            // A diagonal is one character of line with nowhere to put an
            // arrowhead. Drawing it and saying underneath which way it
            // runs shows more than leaving it off the map did.
            if (Directions.Offset(direction) is { X: not 0, Y: not 0 }
                && !IsTwoWay(room, destination, direction))
            {
                oneWay.Add(Describe(room, direction, destination, "one way"));
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
            return NoLine;
        }

        return null;
    }

    private const string NoLine = "no clear line";

    /// <summary>The two rooms a passage joins, in a fixed order.</summary>
    private static (int, int) Pair(Room room, Room destination) =>
        (Math.Min(room.Id, destination.Id), Math.Max(room.Id, destination.Id));

    /// <summary>How many cells lie between a passage's two rooms.</summary>
    private static int Apart((Room Room, Direction Direction, Room To) passage)
    {
        var here = passage.Room.Position!.Value;
        var there = passage.To.Position!.Value;
        return Math.Max(Math.Abs(there.X - here.X), Math.Abs(there.Y - here.Y));
    }

    /// <summary>
    /// Joins two rooms with a line that bends, when a straight one
    /// cannot reach.
    /// </summary>
    /// <remarks>
    /// The gaps between the boxes make a lattice the whole way across
    /// the picture: no box ever occupies the five columns to the right
    /// of one, or the row underneath one, so those columns and rows are
    /// free corridors from edge to edge. A line leaves its room into one
    /// of them and finds its way to the room it leads to.
    ///
    /// It is found rather than planned: the shortest way through the
    /// blank characters, paying a little extra for every corner so the
    /// result is as straight as the space allows. Anything already drawn
    /// is in the way, so lines neither cross each other nor run over a
    /// room that has nothing to do with them, and a line that cannot get
    /// through is not drawn at all.
    ///
    /// Where two rooms are already joined by a bent line, a second
    /// passage between the same two is not drawn again on top of it. It
    /// is still named underneath, so nothing is hidden.
    /// </remarks>
    private static bool Bend(
        char[][] canvas,
        Room room,
        Room destination,
        Direction direction,
        int minX,
        int minY,
        HashSet<(int, int)> joined)
    {
        if (Leaving(canvas, room, direction, minX, minY) is not { } start
            || Leaving(canvas, destination, Directions.Opposite(direction), minX, minY) is not { } end)
        {
            return false;
        }

        if (Path(canvas, start, end) is not { } path)
        {
            return false;
        }

        // A bent line carries an arrowhead only where nothing at all is
        // known to come back. The way back need not be the opposite
        // direction, which it often is not once a map has folded over:
        // what matters is whether the far room names this one at all.
        var back = destination.Exits.Values.Contains(room.Id);

        Trace(canvas, path, direction, back);
        joined.Add(Pair(room, destination));
        return true;
    }

    /// <summary>
    /// A free character just outside a room's box where a passage in that
    /// direction can begin, or null when there is none.
    /// </summary>
    /// <remarks>
    /// A room can have both a passage south and a staircase down, and
    /// they cannot both start from the middle of the same edge. The row
    /// under a box is free along its whole width, though, so a line that
    /// finds the middle taken shuffles along the edge until it finds
    /// somewhere to start. Which is also how the picture ends up looking
    /// right: two lines leaving the same wall a few characters apart,
    /// rather than one of them not being drawn.
    /// </remarks>
    private static (int Row, int Column)? Leaving(
        char[][] canvas,
        Room room,
        Direction direction,
        int minX,
        int minY)
    {
        if (room.Position is null)
        {
            return null;
        }

        var (left, top) = Corner(room, minX, minY);
        var center = left + (BoxWidth / 2);

        var row = direction switch
        {
            Direction.North or Direction.Up or Direction.Northeast or Direction.Northwest =>
                top - GapHeight,
            Direction.South or Direction.Down or Direction.Southeast or Direction.Southwest =>
                top + BoxHeight,
            _ => top + 1,
        };

        // Sideways there is one row to leave by, so the column is fixed.
        if (direction is Direction.East)
        {
            return Free(canvas, [(row, left + BoxWidth)]);
        }

        if (direction is Direction.West)
        {
            return Free(canvas, [(row, left - 1)]);
        }

        // Up and down the box there is a whole edge to choose from.
        // A diagonal starts at its corner, and everything else in the
        // middle, and both work outwards from there.
        var from = direction switch
        {
            Direction.Northeast or Direction.Southeast => left + BoxWidth,
            Direction.Northwest or Direction.Southwest => left - 1,
            _ => center,
        };

        var along = new List<(int Row, int Column)>();
        for (var step = 0; step < BoxWidth; step++)
        {
            foreach (var column in step == 0 ? [from] : new[] { from - step, from + step })
            {
                if (column >= left - 1 && column <= left + BoxWidth)
                {
                    along.Add((row, column));
                }
            }
        }

        return Free(canvas, along);
    }

    private static (int Row, int Column)? Free(
        char[][] canvas,
        IEnumerable<(int Row, int Column)> options)
    {
        foreach (var option in options)
        {
            if (Blank(canvas, option))
            {
                return option;
            }
        }

        return null;
    }

    /// <summary>
    /// The cheapest way from one character to another through the blanks,
    /// counting a corner as worth a couple of extra steps so the line
    /// comes out straight where it can.
    /// </summary>
    private static List<(int Row, int Column)>? Path(
        char[][] canvas,
        (int Row, int Column) from,
        (int Row, int Column) to)
    {
        if (!Blank(canvas, from) || !Blank(canvas, to))
        {
            return null;
        }

        (int Row, int Column)[] steps = [(-1, 0), (0, 1), (1, 0), (0, -1)];

        var best = new Dictionary<(int, int, int), int>();
        var back = new Dictionary<(int, int, int), (int, int, int)>();
        var queue = new PriorityQueue<(int Row, int Column, int Facing), int>();

        for (var facing = 0; facing < steps.Length; facing++)
        {
            best[(from.Row, from.Column, facing)] = 0;
            queue.Enqueue((from.Row, from.Column, facing), 0);
        }

        while (queue.TryDequeue(out var at, out var cost))
        {
            if (cost > best.GetValueOrDefault((at.Row, at.Column, at.Facing), int.MaxValue))
            {
                continue;
            }

            if ((at.Row, at.Column) == to)
            {
                return Walk(back, at, from);
            }

            for (var facing = 0; facing < steps.Length; facing++)
            {
                var next = (Row: at.Row + steps[facing].Row, Column: at.Column + steps[facing].Column);
                if (!Blank(canvas, next))
                {
                    continue;
                }

                var price = cost + 1 + (facing == at.Facing ? 0 : 2);
                var key = (next.Row, next.Column, facing);
                if (price >= best.GetValueOrDefault(key, int.MaxValue))
                {
                    continue;
                }

                best[key] = price;
                back[key] = (at.Row, at.Column, at.Facing);
                queue.Enqueue((next.Row, next.Column, facing), price);
            }
        }

        return null;
    }

    private static List<(int Row, int Column)> Walk(
        Dictionary<(int, int, int), (int, int, int)> back,
        (int Row, int Column, int Facing) at,
        (int Row, int Column) from)
    {
        var path = new List<(int Row, int Column)>();
        var key = (at.Row, at.Column, at.Facing);

        while (true)
        {
            path.Add((key.Item1, key.Item2));
            if ((key.Item1, key.Item2) == from)
            {
                break;
            }

            key = back[key];
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// Draws a found path, with a corner wherever it turns.
    /// </summary>
    private static void Trace(
        char[][] canvas,
        List<(int Row, int Column)> path,
        Direction direction,
        bool twoWay)
    {
        for (var i = 0; i < path.Count; i++)
        {
            var before = i == 0 ? path[i] : path[i - 1];
            var after = i == path.Count - 1 ? path[i] : path[i + 1];

            var across = before.Column != after.Column;
            var down = before.Row != after.Row;

            canvas[path[i].Row][path[i].Column] = (across, down) switch
            {
                (true, true) => '+',
                (true, false) => '-',
                _ => '|',
            };
        }

        if (twoWay || path.Count < 2)
        {
            return;
        }

        // The arrowhead points into the room it leads to, which is not
        // the way the line happened to be travelling when it got there:
        // a line that comes round the houses and arrives from the west
        // is still a passage south, and an arrow reading east beside the
        // box it enters from above says nothing true.
        var last = path[^1];
        canvas[last.Row][last.Column] = direction switch
        {
            Direction.East => '>',
            Direction.West => '<',
            Direction.South or Direction.Down or Direction.Southeast or Direction.Southwest => 'v',
            _ => '^',
        };
    }

    private static bool Blank(char[][] canvas, (int Row, int Column) at) =>
        at.Row >= 0
        && at.Row < canvas.Length
        && at.Column >= 0
        && at.Column < canvas[at.Row].Length
        && canvas[at.Row][at.Column] == ' ';

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

    private static string Assemble(
        char[][] canvas,
        List<Room> truncated,
        List<string> undrawn,
        List<string> oneWay,
        List<string> bent)
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

        if (bent.Count > 0)
        {
            text.Append("\nJoined by a line that bends:\n");
            foreach (var line in bent)
            {
                text.Append(line).Append('\n');
            }
        }

        if (oneWay.Count > 0)
        {
            text.Append("\nDrawn, but running one way only:\n");
            foreach (var line in oneWay)
            {
                text.Append(line).Append('\n');
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

using Rezrov.Mapping;

namespace Rezrov.Gui;

/// <summary>
/// One room, as a box to be drawn.
/// </summary>
/// <param name="Room">Which room in the graph this was.</param>
/// <param name="Name">What the game called it.</param>
/// <param name="Left">The left edge, in map units.</param>
/// <param name="Top">The top edge, in map units.</param>
/// <param name="Current">Whether the player is standing in it.</param>
public readonly record struct MapBox(int Room, string Name, double Left, double Top, bool Current)
{
    /// <summary>The middle of the box, which lines are aimed at.</summary>
    public (double X, double Y) Center =>
        (Left + (MapShot.BoxWidth / 2), Top + (MapShot.BoxHeight / 2));
}

/// <summary>
/// One passage, as a line to be drawn between two box edges.
/// </summary>
/// <remarks>
/// A line stands for both directions between a pair of rooms, because
/// two lines between the same two boxes would sit on top of each other
/// and say nothing the one line does not. What it has to carry instead
/// is everything that would otherwise be lost by joining them: whether
/// the passage has been walked both ways, and which direction was
/// walked from each end when that is not obvious from where the boxes
/// sit.
/// </remarks>
/// <param name="X1">Where the line leaves the first box.</param>
/// <param name="Y1">Where the line leaves the first box.</param>
/// <param name="X2">Where the line meets the second box.</param>
/// <param name="Y2">Where the line meets the second box.</param>
/// <param name="Dashed">Whether it is a way up, down, in or out.</param>
/// <param name="ArrowAtOne">The passage runs only towards box one.</param>
/// <param name="ArrowAtTwo">The passage runs only towards box two.</param>
/// <param name="LabelOne">What was walked at box one, if odd.</param>
/// <param name="LabelTwo">What was walked at box two, if odd.</param>
public readonly record struct MapLine(
    double X1,
    double Y1,
    double X2,
    double Y2,
    bool Dashed,
    bool ArrowAtOne,
    bool ArrowAtTwo,
    string? LabelOne,
    string? LabelTwo);

/// <summary>
/// The map worked out as shapes, taken all at once so that it can be
/// drawn without the game running underneath it.
/// </summary>
/// <remarks>
/// A window draws on its own thread while the game plays on another,
/// and the graph is a plain object graph that would be torn by the two
/// meeting. So the graph is turned into flat shapes once, while the
/// game is held off it, and every repaint after that reads only this.
///
/// Working the shapes out here rather than in the painting also means
/// the layout can be checked without a window, which is most of what
/// there is to get wrong.
/// </remarks>
public sealed class MapShot
{
    /// <summary>How wide a room's box is, in map units.</summary>
    public const double BoxWidth = 92;

    /// <summary>How tall a room's box is, in map units.</summary>
    public const double BoxHeight = 38;

    /// <summary>How far apart two neighboring cells sit across.</summary>
    public const double PitchX = 132;

    /// <summary>How far apart two neighboring cells sit down.</summary>
    public const double PitchY = 86;

    /// <summary>The blank kept around the whole map.</summary>
    public const double Margin = 24;

    private MapShot(
        IReadOnlyList<MapBox> boxes,
        IReadOnlyList<MapLine> lines,
        (double Left, double Top, double Width, double Height) extent,
        int passages)
    {
        Boxes = boxes;
        Lines = lines;
        Extent = extent;
        Passages = passages;
    }

    /// <summary>An empty map, which is all there is at first.</summary>
    public static MapShot None { get; } = new([], [], (0, 0, 0, 0), 0);

    /// <summary>Every room that has been placed.</summary>
    public IReadOnlyList<MapBox> Boxes { get; }

    /// <summary>Every passage that can be drawn as a line.</summary>
    public IReadOnlyList<MapLine> Lines { get; }

    /// <summary>
    /// The map's own bounds, margin included, which is what a window
    /// fits itself around.
    /// </summary>
    public (double Left, double Top, double Width, double Height) Extent { get; }

    /// <summary>
    /// How many passages were found, which is more than the number of
    /// lines whenever one has been walked both ways.
    /// </summary>
    public int Passages { get; }

    /// <summary>
    /// How the whole map sits in a pane of the given size.
    /// </summary>
    /// <remarks>
    /// The same arithmetic an Infocom screen is fitted by, for the same
    /// reason: a window draws through it one way and a pointer comes
    /// back through it the other, and they must not be two separate
    /// calculations that can drift.
    ///
    /// Two things are the map's own. Its corner is not the origin,
    /// since rooms are laid out around the room the game started in and
    /// half of them end up at negative cells, so the corner is taken
    /// off the margin afterwards. And a map of one room is fitted as
    /// though it were a small map rather than scaled until the one room
    /// fills the pane, with the little there is sitting in the middle.
    /// </remarks>
    public ScreenFit Fitted(double width, double height)
    {
        var (left, top, across, down) = Extent;
        var (wide, tall) = (Math.Max(across, PitchX * 5), Math.Max(down, PitchY * 5));

        if (ScreenFit.Of((width, height), ((int)wide, (int)tall)) is not { } fit)
        {
            return new ScreenFit(1, 0, 0);
        }

        return new ScreenFit(
            fit.Scale,
            fit.Across + ((((wide - across) / 2) - left) * fit.Scale),
            fit.Down + ((((tall - down) / 2) - top) * fit.Scale));
    }

    /// <summary>
    /// How the map should sit in the pane while it is following the
    /// player rather than being held where they put it.
    /// </summary>
    /// <remarks>
    /// The whole game while the whole game will go in and still be
    /// read, and the player's own surroundings once it will not. The
    /// turn where those two part company is the turn a map stops being
    /// a map and becomes a picture of one, which is exactly when
    /// knowing where you are standing matters most.
    /// </remarks>
    /// <param name="width">How wide the pane is.</param>
    /// <param name="height">How tall the pane is.</param>
    /// <param name="readable">
    /// The smallest scale a room's name can still be read at.
    /// </param>
    public ScreenFit Following(double width, double height, double readable)
    {
        var whole = Fitted(width, height);

        return whole.Scale >= readable ? whole : Centered(readable, width, height);
    }

    /// <summary>
    /// The map at a set scale with the player's own room in the middle
    /// of the pane.
    /// </summary>
    /// <remarks>
    /// For when the whole map will not go in the pane at a size anyone
    /// could read. A map of thirty rooms in a pane beside a game is
    /// scaled until the room names are a few pixels of gray, which is
    /// the moment a player most wants to know where they are. Keeping
    /// them in the middle at a size that can be read is worth more than
    /// showing them everything at a size that cannot.
    /// </remarks>
    public ScreenFit Centered(double scale, double width, double height)
    {
        var (left, top, across, down) = Extent;

        var (x, y) = Boxes
            .Where(box => box.Current)
            .Select(box => box.Center)
            .FirstOrDefault((left + (across / 2), top + (down / 2)));

        return new ScreenFit(scale, (width / 2) - (x * scale), (height / 2) - (y * scale));
    }

    /// <summary>
    /// The room whose box covers a point in the pane, or null where the
    /// point is on the blank between them.
    /// </summary>
    public int? At(ScreenFit fit, double x, double y)
    {
        var (across, down) = fit.Unscaled(x, y);

        foreach (var box in Boxes)
        {
            if (across >= box.Left
                && across <= box.Left + BoxWidth
                && down >= box.Top
                && down <= box.Top + BoxHeight)
            {
                return box.Room;
            }
        }

        return null;
    }

    /// <summary>
    /// The map as it stands. Called with the game held off the graph.
    /// </summary>
    public static MapShot Of(RoomGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var boxes = new List<MapBox>();
        var at = new Dictionary<int, MapBox>();

        foreach (var room in graph.Rooms)
        {
            // A room with nowhere to be is one placement could not find
            // a cell for. There is nothing to draw and nothing to say
            // about it, so it is simply not on the map.
            if (room.Position is not { } cell)
            {
                continue;
            }

            var box = new MapBox(
                room.Id,
                room.Name,
                (cell.X * PitchX) - (BoxWidth / 2),
                (cell.Y * PitchY) - (BoxHeight / 2),
                ReferenceEquals(room, graph.Current));

            boxes.Add(box);
            at[room.Id] = box;
        }

        var (lines, passages) = Joined(graph, at);

        return new MapShot(boxes, lines, Around(boxes), passages);
    }

    /// <summary>
    /// Gathers the passages into one line for each pair of rooms they
    /// run between.
    /// </summary>
    private static (List<MapLine> Lines, int Passages) Joined(
        RoomGraph graph,
        Dictionary<int, MapBox> at)
    {
        // Keyed on the pair with the lower room first, so that a
        // passage and the way back land on the same entry however they
        // were walked.
        var pairs = new Dictionary<(int Low, int High), (List<Direction> Up, List<Direction> Down)>();
        var passages = 0;

        foreach (var room in graph.Rooms)
        {
            if (!at.ContainsKey(room.Id))
            {
                continue;
            }

            foreach (var (direction, destination) in room.Exits)
            {
                if (!at.ContainsKey(destination))
                {
                    continue;
                }

                passages++;

                var key = (Low: Math.Min(room.Id, destination), High: Math.Max(room.Id, destination));

                if (!pairs.TryGetValue(key, out var walked))
                {
                    walked = ([], []);
                    pairs[key] = walked;
                }

                // "Up" here is the list of directions walked from the
                // lower-numbered room, which has nothing to do with
                // walking upwards.
                (room.Id == key.Low ? walked.Up : walked.Down).Add(direction);
            }
        }

        var lines = new List<MapLine>();

        foreach (var (key, walked) in pairs)
        {
            if (Drawn(at[key.Low], at[key.High], walked.Up, walked.Down) is { } line)
            {
                lines.Add(line);
            }
        }

        return (lines, passages);
    }

    /// <summary>
    /// The line between one pair of rooms, or null where the two sit in
    /// the same place and there is no line to draw.
    /// </summary>
    private static MapLine? Drawn(
        MapBox one,
        MapBox two,
        List<Direction> fromOne,
        List<Direction> fromTwo)
    {
        var (ax, ay) = one.Center;
        var (bx, by) = two.Center;

        if (ax == bx && ay == by)
        {
            return null;
        }

        var (x1, y1) = Edge(one, bx - ax, by - ay);
        var (x2, y2) = Edge(two, ax - bx, ay - by);

        return new MapLine(
            x1,
            y1,
            x2,
            y2,
            fromOne.Concat(fromTwo).Any(d => Directions.Offset(d) is null),
            fromOne.Count == 0,
            fromTwo.Count == 0,
            Label(fromOne, fromTwo),
            Label(fromTwo, fromOne));
    }

    /// <summary>
    /// What to write at one end of a line, or null where the line
    /// already says it.
    /// </summary>
    /// <remarks>
    /// Most passages need nothing written on them: a box to the north
    /// joined by a line is a way north, and saying so would only crowd
    /// the map. Two cases do need it. A way up, down, in or out has no
    /// direction on the page at all, since the rooms were put wherever
    /// there was room. And a pair walked by two directions that are not
    /// each other's opposite is worth saying out loud, because the one
    /// line can no longer be read as a single passage.
    /// </remarks>
    private static string? Label(List<Direction> here, List<Direction> back)
    {
        foreach (var direction in here)
        {
            if (Directions.Offset(direction) is null
                || back.Any(other => Directions.Opposite(other) != direction))
            {
                return Directions.Abbreviation(direction);
            }
        }

        return null;
    }

    /// <summary>
    /// Where a line aimed in some direction leaves a box, which is the
    /// point on the box's own border rather than its middle.
    /// </summary>
    private static (double X, double Y) Edge(MapBox box, double dx, double dy)
    {
        var (cx, cy) = box.Center;

        // How far along the aim the border is, on each axis in turn.
        // The nearer of the two is the side the line actually leaves
        // by, which is what makes a diagonal leave a corner rather
        // than run through it.
        var across = dx == 0 ? double.PositiveInfinity : (BoxWidth / 2) / Math.Abs(dx);
        var down = dy == 0 ? double.PositiveInfinity : (BoxHeight / 2) / Math.Abs(dy);
        var reach = Math.Min(across, down);

        return (cx + (dx * reach), cy + (dy * reach));
    }

    /// <summary>
    /// The bounds of every box with the margin added, or nothing at all
    /// where there are no boxes.
    /// </summary>
    private static (double Left, double Top, double Width, double Height) Around(List<MapBox> boxes)
    {
        if (boxes.Count == 0)
        {
            return (0, 0, 0, 0);
        }

        var left = boxes.Min(b => b.Left);
        var top = boxes.Min(b => b.Top);
        var right = boxes.Max(b => b.Left) + BoxWidth;
        var bottom = boxes.Max(b => b.Top) + BoxHeight;

        return (
            left - Margin,
            top - Margin,
            right - left + (Margin * 2),
            bottom - top + (Margin * 2));
    }
}

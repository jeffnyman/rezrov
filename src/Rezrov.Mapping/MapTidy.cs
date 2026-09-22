namespace Rezrov.Mapping;

/// <summary>
/// Puts the rooms back where their passages say they belong.
/// </summary>
/// <remarks>
/// Rooms are placed one at a time as they are found, and are never moved
/// afterwards, so that a map being watched does not rearrange itself
/// under the player. The price is that the map drifts: making space for
/// a new room shoves a whole half of the map one cell over, and every
/// passage that straddled the line is a cell longer for it. Do that a
/// few hundred times and rooms that are next door to each other are
/// three cells and a column apart, and the drawing has to name them
/// underneath instead of joining them.
///
/// This undoes that, when someone asks for it. Every passage wants its
/// two rooms one cell apart in the direction it was walked, and the
/// number of passages that get their wish is a score the whole map can
/// be judged by. A room is offered every cell its own passages point at,
/// and takes the one that scores best; where that cell is taken, the two
/// rooms swap, which is often exactly what a drifted map needs. Nothing
/// is accepted unless the score really rises, so the pass cannot wander
/// and cannot fail to finish.
///
/// What it does not do is take a view about which map is prettier. It
/// counts passages, and that is all.
/// </remarks>
public static class MapTidy
{
    /// <summary>
    /// How many times to sweep the rooms before giving up. A sweep that
    /// improves nothing stops early, so this only bounds the pathological
    /// case.
    /// </summary>
    private const int Sweeps = 24;

    /// <summary>One passage, and the offset it wants.</summary>
    private readonly record struct Wish(Room From, Room To, int X, int Y);

    /// <summary>
    /// Re-seats the rooms and returns how many moves it took.
    /// </summary>
    public static int Tidy(RoomGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var wishes = Wishes(graph);
        if (wishes.Count == 0)
        {
            return 0;
        }

        var best = Granted(wishes);
        var moves = 0;

        for (var sweep = 0; sweep < Sweeps; sweep++)
        {
            var improved = false;

            foreach (var room in graph.Rooms)
            {
                if (room.Position is not { } here)
                {
                    continue;
                }

                foreach (var candidate in Candidates(wishes, room, here))
                {
                    var other = graph.Rooms.FirstOrDefault(
                        seated => seated.Position == candidate);

                    room.Position = candidate;
                    if (other is not null)
                    {
                        other.Position = here;
                    }

                    var score = Granted(wishes);
                    if (score > best)
                    {
                        best = score;
                        improved = true;
                        moves++;
                        break;
                    }

                    room.Position = here;
                    if (other is not null)
                    {
                        other.Position = candidate;
                    }
                }
            }

            if (!improved)
            {
                break;
            }
        }

        return moves;
    }

    /// <summary>
    /// Every passage that wants its rooms in a particular arrangement: a
    /// compass point or a staircase, walked between two rooms that both
    /// have somewhere to sit.
    /// </summary>
    /// <remarks>
    /// A passage in or out wants nothing, since a flat grid has no cell
    /// for it, and a passage that loops back into its own room is already
    /// where it wants to be. Neither is counted, so neither can drag a
    /// room anywhere.
    /// </remarks>
    private static List<Wish> Wishes(RoomGraph graph)
    {
        var wishes = new List<Wish>();

        foreach (var room in graph.Rooms)
        {
            if (room.Position is null)
            {
                continue;
            }

            foreach (var direction in Directions.All)
            {
                if (!room.Exits.TryGetValue(direction, out var id)
                    || Directions.Cell(direction) is not { } step)
                {
                    continue;
                }

                var destination = graph.Rooms[id];
                if (destination.Position is null || ReferenceEquals(destination, room))
                {
                    continue;
                }

                wishes.Add(new Wish(room, destination, step.X, step.Y));
            }
        }

        return wishes;
    }

    /// <summary>
    /// How many passages have their two rooms where they want them.
    /// </summary>
    private static int Granted(List<Wish> wishes)
    {
        var granted = 0;

        foreach (var wish in wishes)
        {
            var from = wish.From.Position!.Value;
            var to = wish.To.Position!.Value;

            if (to.X - from.X == wish.X && to.Y - from.Y == wish.Y)
            {
                granted++;
            }
        }

        return granted;
    }

    /// <summary>
    /// The cells this room's own passages would put it in, nearest first
    /// and each offered once.
    /// </summary>
    private static List<(int X, int Y)> Candidates(
        List<Wish> wishes,
        Room room,
        (int X, int Y) here)
    {
        var candidates = new List<(int X, int Y)>();

        foreach (var wish in wishes)
        {
            (int X, int Y)? wanted = null;

            if (ReferenceEquals(wish.From, room))
            {
                var to = wish.To.Position!.Value;
                wanted = (to.X - wish.X, to.Y - wish.Y);
            }
            else if (ReferenceEquals(wish.To, room))
            {
                var from = wish.From.Position!.Value;
                wanted = (from.X + wish.X, from.Y + wish.Y);
            }

            if (wanted is { } cell && cell != here && !candidates.Contains(cell))
            {
                candidates.Add(cell);
            }
        }

        return candidates;
    }
}

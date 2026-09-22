namespace Rezrov.Mapping;

/// <summary>
/// Where a newly found room goes on the grid.
/// </summary>
/// <remarks>
/// One room is placed at a time, beside the room it was reached from, in
/// the direction it was reached by. Rooms already on the map are never
/// picked up and put down again, so the map a player has been watching
/// for an hour does not rearrange itself under them when a corridor
/// turns out to loop. That stability is worth more than a tidier
/// picture, and it is why a passage can end up spanning several cells
/// rather than joining two neighbors.
///
/// When the cell a room wants is taken, a move along a compass axis
/// shoves every room at or beyond that cell one step further out and
/// takes it anyway, which keeps the new room in the direction it was
/// actually walked. This is Trizbort's strategy, and the reason for it
/// is that the direction is the one thing the player will check. A
/// diagonal yields instead and settles for the nearest free cell, since
/// shoving along both axes at once moves far more of the map than the
/// one room is worth.
/// </remarks>
internal static class Placement
{
    /// <summary>
    /// Puts <paramref name="to"/> beside <paramref name="from"/>, in the
    /// direction it was walked.
    /// </summary>
    public static void Place(RoomGraph graph, Room from, Room to, Direction via)
    {
        // Already placed: the player has walked back into somewhere they
        // have been, which closes a loop rather than discovering a room.
        if (to.Position is not null)
        {
            return;
        }

        if (from.Position is not { } origin)
        {
            return;
        }

        if (Directions.Cell(via) is not { } step)
        {
            PlaceNear(graph, from, to);
            return;
        }

        var ideal = (X: origin.X + step.X, Y: origin.Y + step.Y);
        var occupied = Occupied(graph);

        if (!occupied.Contains(ideal))
        {
            to.Position = ideal;
            return;
        }

        // A cardinal step has exactly one zero component; a diagonal has
        // none.
        if (step.X == 0 || step.Y == 0)
        {
            ShiftBeyond(graph, ideal, step);
            to.Position = ideal;
        }
        else
        {
            to.Position = NearestFree(occupied, ideal);
        }
    }

    /// <summary>
    /// Puts <paramref name="to"/> as close to <paramref name="anchor"/>
    /// as there is room for, for a move that named no direction.
    /// </summary>
    public static void PlaceNear(RoomGraph graph, Room anchor, Room to)
    {
        if (to.Position is not null)
        {
            return;
        }

        to.Position = NearestFree(Occupied(graph), anchor.Position ?? (0, 0));
    }

    private static HashSet<(int X, int Y)> Occupied(RoomGraph graph)
    {
        var cells = new HashSet<(int X, int Y)>();

        foreach (var room in graph.Rooms)
        {
            if (room.Position is { } position)
            {
                cells.Add(position);
            }
        }

        return cells;
    }

    /// <summary>
    /// Moves every placed room at or beyond <paramref name="ideal"/>
    /// along <paramref name="step"/> one cell further out, opening the
    /// wanted cell.
    /// </summary>
    /// <remarks>
    /// The whole half of the map beyond the cell moves together, so every
    /// room out there keeps its place relative to every other. Only a
    /// passage that straddles the line stretches, and the drawing says so
    /// rather than pretending the two rooms are still neighbors.
    /// </remarks>
    private static void ShiftBeyond(RoomGraph graph, (int X, int Y) ideal, (int X, int Y) step)
    {
        foreach (var room in graph.Rooms)
        {
            if (room.Position is { } position && IsBeyond(position, ideal, step))
            {
                room.Position = (position.X + step.X, position.Y + step.Y);
            }
        }
    }

    private static bool IsBeyond((int X, int Y) position, (int X, int Y) ideal, (int X, int Y) step) =>
        step switch
        {
            (1, 0) => position.X >= ideal.X,
            (-1, 0) => position.X <= ideal.X,
            (0, 1) => position.Y >= ideal.Y,
            (0, -1) => position.Y <= ideal.Y,
            _ => false,
        };

    /// <summary>
    /// The first free cell at or around <paramref name="from"/>, looked
    /// for in rings of one cell at a time.
    /// </summary>
    /// <remarks>
    /// The order within a ring is fixed rather than chosen, so the same
    /// walk always draws the same map. It is a square ring, not a circle,
    /// so a corner is reached before an edge one cell further off; that
    /// costs nothing worth the arithmetic to avoid.
    /// </remarks>
    private static (int X, int Y) NearestFree(HashSet<(int X, int Y)> occupied, (int X, int Y) from)
    {
        for (var radius = 0; ; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
                    {
                        continue;
                    }

                    var cell = (X: from.X + dx, Y: from.Y + dy);
                    if (!occupied.Contains(cell))
                    {
                        return cell;
                    }
                }
            }
        }
    }
}

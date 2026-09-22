namespace Rezrov.Mapping;

/// <summary>
/// A way out of a room: the eight compass points, the two vertical
/// moves, and the two that go through something.
/// </summary>
/// <remarks>
/// The compass points come first, clockwise from north, so that
/// iterating the enum walks the rose in order.
/// </remarks>
public enum Direction
{
    North,
    Northeast,
    East,
    Southeast,
    South,
    Southwest,
    West,
    Northwest,
    Up,
    Down,
    In,
    Out,
}

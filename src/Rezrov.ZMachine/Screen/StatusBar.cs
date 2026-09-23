using System.Globalization;

namespace Rezrov.ZMachine.Screen;

/// <summary>
/// [zm 8.2] How the status line of a Version 1 to 3 game is laid out.
/// </summary>
/// <remarks>
/// The interpreter draws this line itself, from the two globals the
/// standard sets aside, and the standard leaves the arrangement to the
/// interpreter. So the arrangement is here, in one place, because two
/// need it: the screen model lays it out when the game asks for it, and
/// the grid lays it out again when the screen changes width. The game
/// is only asked for the line once a turn, so without the second the
/// score would sit stranded wherever the old right-hand edge used to
/// be until the player typed something.
/// </remarks>
/// <summary>
/// [zm 8.2] What a Version 1 to 3 game last said its status line
/// should show, which is everything needed to lay the line out at any
/// width.
/// </summary>
/// <param name="Name">The room the player is in.</param>
/// <param name="TimeGame">
/// [zm 8.2.3] Whether the two values are an hour and a minute rather
/// than a score and a turn count.
/// </param>
/// <param name="First">The second global: score, or hours.</param>
/// <param name="Second">The third global: turns, or minutes.</param>
public readonly record struct StatusValues(string Name, bool TimeGame, short First, short Second);

internal static class StatusBar
{
    /// <summary>
    /// The line as cells: the room at the left, the score or the time
    /// at the right, and reverse video across the whole of it.
    /// </summary>
    /// <param name="name">The room the player is in.</param>
    /// <param name="timeGame">
    /// [zm 8.2.3] Whether the two values are an hour and a minute
    /// rather than a score and a turn count.
    /// </param>
    /// <param name="first">The second global: score, or hours.</param>
    /// <param name="second">The third global: turns, or minutes.</param>
    /// <param name="width">How wide the screen is now.</param>
    /// <param name="attributes">The colors and font to draw it in.</param>
    public static Cell[] Compose(
        string name,
        bool timeGame,
        short first,
        short second,
        int width,
        TextAttributes attributes)
    {
        ArgumentNullException.ThrowIfNull(name);

        var right = timeGame ? Time(first, second) : Score(first, second, width);

        // One space at the left, two before the right-hand text, and
        // one after it.
        var room = width - right.Length - 4;
        if (name.Length > room)
        {
            // [zm 8.2.2.2] A name too long for the room it has is
            // broken at its last space and given an ellipsis.
            var cut = room > 3 ? name.LastIndexOf(' ', room - 3) : -1;
            name = (cut > 0 ? name[..cut] : name[..Math.Max(room - 3, 0)]) + "...";
        }

        var cells = new Cell[width];
        Array.Fill(cells, new Cell(' ', attributes));

        for (var i = 0; i < name.Length && i + 1 < width; i++)
        {
            cells[i + 1] = new Cell(name[i], attributes);
        }

        var start = width - right.Length - 1;
        for (var i = 0; i < right.Length && start + i >= 0 && start + i < width; i++)
        {
            cells[start + i] = new Cell(right[i], attributes);
        }

        return cells;
    }

    /// <summary>
    /// How much a room name needs before the score is written out in
    /// words rather than compactly.
    /// </summary>
    private const int LeastRoom = 16;

    /// <summary>
    /// [zm 8.2] The score and the turn count.
    /// </summary>
    /// <remarks>
    /// The standard leaves the layout to the interpreter, and its
    /// remarks on section 8 suggest a compact "80/733". Infocom's own
    /// interpreters wrote the words out, and that is the status line
    /// anyone who has played these games remembers, so the words are
    /// what this writes. The compact form is kept for the case those
    /// remarks were really answering: a screen too narrow to carry
    /// both the words and a room name worth reading.
    /// </remarks>
    private static string Score(short score, short turns, int width)
    {
        var written = string.Create(CultureInfo.InvariantCulture, $"Score: {score}     Moves: {turns}");

        return width - written.Length - 4 >= LeastRoom
            ? written
            : string.Create(CultureInfo.InvariantCulture, $"{score}/{turns}");
    }

    // [zm 8.2.3.2] Twelve-hour clock with AM or PM, so that 4am and
    // 4pm can be told apart.
    private static string Time(short hours, short minutes)
    {
        var meridian = hours >= 12 ? "PM" : "AM";
        var shown = hours % 12;
        if (shown == 0)
        {
            shown = 12;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{shown}:{minutes:D2} {meridian}");
    }
}

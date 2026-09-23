namespace Rezrov.ZMachine.Screen;

/// <summary>
/// [zm 7.2] The rules for where a line of the lower window ends.
/// </summary>
/// <remarks>
/// There are only three of them, and they are here rather than written
/// out where they are used because two places need them and those two
/// must not be allowed to differ. The screen model applies them as the
/// game prints, deciding each break as it comes. The grid applies them
/// again when the screen is resized, laying the text it has kept out at
/// the new width. If those two ever disagreed, resizing a window would
/// silently rewrite the text that was already on it.
/// </remarks>
internal static class LineFit
{
    /// <summary>
    /// Whether a word of <paramref name="word"/> characters has to
    /// start a new line rather than finish this one.
    /// </summary>
    /// <remarks>
    /// [zm 7.2] The promise is only made for words that would fit on a
    /// line of their own. One longer than the whole screen has nowhere
    /// better to go, so it starts here and breaks where it must.
    /// </remarks>
    public static bool Breaks(int column, int word, int width) =>
        column > 0 && column + word > width && word <= width;

    /// <summary>
    /// Whether the line has no room left, so the next character begins
    /// the next line.
    /// </summary>
    public static bool Full(int column, int width) => column >= width;
}

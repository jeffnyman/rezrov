namespace Rezrov.Glulx.Glk;

/// <summary>
/// [glk #stream_style_hints] The style hints one window was opened
/// with.
/// </summary>
/// <remarks>
/// The specification is firm that a hint does not reach a window that
/// is already open: hints are suggestions for the windows a game makes
/// after setting them. The note in the specification says why, which is
/// that a library then knows everything about a window's appearance the
/// moment the window is made and never has to change it afterwards. So
/// the hints in force are taken down here as the window opens, and are
/// that window's from then on however the game goes on to change them.
/// </remarks>
public sealed class GlkStyles
{
    private readonly Dictionary<(GlkStyle Style, StyleHint Hint), int> _hints;

    internal GlkStyles(Dictionary<(GlkStyle Style, StyleHint Hint), int> hints) => _hints = hints;

    /// <summary>
    /// What a window opened with no hints set has, which is what every
    /// window has until a game says otherwise.
    /// </summary>
    public static GlkStyles None { get; } = new([]);

    /// <summary>
    /// The hint set for a style, or null where none was set: [glk
    /// #stream_style_hints] having no hint is not the same as having a
    /// hint of zero.
    /// </summary>
    public int? Hint(GlkStyle style, StyleHint hint) =>
        _hints.TryGetValue((style, hint), out var value) ? value : null;
}

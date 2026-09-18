namespace Rezrov.Glulx.Glk;

/// <summary>
/// [glk #stream_style_check] What one style actually looks like in one
/// window: the display's own idea of the style with the window's hints
/// laid over it.
/// </summary>
/// <remarks>
/// A hint is a suggestion and an appearance is the answer, which is why
/// this is the display's to work out rather than the library's. The
/// library keeps the hints and hands them to the display with the
/// window; the display owns the fonts and the colors and so is the only
/// thing that can say what came of them.
///
/// [glk op:style_measure] The game can ask for any of this back, and
/// [glk op:style_distinguish] can ask whether two styles come out
/// looking different, which is the same answers compared.
/// </remarks>
/// <param name="Indentation">
/// How far the lines of the style are set in from the edge, in the
/// display's own metric, which may be negative to set them out.
/// </param>
/// <param name="ParaIndentation">
/// How much further the first line of a paragraph is set in, in the
/// same metric and also possibly negative.
/// </param>
/// <param name="Justification">How the lines sit between the edges.</param>
/// <param name="Size">
/// How large the style is drawn, in the display's own metric. The hint
/// is a relative one, so this is what the display made of it.
/// </param>
/// <param name="Weight">Heavy at 1, ordinary at 0, light at -1.</param>
/// <param name="Oblique">Whether the style leans.</param>
/// <param name="Proportional">
/// Whether the style is drawn in a proportional face rather than one
/// whose characters all have the same width.
/// </param>
/// <param name="TextColor">
/// The color of the text, with the top eight bits zero and the rest
/// red, green, and blue.
/// </param>
/// <param name="BackColor">The color behind the text.</param>
/// <param name="Reverse">
/// Whether the two colors are used the other way round, which is how a
/// game asks for reverse video without naming any colors at all.
/// </param>
public readonly record struct GlkAppearance(
    int Indentation,
    int ParaIndentation,
    Justification Justification,
    double Size,
    int Weight,
    bool Oblique,
    bool Proportional,
    uint TextColor,
    uint BackColor,
    bool Reverse)
{
    /// <summary>
    /// The colors a run is really drawn in: the two the other way round
    /// for a reversed style, and as they stand otherwise.
    /// </summary>
    public (uint Ink, uint Paper) Colors => Reverse ? (BackColor, TextColor) : (TextColor, BackColor);

    /// <summary>
    /// [glk op:style_measure] One attribute of the appearance, or null
    /// for a hint this knows nothing about, which the specification
    /// says is answered by saying the attribute could not be told.
    /// </summary>
    public uint? Measure(StyleHint hint) => hint switch
    {
        // [glk op:style_measure] Signed values are cast, which the
        // specification spells out, and the game casts them back.
        StyleHint.Indentation => (uint)Indentation,
        StyleHint.ParaIndentation => (uint)ParaIndentation,
        StyleHint.Justification => (uint)Justification,
        StyleHint.Size => (uint)Math.Round(Size),
        StyleHint.Weight => (uint)Weight,
        StyleHint.Oblique => Oblique ? 1u : 0u,
        StyleHint.Proportional => Proportional ? 1u : 0u,
        StyleHint.TextColor => TextColor,
        StyleHint.BackColor => BackColor,
        StyleHint.ReverseColor => Reverse ? 1u : 0u,
        _ => null,
    };

    /// <summary>
    /// [glk op:style_distinguish] Whether a player could tell this
    /// style from another one by looking at it.
    /// </summary>
    /// <remarks>
    /// The colors are compared as they are really drawn rather than as
    /// they were given, so two styles that name their colors the other
    /// way round and reverse one of them are the same style to look at,
    /// which is what the game is asking about.
    /// </remarks>
    public bool DiffersFrom(GlkAppearance other) =>
        Colors != other.Colors
        || Math.Abs(Size - other.Size) > 0.01
        || Weight != other.Weight
        || Oblique != other.Oblique
        || Proportional != other.Proportional
        || Indentation != other.Indentation
        || ParaIndentation != other.ParaIndentation
        || Justification != other.Justification;
}

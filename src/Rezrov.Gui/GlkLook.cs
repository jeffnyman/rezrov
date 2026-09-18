using Rezrov.Glulx.Glk;

namespace Rezrov.Gui;

/// <summary>
/// What each Glk style looks like in one window: this frontend's own
/// idea of the eleven styles, with the hints the window was opened with
/// laid over it.
/// </summary>
/// <remarks>
/// [glk #stream_style] Glk names the styles and leaves their appearance
/// to the library, and [glk #stream_style_hints] a game may suggest
/// changes to it. Both halves are settled here and nowhere else, so
/// what is drawn on the screen and what [glk op:style_measure] answers
/// are one decision made once rather than two that can drift apart.
///
/// A hint is a suggestion, and two of them are declined. [glk
/// #window_textgrid] A text grid is a grid of cells, so a proportional
/// face or a larger size in one would put the characters out of their
/// columns, and a game that asked for it would be worse off than if it
/// had not asked at all.
/// </remarks>
public sealed class GlkLook
{
    /// <summary>The color ordinary text is drawn in.</summary>
    public const uint Ink = 0x001A1A1A;

    /// <summary>The color of the page behind it.</summary>
    public const uint Paper = 0x00FAFAF7;

    /// <summary>
    /// [glk #link_creating] The color a link is drawn in, whatever
    /// style it was printed in.
    /// </summary>
    /// <remarks>
    /// The specification asks a library to show links in some
    /// distinctive way, whether or not the game has asked for link
    /// input, and says blue underlined text is most likely. It is not
    /// a style hint and a game cannot change it, which is the point:
    /// a link looks like a link wherever it appears.
    /// </remarks>
    public const uint Linked = 0x00215FA6;

    private readonly Dictionary<GlkStyle, GlkAppearance> _looks = [];
    private readonly WindowType _type;
    private readonly GlkStyles _styles;
    private readonly double _size;
    private readonly double _step;

    /// <param name="type">Which kind of window the styles are for.</param>
    /// <param name="styles">The hints the window was opened with.</param>
    /// <param name="size">The size of ordinary text, in pixels.</param>
    /// <param name="step">
    /// [glk #stream_style_hints] What one step of indentation is worth
    /// in pixels. The specification leaves the metric open and asks
    /// only that one step be clearly visible, and a character's width
    /// is the obvious answer for a page of text.
    /// </param>
    public GlkLook(WindowType type, GlkStyles styles, double size, double step)
    {
        ArgumentNullException.ThrowIfNull(styles);

        _type = type;
        _styles = styles;
        _size = size;
        _step = step;
    }

    /// <summary>
    /// What a style comes out looking like, worked out once and kept,
    /// since every piece of every line asks.
    /// </summary>
    public GlkAppearance Of(GlkStyle style)
    {
        if (_looks.TryGetValue(style, out var known))
        {
            return known;
        }

        var look = Resolve(style);
        _looks[style] = look;
        return look;
    }

    private GlkAppearance Resolve(GlkStyle style)
    {
        var grid = _type == WindowType.TextGrid;

        // [glk #stream_style] The defaults are the ones the
        // specification says a game should be able to count on without
        // asking: emphasis leans, the headings and the warning are
        // heavy, and preformatted is fixed, which is what makes it
        // preformatted.
        var weight = _styles.Hint(style, StyleHint.Weight) is { } heavy
            ? Math.Clamp(heavy, -1, 1)
            : style is GlkStyle.Header or GlkStyle.Subheader or GlkStyle.Alert ? 1 : 0;

        var oblique = _styles.Hint(style, StyleHint.Oblique) is { } leaning
            ? leaning != 0
            : style is GlkStyle.Emphasized or GlkStyle.Note or GlkStyle.BlockQuote;

        var proportional = !grid
            && (_styles.Hint(style, StyleHint.Proportional) is { } spaced
                ? spaced != 0
                : style != GlkStyle.Preformatted);

        var text = _styles.Hint(style, StyleHint.TextColor) is { } ink ? Color(ink) : Ink;
        var back = _styles.Hint(style, StyleHint.BackColor) is { } paper ? Color(paper) : Paper;
        var reverse = _styles.Hint(style, StyleHint.ReverseColor) is { } flipped && flipped != 0;

        // [glk #stream_style_hints] Setting a line against both edges
        // means stretching the spaces of a proportional line, which the
        // layout here does not do, so that one is declined and comes
        // back as left flush. A hint outside the four the
        // specification names means nothing and is declined too.
        var justification = _styles.Hint(style, StyleHint.Justification) switch
        {
            (int)Justification.Centered => Justification.Centered,
            (int)Justification.RightFlush => Justification.RightFlush,
            _ => Justification.LeftFlush,
        };

        return new GlkAppearance(
            Indentation: Steps(style, StyleHint.Indentation),
            ParaIndentation: Steps(style, StyleHint.ParaIndentation),
            Justification: justification,
            Size: grid ? _size : Sized(style),
            Weight: weight,
            Oblique: oblique,
            Proportional: proportional,
            TextColor: text,
            BackColor: back,
            Reverse: reverse);
    }

    /// <summary>
    /// [glk #stream_style_hints] How large a style is drawn, in pixels.
    /// </summary>
    /// <remarks>
    /// The hint is relative, and the specification says the step need
    /// not be a constant number of points, only that one step be easily
    /// visible. A fifth again each way is that, and being a proportion
    /// it holds at whatever size ordinary text happens to be.
    /// </remarks>
    private double Sized(GlkStyle style)
    {
        var ordinary = style switch
        {
            GlkStyle.Header => _size * 1.4,
            GlkStyle.Subheader => _size * 1.15,
            _ => _size,
        };

        return _styles.Hint(style, StyleHint.Size) is { } steps
            ? Math.Clamp(ordinary * Math.Pow(1.2, steps), 6, 200)
            : ordinary;
    }

    /// <summary>
    /// An indentation hint in pixels, which may be negative, since the
    /// specification allows a style to be set out as well as in.
    /// </summary>
    private int Steps(GlkStyle style, StyleHint hint) =>
        (int)Math.Round((_styles.Hint(style, hint) ?? 0) * _step);

    /// <summary>
    /// [glk #stream_style_hints] A color hint, whose top eight bits the
    /// specification says must be zero.
    /// </summary>
    private static uint Color(int value) => (uint)value & 0x00FFFFFF;
}

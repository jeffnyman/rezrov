using Rezrov.AaMachine;

namespace Rezrov.Gui;

/// <summary>
/// [aam story] A story's style sheet worked out in pixels: what a
/// class does to the text inside it, and what shape of box it asks
/// for.
/// </summary>
/// <remarks>
/// CSS divides its properties into the ones that pass down to what is
/// inside an element and the ones that do not, and the division is not
/// a detail here. The face, the size, the weight, the slant and the
/// color of the text all pass down, so a class inside a class is the
/// two of them together. The margins, the padding, the border and the
/// background belong to one box and to nothing inside it, which is why
/// a div with silver behind it does not leave every span within it
/// silver as well.
///
/// A measurement in ems is a multiple of the size of the text in the
/// class that gives it, so the size has to be settled before anything
/// else is measured. A measurement in characters is a multiple of the
/// width of the figure nought, which is what CSS means by the unit and
/// what a game means when it asks for a status field so many
/// characters wide. A measurement as a share is a share of the room
/// the box has to fill.
/// </remarks>
public sealed class AaSheet
{
    private readonly AaStyles _styles;
    private readonly IAaGlyphs _glyphs;

    /// <param name="styles">The story's style sheet.</param>
    /// <param name="glyphs">The faces, for measuring ems and characters.</param>
    /// <param name="plain">
    /// How text is set where no class says otherwise, which is the
    /// frontend's own choice rather than the story's.
    /// </param>
    public AaSheet(AaStyles styles, IAaGlyphs glyphs, AaLook plain)
    {
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(glyphs);

        _styles = styles;
        _glyphs = glyphs;
        Plain = plain;
    }

    /// <summary>How text is set outside every class.</summary>
    public AaLook Plain { get; }

    /// <summary>
    /// The look of text one class further in. Everything the class says
    /// nothing about is whatever was already in force, which is what it
    /// means for a property to pass down.
    /// </summary>
    /// <param name="outer">The look the class was opened inside.</param>
    /// <param name="styleClass">The class being opened.</param>
    /// <param name="span">
    /// Whether it is a span rather than a div. A div has a box of its
    /// own to paint its background on, so the text inside it carries
    /// none; a span has no box, so its background travels with its
    /// text.
    /// </param>
    public AaLook Inside(AaLook outer, int styleClass, bool span)
    {
        var families = _styles.Families(styleClass);
        var size = Size(styleClass, outer.Size);

        var look = outer with
        {
            Family = families.Count > 0 ? string.Join(", ", families) : outer.Family,
            Size = size,
            Bold = _styles.Bold(styleClass) ?? outer.Bold,
            Italic = _styles.Italic(styleClass) ?? outer.Italic,

            // Letter spacing is measured against the size of the text
            // it spreads out, which is the size just settled and not
            // the one outside.
            Spacing = Pixels(_styles.Length(styleClass, "letter-spacing"), size, 0, outer.Spacing),
            Ink = Ink(_styles.Color(styleClass), outer.Ink, Plain.Ink),
        };

        return span
            ? look with { Paper = Ink(_styles.BackgroundColor(styleClass), outer.Paper, 0) }
            : look with { Paper = 0 };
    }

    /// <summary>
    /// The box a div asks for, worked out against the room it has to
    /// fill and the text it is set in.
    /// </summary>
    /// <param name="styleClass">The class the div was opened with.</param>
    /// <param name="look">
    /// How its text is set, which is what its ems and its characters
    /// are measured against.
    /// </param>
    /// <param name="room">
    /// How wide the box the div sits inside is, which is what a share
    /// is a share of.
    /// </param>
    /// <param name="inherited">
    /// Where the enclosing box sets its lines, since a class that says
    /// nothing about it goes on doing whatever was being done.
    /// </param>
    public AaBox Box(int styleClass, AaLook look, double room, AaAlignment inherited)
    {
        var border = _styles.Border(styleClass);
        var thickness = border.IsDrawn ? Pixels(border.Width, look.Size, room, Medium(look)) : 0;

        // [aam story] A margin of "auto" on both sides is CSS for
        // sharing out whatever room is left over, which is how a narrow
        // block is put in the middle of the page.
        var left = _styles.Margin(styleClass, AaSide.Left);
        var right = _styles.Margin(styleClass, AaSide.Right);

        return new AaBox(
            Margin: new AaEdges(
                Edge(styleClass, AaSide.Top, look, room, margin: true),
                Edge(styleClass, AaSide.Right, look, room, margin: true),
                Edge(styleClass, AaSide.Bottom, look, room, margin: true),
                Edge(styleClass, AaSide.Left, look, room, margin: true)),
            Padding: new AaEdges(
                Edge(styleClass, AaSide.Top, look, room, margin: false),
                Edge(styleClass, AaSide.Right, look, room, margin: false),
                Edge(styleClass, AaSide.Bottom, look, room, margin: false),
                Edge(styleClass, AaSide.Left, look, room, margin: false)),
            Border: thickness,
            BorderColor: border.Color.IsSet ? border.Color.Value : Ink(border.Color, look.Ink, look.Ink),
            Radius: Pixels(_styles.Length(styleClass, "border-radius"), look.Size, room, 0),
            Background: _styles.BackgroundColor(styleClass) is { IsSet: true } behind ? behind.Value : 0,
            Width: Room(_styles.Length(styleClass, "width"), look, room),
            Centered: left.Unit == AaUnit.Auto && right.Unit == AaUnit.Auto,
            Alignment: _styles.Property(styleClass, "text-align") is null
                ? inherited
                : _styles.Alignment(styleClass),
            Floats: _styles.FloatsTo(styleClass),
            Clears: _styles.ClearsFloats(styleClass),
            Hidden: _styles.IsHidden(styleClass),
            Height: Pixels(_styles.Length(styleClass, "height"), look.Size, room, 0));
    }

    /// <summary>Whether a class asks for its text to be shouted.</summary>
    public bool Shouts(int styleClass) => _styles.IsUppercase(styleClass);

    /// <summary>
    /// A measurement in pixels, or <paramref name="fallback"/> where
    /// the style sheet gave none or gave one in a unit that means
    /// nothing here.
    /// </summary>
    /// <param name="length">What the style sheet said.</param>
    /// <param name="size">The size of the text, for a measurement in ems.</param>
    /// <param name="room">The room to fill, for a measurement as a share.</param>
    /// <param name="fallback">What to say where it said nothing.</param>
    private double Pixels(AaLength length, double size, double room, double fallback) => length.Unit switch
    {
        AaUnit.Em => length.Amount * size,
        AaUnit.Ch => length.Amount * _glyphs.CharacterWidth(Plain with { Size = size }),
        AaUnit.Pixels => length.Amount,
        AaUnit.Percent => length.Amount / 100 * room,
        _ => fallback,
    };

    /// <summary>
    /// One side of a margin or a padding, where a side given as "auto"
    /// is no room of its own: what it asks for is the room left over,
    /// and that is settled by the layout rather than here.
    /// </summary>
    private double Edge(int styleClass, AaSide side, AaLook look, double room, bool margin)
    {
        var length = margin ? _styles.Margin(styleClass, side) : _styles.Padding(styleClass, side);

        return Pixels(length, look.Size, room, 0);
    }

    /// <summary>
    /// How wide a box is inside, or null where it takes whatever room
    /// there is, which is what a box says by saying nothing and by
    /// asking for all of it.
    /// </summary>
    private double? Room(AaLength width, AaLook look, double room) => width.IsNone
        ? null
        : Math.Max(Pixels(width, look.Size, room, room), 0);

    /// <summary>
    /// [aam story] How large the text in a class is. A size in ems is a
    /// multiple of the size outside it, which is what lets a heading
    /// inside a heading come out larger again.
    /// </summary>
    private double Size(int styleClass, double outer)
    {
        var size = _styles.Length(styleClass, "font-size");

        return size.Unit switch
        {
            AaUnit.Em => Math.Clamp(size.Amount * outer, 1, 400),
            AaUnit.Percent => Math.Clamp(size.Amount / 100 * outer, 1, 400),
            AaUnit.Pixels => Math.Clamp(size.Amount, 1, 400),
            _ => outer,
        };
    }

    /// <summary>
    /// A color, where the class gave one; the color the frontend
    /// started with, where it asked for that; and otherwise whatever
    /// color was already in force.
    /// </summary>
    private static uint Ink(AaColor color, uint inherited, uint initial) => color.Kind switch
    {
        AaColorKind.Set => color.Value,
        AaColorKind.Initial => initial,
        _ => inherited,
    };

    /// <summary>
    /// How thick a border is where the class asked for one without
    /// saying how thick. CSS calls that width "medium" and leaves the
    /// number to whoever is drawing.
    /// </summary>
    private static double Medium(AaLook look) => Math.Max(Math.Round(look.Size / 8), 1);
}

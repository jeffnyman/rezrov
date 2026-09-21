using Avalonia.Media;

namespace Rezrov.Gui;

/// <summary>
/// [aam story] The faces an Aa-machine story is set in, and the
/// measurements the layout takes from them.
/// </summary>
/// <remarks>
/// A style class names its faces the way CSS does: a list of families,
/// best first, ending in one of the generic names that every machine
/// is expected to have something for. The frontend has three families
/// to offer, one for each of the generic names that matter, so a
/// generic name in a story's list is replaced by the frontend's own
/// list for it and the rest of the names are passed through as they
/// stand. What the machine actually has among them is the toolkit's to
/// decide, which is the same arrangement as for the Glk styles.
///
/// Measurements are kept, since a line of prose is measured word by
/// word and the same words come round again and again.
/// </remarks>
internal sealed class GuiAaGlyphs : IAaGlyphs
{
    /// <summary>
    /// [aam story] The family a story's sans-serif text is set in
    /// where the player names no other. The status areas and the room
    /// headings of every game in the corpus ask for one.
    /// </summary>
    public const string SansFamily = "Verdana, Helvetica Neue, DejaVu Sans, sans-serif";

    private readonly Dictionary<string, FontFamily> _families = [];
    private readonly Fonts _fonts;
    private readonly string _prose;
    private readonly string _sans;
    private readonly string _fixed;

    /// <param name="fonts">The faces and their measurements.</param>
    /// <param name="prose">What a serif face means here.</param>
    /// <param name="sans">What a sans-serif face means here.</param>
    /// <param name="fixedWidth">What a monospace face means here.</param>
    public GuiAaGlyphs(Fonts fonts, string prose, string sans, string fixedWidth)
    {
        ArgumentNullException.ThrowIfNull(fonts);

        _fonts = fonts;
        _prose = prose;
        _sans = sans;
        _fixed = fixedWidth;
    }

    public double Width(string text, AaLook look) => _fonts.Width(text, Face(look), look.Size);

    public double Ascent(AaLook look) => _fonts.Line(Face(look), look.Size).Baseline;

    public double Descent(AaLook look)
    {
        var line = _fonts.Line(Face(look), look.Size);

        return Math.Max(line.Height - line.Baseline, 0);
    }

    public double CharacterWidth(AaLook look) => _fonts.Width("0", Face(look), look.Size);

    /// <summary>The face a look calls for.</summary>
    public Typeface Face(AaLook look) => Fonts.Face(Family(look.Family), look.Bold, look.Italic);

    /// <summary>
    /// The families to try for a story's list, with each generic name
    /// in it replaced by the frontend's own list for that name.
    /// </summary>
    private FontFamily Family(string list)
    {
        if (_families.TryGetValue(list, out var known))
        {
            return known;
        }

        var names = list
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Generic);

        var family = new FontFamily(string.Join(", ", names.Append(_prose)));

        _families[list] = family;
        return family;
    }

    /// <summary>
    /// What the frontend offers for one of the names CSS gives to the
    /// sorts of face rather than to any face in particular. A name
    /// that is not one of those is a family of its own and is passed
    /// through untouched.
    /// </summary>
    private string Generic(string name) => name.ToUpperInvariant() switch
    {
        "SERIF" => _prose,
        "SANS-SERIF" => _sans,
        "MONOSPACE" => _fixed,

        // CSS has two more of these and no frontend here has a face
        // set aside for either, so they are read as ordinary prose.
        "CURSIVE" or "FANTASY" or "SYSTEM-UI" => _prose,
        _ => name,
    };
}

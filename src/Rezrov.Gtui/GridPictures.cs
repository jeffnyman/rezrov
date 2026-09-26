using Rezrov.Core.Blorb;
using Rezrov.Core.Graphics;

namespace Rezrov.Gtui;

/// <summary>
/// The artwork a game carries, decoded once and kept.
/// </summary>
/// <remarks>
/// [infocom pictures] The four Version 6 games Infocom drew art for
/// shipped it beside the story rather than inside it, in a file per
/// rendition, and that is what this program is usually pointed at. A
/// Blorb that carries pictures works as well, since both end up as
/// the same catalog.
///
/// Decoding is not cheap and a screen is repainted whenever anything
/// on it moves, so a picture is decoded the first time it is asked
/// for and kept afterwards. The palette is part of what is asked for,
/// because [blorb 11.3] a picture that takes its colors from whatever
/// was plotted before it is a different picture each time it is.
/// </remarks>
public sealed class GridPictures
{
    private readonly BlorbPictures? _catalog;
    private readonly Dictionary<(int Number, byte[]? Palette), Pixels?> _drawn = [];

    /// <param name="catalog">
    /// The pictures, or null for a game that has none.
    /// </param>
    public GridPictures(BlorbPictures? catalog) => _catalog = catalog;

    /// <summary>How many pictures there are.</summary>
    public int Count => _catalog?.Count ?? 0;

    /// <summary>
    /// The screen the artwork was drawn for, where it says.
    /// </summary>
    public (int Width, int Height)? Screen => _catalog?.StandardWindow;

    /// <summary>
    /// Whether there is a picture of that number, which can be asked
    /// from any thread since nothing is decoded and nothing is kept.
    /// </summary>
    public bool Has(int number) => _catalog?.Contains(number) == true;

    /// <summary>
    /// The picture's pixels, or null for one that is not there or
    /// cannot be read.
    /// </summary>
    /// <remarks>
    /// Asked for only on the thread that draws, which is the one place
    /// the cache is touched.
    /// </remarks>
    public Pixels? Decode(int number, byte[]? palette = null)
    {
        if (_drawn.TryGetValue((number, palette), out var known))
        {
            return known;
        }

        Pixels? pixels;

        try
        {
            pixels = _catalog?.Decode(number, palette ?? default);
        }
        catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException)
        {
            // [blorb 2] One picture that will not decode is one
            // picture missing, not a game that cannot be played.
            pixels = null;
        }

        _drawn[(number, palette)] = pixels;

        return pixels;
    }

    /// <summary>
    /// The pictures a resource file carries, or null where it carries
    /// none that can be read.
    /// </summary>
    public static GridPictures? Of(BlorbFile? resources)
    {
        if (resources is null)
        {
            return null;
        }

        try
        {
            var catalog = BlorbPictures.From(resources);

            return catalog.Count > 0 ? new GridPictures(catalog) : null;
        }
        catch (InvalidDataException)
        {
            // [blorb 2] A malformed picture header makes the whole
            // catalog unreadable, and a game with no pictures it can
            // draw is better than no game at all.
            return null;
        }
    }
}

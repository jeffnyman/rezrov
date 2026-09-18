using Rezrov.Core.Blorb;

namespace Rezrov.Core.Graphics;

/// <summary>
/// [blorb 11.3] The colors an adaptive picture is plotted with, which
/// are the colors of the last ordinary picture plotted before it.
/// </summary>
/// <remarks>
/// Two of the Infocom Version 6 games, Arthur and Zork Zero, shade the
/// same artwork differently as the game goes on: one picture carries a
/// set of colors and the pictures that follow take them. A resource
/// file lists the second sort in its adaptive palette chunk, and the
/// pictures listed carry a placeholder palette of flat primaries which
/// is there only because the PNG standard insists on one.
///
/// The specification calls anything outside that pattern undefined,
/// including plotting an adaptive picture before any ordinary one, so
/// that case falls back to the placeholder rather than inventing
/// something.
///
/// The palettes handed out are kept rather than made afresh each time,
/// so the same picture plotted twice under the same colors comes back
/// as the same object and anything caching decoded pictures can see
/// that nothing has changed.
/// </remarks>
public sealed class AdaptivePalette
{
    /// <summary>
    /// [blorb 11.3] How many entries the current palette holds. The
    /// specification describes it as covering the color indices from 2
    /// to 15, and suggests keeping sixteen of them with the first two
    /// left insignificant, which is what this does.
    /// </summary>
    private const int Entries = 16;

    /// <summary>
    /// How many entries at the start of a picture's own palette are its
    /// own business: index 0 is the one a picture may mark transparent,
    /// and index 1 is not part of the current palette either.
    /// </summary>
    private const int Own = 2;

    private readonly IReadOnlySet<int> _adaptive;
    private readonly Dictionary<int, byte[]> _handed = [];
    private byte[]? _current;

    /// <param name="adaptive">
    /// The pictures that take their colors from another, which is what
    /// the resource file's adaptive palette chunk lists.
    /// </param>
    public AdaptivePalette(IReadOnlySet<int> adaptive)
    {
        ArgumentNullException.ThrowIfNull(adaptive);
        _adaptive = adaptive;
    }

    /// <summary>
    /// Whether any picture in the file takes its colors from another,
    /// which is false for a file with no adaptive palette chunk and for
    /// one whose chunk is empty.
    /// </summary>
    public bool Adapts => _adaptive.Count > 0;

    /// <summary>
    /// Notes that a picture is being plotted, and gives the palette to
    /// plot it with, or null to use the one the picture carries.
    /// </summary>
    public byte[]? Plot(PictureInfo picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        if (!_adaptive.Contains(picture.Number))
        {
            // [blorb 11.3] An ordinary picture carries the colors, and
            // plotting it hands them to the adaptive pictures that come
            // after.
            Remember(picture);
            return null;
        }

        return Colors(picture);
    }

    /// <summary>
    /// The colors a picture should be shown in now, or null for one
    /// that carries its own or has nothing yet to take.
    /// </summary>
    /// <remarks>
    /// Asking this of a picture already on the screen is what gives it
    /// the colors of a scene plotted after it, which is what Arthur
    /// wants of the frame it draws once and never redraws.
    /// </remarks>
    public byte[]? Colors(PictureInfo picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        if (!_adaptive.Contains(picture.Number) || _current is null)
        {
            return null;
        }

        if (_handed.TryGetValue(picture.Number, out var known))
        {
            return known;
        }

        var palette = Mixed(picture);
        _handed[picture.Number] = palette;
        return palette;
    }

    /// <summary>
    /// [blorb 11.3] Takes the colors of an ordinary picture as the
    /// current ones.
    /// </summary>
    /// <remarks>
    /// A palette of fewer than sixteen entries changes only those
    /// entries and leaves the rest of the current one as it was, which
    /// the specification is explicit about.
    /// </remarks>
    private void Remember(PictureInfo picture)
    {
        // A picture with no palette has no colors to hand on, so the
        // current ones stay as they are. The specification says this
        // cannot happen, since every picture in such a file is supposed
        // to be indexed, but Arthur and Zork Zero both carry gray and
        // true color pictures as well.
        if (picture.Kind != PictureKind.Png || PngReader.Palette(picture.Data.Span) is not { } palette)
        {
            return;
        }

        var current = _current is null ? new byte[Entries * 3] : (byte[])_current.Clone();
        palette.AsSpan(0, Math.Min(palette.Length, current.Length)).CopyTo(current);

        if (_current is not null && current.AsSpan().SequenceEqual(_current))
        {
            // The same colors as before, so nothing that was handed out
            // is out of date and there is no reason to hand it out
            // again.
            return;
        }

        _current = current;
        _handed.Clear();
    }

    /// <summary>
    /// The palette an adaptive picture is plotted with: the current
    /// colors, with the picture's own first two entries kept, since
    /// those are the ones the current palette does not cover.
    /// </summary>
    private byte[] Mixed(PictureInfo picture)
    {
        var palette = (byte[])_current!.Clone();

        if (PngReader.Palette(picture.Data.Span) is { } own)
        {
            own.AsSpan(0, Math.Min(own.Length, Own * 3)).CopyTo(palette);
        }

        return palette;
    }
}

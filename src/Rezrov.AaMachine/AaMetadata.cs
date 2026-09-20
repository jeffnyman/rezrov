namespace Rezrov.AaMachine;

/// <summary>
/// [aam story] The META chunk: what a story says about itself, for a
/// banner or a library catalogue rather than for the game.
/// </summary>
/// <remarks>
/// The strings are in the game's own character set, so reading them is
/// the first thing the machine does that needs the character set to be
/// right. An author's name is a fair test of that: get the table wrong
/// and Linus Akesson loses his ring.
/// </remarks>
public sealed class AaMetadata
{
    private AaMetadata()
    {
    }

    /// <summary>
    /// An empty set, for a story that carries no META chunk.
    /// </summary>
    public static AaMetadata None { get; } = new AaMetadata();

    public string? Title { get; private init; }

    public string? Author { get; private init; }

    /// <summary>
    /// [aam story] The story's noun, such as "An Interactive Fiction",
    /// which goes under the title in a banner.
    /// </summary>
    public string? Noun { get; private init; }

    /// <summary>
    /// The blurb, in which two line feeds part paragraphs.
    /// </summary>
    public string? Blurb { get; private init; }

    /// <summary>The release date, as YYYY-MM-DD.</summary>
    public string? ReleaseDate { get; private init; }

    /// <summary>What compiled the story, and which version of it.</summary>
    public string? Compiler { get; private init; }

    internal static AaMetadata Read(ReadOnlySpan<byte> chunk, AaCharacterSet characters)
    {
        if (chunk.IsEmpty)
        {
            return None;
        }

        string? title = null, author = null, noun = null, blurb = null, date = null, compiler = null;

        var count = chunk[0];
        var at = chunk[1..];

        for (var i = 0; i < count; i++)
        {
            if (at.IsEmpty)
            {
                throw new InvalidDataException($"The META chunk promised {count} entries and ran out at {i}.");
            }

            var identifier = at[0];
            at = at[1..];

            var end = at.IndexOf((byte)0);

            if (end < 0)
            {
                throw new InvalidDataException($"Entry {i} of the META chunk is not terminated.");
            }

            var text = characters.Text(at[..end]);
            at = at[(end + 1)..];

            switch (identifier)
            {
                case 1: title = text; break;
                case 2: author = text; break;
                case 3: noun = text; break;
                case 4: blurb = text; break;
                case 5: date = text; break;
                case 6: compiler = text; break;

                // [aam story] Anything else is from a later compiler
                // than this, and is skipped rather than refused.
                default: break;
            }
        }

        return new AaMetadata
        {
            Title = title,
            Author = author,
            Noun = noun,
            Blurb = blurb,
            ReleaseDate = date,
            Compiler = compiler,
        };
    }
}

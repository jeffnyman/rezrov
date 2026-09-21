using Rezrov.Core;
using Rezrov.Core.Graphics;

namespace Rezrov.ZMachine;

/// <summary>
/// What a frontend says a story is: the name to put on its window, and
/// the mark to put beside it.
/// </summary>
/// <remarks>
/// [babel legacy Z-code IFID] A story carries nothing a player would
/// recognize, so these are worked out from the little a file does say.
/// Infocom's games are known by name from the catalog; everything else
/// is known by the machine it runs on and by whether it came packaged
/// with its pictures and sounds.
///
/// The two go together deliberately. A window has one icon and three
/// things worth saying, so the icon takes the one a picture says best,
/// which is whose game it is, and the title takes the rest, which are
/// facts that read perfectly well as words.
/// </remarks>
public static class StoryBadges
{
    /// <summary>
    /// What to call a story's window: the game where it can be named,
    /// otherwise the file, and then what it runs on.
    /// </summary>
    /// <param name="path">Where the story was loaded from.</param>
    /// <param name="format">Which machine plays it.</param>
    /// <param name="story">
    /// The story itself, unwrapped from any resource file it arrived
    /// in.
    /// </param>
    /// <param name="packaged">
    /// Whether it arrived inside a resource file, which is a thing
    /// worth saying and the one an icon has no room left for.
    /// </param>
    public static string Title(string path, StoryFormat format, ReadOnlySpan<byte> story, bool packaged)
    {
        ArgumentNullException.ThrowIfNull(path);

        var named = format == StoryFormat.ZMachine
            ? InfocomCatalog.TitleOf(story)
            : null;

        var name = named ?? Path.GetFileName(path);
        var marks = new List<string>();

        if (format == StoryFormat.ZMachine && story.Length > 0 && story[0] is >= 1 and <= 8)
        {
            marks.Add($"Z{story[0]}");
        }

        if (packaged)
        {
            marks.Add("Blorb");
        }

        return marks.Count == 0 ? name : $"{name} ({string.Join(", ", marks)})";
    }

    /// <summary>
    /// The mark to show for a story: Infocom's where the game is one of
    /// theirs, the machine's where it is not, and the program's own for
    /// a story of some other machine entirely.
    /// </summary>
    public static string Icon(StoryFormat format, ReadOnlySpan<byte> story)
    {
        if (format != StoryFormat.ZMachine || story.Length == 0)
        {
            return StoryIcons.Rezrov;
        }

        if (InfocomCatalog.Knows(Ifid.Of(story)))
        {
            return StoryIcons.Infocom;
        }

        return story[0] is >= 1 and <= 8 ? StoryIcons.ForVersion(story[0]) : StoryIcons.Rezrov;
    }
}

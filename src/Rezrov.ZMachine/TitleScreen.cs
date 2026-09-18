namespace Rezrov.ZMachine;

/// <summary>
/// The picture an interpreter puts up of its own accord before a story
/// begins.
/// </summary>
/// <remarks>
/// Beyond Zork is the only one of Infocom's games outside Version 6 to
/// carry artwork: a single title screen of 320 by 200, shown while the
/// player waited for the game to load. The game never draws it, and
/// nothing in the standard says anything about it, so the picture is
/// the interpreter's to show or to leave. Every Frotz shows it, and
/// shows it again whenever the game restarts, which is what the
/// original interpreters did.
///
/// A story that wants one has to be recognized by its release and
/// serial number, since there is nothing in a header to ask. The
/// releases are the ones Frotz lists, with the version checked as well
/// so that another game's release one cannot be taken for this one.
/// </remarks>
public static class TitleScreen
{
    // [blorb 2] Resource numbers begin at zero, but Beyond Zork's
    // artwork is the single picture one.
    private const int First = 1;

    private static readonly (ushort Release, string Serial)[] BeyondZork =
    [
        (1, "870412"),
        (1, "870715"),
        (47, "870915"),
        (49, "870917"),
        (51, "870923"),
        (57, "871221"),
        (60, "880610"),
    ];

    /// <summary>
    /// The picture the story wants shown before it starts, or zero for
    /// a game that has none or draws its own.
    /// </summary>
    public static int Picture(StoryHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);

        if (header.Version != ZMachineVersion.V5)
        {
            return 0;
        }

        foreach (var (release, serial) in BeyondZork)
        {
            if (header.Release == release && header.SerialCode == serial)
            {
                return First;
            }
        }

        return 0;
    }
}

namespace Rezrov.Core.Blorb;

/// <summary>
/// [arc blorb] What an Arcturus resource file says about itself: that
/// its pictures are arc_image scenes, and what shape the band they
/// were painted for is.
/// </summary>
/// <remarks>
/// The declaration matters more than its contents. A Blorb without it
/// holds no arc_image pictures and must be played exactly as it always
/// was, band and all left alone, which is what keeps every other game
/// in the world unaffected by this extension.
/// </remarks>
/// <param name="Version">
/// Which version of the extension the pictures were made for, 1 so
/// far.
/// </param>
/// <param name="Mode">
/// The band the pictures were painted for: 9 for the shallower Arthur
/// band, 12 for the deeper DAAD one, or 0 where the pack declares
/// none. This is advance notice only. The mode operand on each draw is
/// the authority, so a game that declares 0 here, or changes its mind
/// along the way, still draws correctly.
/// </param>
public sealed record ArcImageDeclaration(int Version, int Mode);

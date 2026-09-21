namespace Rezrov.Gui;

/// <summary>
/// [aam output] The colors an Aa-machine story is shown in where it
/// asks for none of its own.
/// </summary>
/// <remarks>
/// A style sheet may set the color of anything, and most of the games
/// set the color of nothing, so these are what they are shown in. They
/// are the frontend's to choose rather than the story's, in the same
/// way that <see cref="GlkLook"/> chooses what a Glk style looks like.
///
/// The colors are the reference interpreter's own, so that a game that
/// was written and looked at there is looked at here in the same
/// light: a page not quite white, ink not quite black, and one warm
/// accent for the things a player can act on.
/// </remarks>
public static class AaTheme
{
    /// <summary>The color ordinary text is drawn in.</summary>
    public const uint Ink = 0xFF000000;

    /// <summary>The color of the page behind it.</summary>
    public const uint Paper = 0xFFEEEEEE;

    /// <summary>
    /// [aam output] The color of something the player can click, which
    /// is not a style a game can set: a link looks like a link
    /// wherever it appears.
    /// </summary>
    public const uint Link = 0xFFC95C03;

    /// <summary>A link with the pointer over it.</summary>
    public const uint Lit = 0xFFFC8F36;

    /// <summary>
    /// [aam opcode] The bar a game draws to show how far along
    /// something is: a filled part, a lighter stripe along the top of
    /// it, and a line around the whole.
    /// </summary>
    public const uint Bar = 0xFFC95C03;

    /// <summary>The stripe along the top of the filled part.</summary>
    public const uint BarLit = 0xFFFC8F36;

    /// <summary>The line around the whole of it.</summary>
    public const uint BarEdge = 0xFFCCCCCC;
}

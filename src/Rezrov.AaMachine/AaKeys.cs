namespace Rezrov.AaMachine;

/// <summary>
/// [aam text] The keys an Aa-machine story can be sent that are not
/// letters.
/// </summary>
/// <remarks>
/// The character set names only a handful of them, and sets them among
/// the codes below a space that are otherwise undefined for output.
/// Anything printable is itself, so there is nothing else to name: a
/// key the machine has no character for is not sent at all.
///
/// They live here rather than in any one frontend because they are the
/// machine's, and every frontend that takes keys has to agree about
/// them.
/// </remarks>
public static class AaKeys
{
    /// <summary>Backspace, which a delete key also stands for.</summary>
    public const int Backspace = 0x08;

    /// <summary>The return key.</summary>
    public const int Return = 0x0d;

    /// <summary>The four arrows, in the order the set has them.</summary>
    public const int Up = 0x10;

    /// <summary>Down.</summary>
    public const int Down = 0x11;

    /// <summary>Left.</summary>
    public const int Left = 0x12;

    /// <summary>Right.</summary>
    public const int Right = 0x13;

    /// <summary>
    /// The characters of a piece of pasted text, so that a command
    /// copied from somewhere else can be typed for the player.
    /// </summary>
    public static IEnumerable<int> Pasted(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var character in text)
        {
            if (character is '\r' or '\n')
            {
                yield return Return;
            }
            else if (character >= ' ' && character != 0x7f)
            {
                yield return character;
            }
        }
    }
}

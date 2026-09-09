using System.Text;

namespace Rezrov.ZMachine.Saves;

/// <summary>
/// Turns the name a game suggests for an auxiliary file into one the
/// interpreter can use.
/// </summary>
/// <remarks>
/// [zm 7.6.1] As of Standard 1.1 a game may pass any string, and the
/// interpreter converts it: [zm 7.6.1.4] characters illegal in a file
/// name are deleted, the name is cut at its first full stop, and an
/// empty result becomes "NULL"; [zm 7.6.1.1] the name is converted to
/// upper case. [zm 7.6.1.3] Whatever extension to add is the frontend's
/// choice, and ".AUX" is the one the older text suggested.
/// </remarks>
public static class AuxiliaryFileName
{
    /// <summary>
    /// The characters [zm 7.6.1.4] names as illegal, and a few more the
    /// operating systems in use here reject.
    /// </summary>
    private static readonly char[] Illegal = ['/', '\\', '<', '>', ':', '"', '|', '?', '*'];

    /// <summary>
    /// Sanitizes a suggested name to a bare upper case name with no
    /// extension, never empty.
    /// </summary>
    public static string Sanitize(string suggested)
    {
        ArgumentNullException.ThrowIfNull(suggested);

        var stop = suggested.IndexOf('.', StringComparison.Ordinal);
        if (stop >= 0)
        {
            suggested = suggested[..stop];
        }

        var name = new StringBuilder(suggested.Length);
        foreach (var c in suggested)
        {
            if (Array.IndexOf(Illegal, c) < 0 && !char.IsControl(c))
            {
                name.Append(char.ToUpperInvariant(c));
            }
        }

        var result = name.ToString().Trim();
        return result.Length == 0 ? "NULL" : result;
    }
}

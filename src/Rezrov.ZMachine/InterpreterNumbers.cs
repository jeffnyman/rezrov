namespace Rezrov.ZMachine;

/// <summary>
/// The names of the machines behind the interpreter numbers, for a
/// person choosing one, and how to read a choice back.
/// </summary>
/// <remarks>
/// [zm 11.1.3] Infocom's games read the interpreter number to learn
/// what machine they are on and behave accordingly: Beyond Zork picks
/// its font by it, the Version 6 games lay their screens out by it,
/// and [zm 8.3] the Amiga number brings a rule of its own. The
/// interpreter picks a sensible number itself, but a player may want
/// a game to think it is somewhere else, as Frotz allows with its -I
/// option, so each number has a short name here.
/// </remarks>
public static class InterpreterNumbers
{
    private static readonly (InterpreterNumber Number, string Name)[] Names =
    [
        (InterpreterNumber.DecSystem20, "dec20"),
        (InterpreterNumber.AppleIIe, "apple2e"),
        (InterpreterNumber.Macintosh, "macintosh"),
        (InterpreterNumber.Amiga, "amiga"),
        (InterpreterNumber.AtariST, "atarist"),
        (InterpreterNumber.IbmPc, "ibmpc"),
        (InterpreterNumber.Commodore128, "c128"),
        (InterpreterNumber.Commodore64, "c64"),
        (InterpreterNumber.AppleIIc, "apple2c"),
        (InterpreterNumber.AppleIIgs, "apple2gs"),
        (InterpreterNumber.TandyColor, "tandy"),
    ];

    /// <summary>The names, in number order, for a usage message.</summary>
    public static IReadOnlyList<string> AllNames { get; } = Names.Select(n => n.Name).ToArray();

    /// <summary>The name of a number, or its digits if it has none.</summary>
    public static string Name(InterpreterNumber number)
    {
        foreach (var (candidate, name) in Names)
        {
            if (candidate == number)
            {
                return name;
            }
        }

        return ((int)number).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads a choice: a name, in any case, or a number from 1 to 11.
    /// </summary>
    public static bool TryParse(string? text, out InterpreterNumber number)
    {
        number = InterpreterNumber.Unspecified;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var wanted = text.Trim();

        if (int.TryParse(wanted, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            if (value is < 1 or > 11)
            {
                return false;
            }

            number = (InterpreterNumber)value;
            return true;
        }

        foreach (var (candidate, name) in Names)
        {
            if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                number = candidate;
                return true;
            }
        }

        return false;
    }
}

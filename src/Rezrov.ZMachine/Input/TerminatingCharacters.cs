using Rezrov.ZMachine.Text;

namespace Rezrov.ZMachine.Input;

/// <summary>
/// The keys that end a command being read.
/// </summary>
/// <remarks>
/// [zm 10.5.2] Commands are normally terminated by a new-line.
/// [zm 10.5.2.1] In Version 5 and later a game may add to that with a
/// terminating characters table, whose byte address is in the header
/// word at $2E: a zero-terminated list of input codes, each of which
/// also finishes the command. Only function keys are permitted, and the
/// special value 255 means any function key. Beyond Zork's menus use
/// this, with cursor up and down as terminators, as the remarks on
/// section 10 mention.
/// </remarks>
public sealed class TerminatingCharacters
{
    private readonly HashSet<ushort> _codes;

    private TerminatingCharacters(HashSet<ushort> codes, bool anyFunctionKey)
    {
        _codes = codes;
        AnyFunctionKey = anyFunctionKey;
    }

    /// <summary>
    /// [zm 10.5.2] The default: only a new-line ends a command, which is
    /// all there is before Version 5.
    /// </summary>
    public static TerminatingCharacters NewlineOnly { get; } = new([], false);

    /// <summary>
    /// The function keys the table names, not counting 255.
    /// </summary>
    public IReadOnlySet<ushort> Codes => _codes;

    /// <summary>
    /// [zm 10.5.2.1] Whether the table contains 255, meaning any function
    /// key ends a command.
    /// </summary>
    public bool AnyFunctionKey { get; }

    /// <summary>
    /// Reads the story's terminating characters table, if its version
    /// has one and it names one.
    /// </summary>
    /// <remarks>
    /// [zm 10.5.2.1] Entries that are not function key codes are not
    /// permitted, and are skipped rather than honored, since a game that
    /// listed a letter cannot have meant to stop input on it. Frotz reads
    /// the table the same way.
    /// </remarks>
    public static TerminatingCharacters Read(ZMemory memory, StoryHeader header)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(header);

        if (header.Version < ZMachineVersion.V5)
        {
            return NewlineOnly;
        }

        int address = header.TerminatingCharactersTableAddress;
        if (address == 0)
        {
            return NewlineOnly;
        }

        var codes = new HashSet<ushort>();
        var anyFunctionKey = false;

        for (; address < memory.Length; address++)
        {
            ushort code = memory.ReadByte(address);
            if (code == 0)
            {
                break;
            }

            if (code == Zscii.AnyFunctionKey)
            {
                anyFunctionKey = true;
            }
            else if (Zscii.IsFunctionKey(code))
            {
                codes.Add(code);
            }
        }

        return new TerminatingCharacters(codes, anyFunctionKey);
    }

    /// <summary>
    /// Whether a key ends the command: a new-line always, and a function
    /// key if the table names it or says any will do.
    /// </summary>
    public bool IsTerminator(ushort zscii) =>
        zscii == Zscii.Newline || (Zscii.IsFunctionKey(zscii) && (AnyFunctionKey || _codes.Contains(zscii)));
}

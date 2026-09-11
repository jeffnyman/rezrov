namespace Rezrov.Glulx.Instructions;

/// <summary>
/// [glulx #function] The two kinds of function, by their type byte:
/// one takes its arguments on the stack and the other has them written
/// into its locals.
/// </summary>
public enum FunctionType : byte
{
    /// <summary>
    /// C0: after the call frame is built, the arguments are pushed with
    /// the first argument topmost and their count above them all.
    /// </summary>
    StackArguments = 0xC0,

    /// <summary>
    /// C1: the arguments are written into the locals in order, extras
    /// dropped and unfilled locals zero.
    /// </summary>
    LocalArguments = 0xC1,
}

/// <summary>
/// [glulx #callframe] One entry of a locals format: a run of locals of
/// one size. The size is 1, 2, or 4 bytes and the count 1 to 255.
/// </summary>
public readonly record struct LocalsFormatEntry(byte LocalType, byte LocalCount);

/// <summary>
/// The header of a function: its type, the format of its locals, and
/// where its code begins.
/// </summary>
/// <remarks>
/// [glulx #function] A function is a type byte, then a list of
/// LocalType and LocalCount byte pairs ended by a zero pair, then the
/// instructions, with no padding and no terminator. The locals format
/// is copied into the call frame when the function is called, which is
/// why it is read here as a list rather than skipped.
/// </remarks>
/// <param name="Address">Where the type byte is.</param>
/// <param name="Type">Which kind of function it is.</param>
/// <param name="LocalsFormat">The locals, as runs of one size each.</param>
/// <param name="CodeAddress">The first instruction.</param>
public sealed record FunctionHeader(
    uint Address,
    FunctionType Type,
    IReadOnlyList<LocalsFormatEntry> LocalsFormat,
    uint CodeAddress)
{
    /// <summary>How many locals the function has, of every size.</summary>
    public int LocalCount => LocalsFormat.Sum(entry => entry.LocalCount);

    /// <summary>
    /// Reads the function header at <paramref name="address"/>.
    /// </summary>
    /// <exception cref="GlulxException">
    /// The byte there is not a function type, or the locals format has
    /// a size other than 1, 2, or 4, or a count of zero.
    /// </exception>
    public static FunctionHeader Read(GlulxMemory memory, uint address)
    {
        ArgumentNullException.ThrowIfNull(memory);

        // [glulx #function] C0 and C1 are the function types; C2 to DF
        // are reserved for future kinds of function, and anything else
        // is some other object or no object at all.
        var type = memory.ReadByte(address);
        if (type is not (0xC0 or 0xC1))
        {
            throw new GlulxException($"Not a function: type byte {type:X2} at {address:X8}.");
        }

        var format = new List<LocalsFormatEntry>();
        var at = address + 1;

        while (true)
        {
            var localType = memory.ReadByte(at);
            var localCount = memory.ReadByte(at + 1);
            at += 2;

            if (localType == 0 && localCount == 0)
            {
                break;
            }

            // [glulx #callframe] LocalType is 1, 2, or 4 and LocalCount
            // is 1 to 255; the zero pair is the only pair with a zero
            // in it.
            if (localType is not (1 or 2 or 4) || localCount == 0)
            {
                throw new GlulxException(
                    $"Bad locals format in the function at {address:X8}: {localCount} locals of {localType} bytes.");
            }

            format.Add(new LocalsFormatEntry(localType, localCount));
        }

        return new FunctionHeader(address, (FunctionType)type, format, at);
    }
}

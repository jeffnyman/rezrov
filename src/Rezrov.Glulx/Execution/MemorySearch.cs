namespace Rezrov.Glulx.Execution;

/// <summary>
/// [glulx #searching] The flags in the Options argument of the search
/// opcodes.
/// </summary>
[Flags]
public enum SearchOptions : uint
{
    None = 0,

    /// <summary>
    /// The Key argument is the address of the key rather than the key
    /// itself, which lets a key be any length.
    /// </summary>
    KeyIndirect = 0x01,

    /// <summary>
    /// Stop with failure at a structure whose key is all zeroes, unless
    /// the key being looked for is all zeroes too.
    /// </summary>
    ZeroKeyTerminates = 0x02,

    /// <summary>
    /// Return the index of the structure found, or -1, rather than its
    /// address, or 0.
    /// </summary>
    ReturnIndex = 0x04,
}

/// <summary>
/// [glulx #searching] The three searches over fixed-size structures in
/// memory, each with a key of a fixed length at a known position.
/// </summary>
/// <remarks>
/// Keys compare as big-endian unsigned integers of their length, which
/// is a byte-by-byte comparison from the first byte. A key given by
/// value is its low 1, 2, or 4 bytes in that order, so a two-byte key
/// of 0x1234 matches the bytes 12 34.
/// </remarks>
public static class MemorySearch
{
    /// <summary>
    /// [glulx op:linearsearch] Looks at each structure in turn from
    /// <paramref name="start"/>, <paramref name="count"/> of them or
    /// without limit for -1.
    /// </summary>
    public static uint Linear(GlulxMemory memory, uint key, uint keySize, uint start, uint structSize, uint count, uint keyOffset, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var wanted = FetchKey(memory, key, keySize, options);
        var returnIndex = options.HasFlag(SearchOptions.ReturnIndex);
        var zeroTerminates = options.HasFlag(SearchOptions.ZeroKeyTerminates);

        for (uint index = 0; count == 0xFFFFFFFF || index < count; index++)
        {
            var address = start + (index * structSize);
            var found = memory.Slice(address + keyOffset, keySize);

            if (found.SequenceEqual(wanted))
            {
                return returnIndex ? index : address;
            }

            if (zeroTerminates && IsZero(found))
            {
                break;
            }
        }

        return returnIndex ? 0xFFFFFFFF : 0;
    }

    /// <summary>
    /// [glulx op:binarysearch] Halves the array of structures, which
    /// must be in ascending key order without duplicates, until the key
    /// is found or cannot be.
    /// </summary>
    public static uint Binary(GlulxMemory memory, uint key, uint keySize, uint start, uint structSize, uint count, uint keyOffset, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var wanted = FetchKey(memory, key, keySize, options);
        var returnIndex = options.HasFlag(SearchOptions.ReturnIndex);

        uint bottom = 0;
        var top = count;
        while (bottom < top)
        {
            var middle = bottom + ((top - bottom) / 2);
            var address = start + (middle * structSize);
            var comparison = memory.Slice(address + keyOffset, keySize).SequenceCompareTo(wanted);

            if (comparison == 0)
            {
                return returnIndex ? middle : address;
            }

            if (comparison < 0)
            {
                bottom = middle + 1;
            }
            else
            {
                top = middle;
            }
        }

        return returnIndex ? 0xFFFFFFFF : 0;
    }

    /// <summary>
    /// [glulx op:linkedsearch] Follows the address field at
    /// <paramref name="nextOffset"/> in each structure from
    /// <paramref name="start"/> until a zero link.
    /// </summary>
    public static uint Linked(GlulxMemory memory, uint key, uint keySize, uint start, uint keyOffset, uint nextOffset, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var wanted = FetchKey(memory, key, keySize, options);
        var zeroTerminates = options.HasFlag(SearchOptions.ZeroKeyTerminates);

        // [glulx op:linkedsearch] There is no index to return, so the
        // ReturnIndex flag has no meaning here and is ignored, as the
        // reference interpreter ignores it.
        var address = start;
        while (address != 0)
        {
            var found = memory.Slice(address + keyOffset, keySize);

            if (found.SequenceEqual(wanted))
            {
                return address;
            }

            if (zeroTerminates && IsZero(found))
            {
                break;
            }

            address = memory.ReadWord(address + nextOffset);
        }

        return 0;
    }

    private static bool IsZero(ReadOnlySpan<byte> bytes) => bytes.IndexOfAnyExcept((byte)0) < 0;

    // [glulx #searching] The key is either in memory at the given
    // address, any length, or is the value itself, whose size must then
    // be one of the machine's own.
    private static byte[] FetchKey(GlulxMemory memory, uint key, uint keySize, SearchOptions options)
    {
        if (options.HasFlag(SearchOptions.KeyIndirect))
        {
            return memory.Slice(key, keySize).ToArray();
        }

        return keySize switch
        {
            1 => [(byte)key],
            2 => [(byte)(key >> 8), (byte)key],
            4 => [(byte)(key >> 24), (byte)(key >> 16), (byte)(key >> 8), (byte)key],
            _ => throw new GlulxException($"A search key given by value must be 1, 2, or 4 bytes, not {keySize}."),
        };
    }
}

using Rezrov.Glulx.Saves;

namespace Rezrov.Glulx.Execution;

/// <summary>
/// [glulx #opcodes_malloc] The memory allocation heap: blocks handed
/// out above the end of memory as it was when the first was asked for.
/// </summary>
/// <remarks>
/// [glulx #opcodes_malloc] The heap becomes active at the first
/// allocation, when the current end of memory becomes its start and
/// memory is extended to make room, and inactive again when the last
/// block is freed, when memory shrinks back to that start. While it is
/// active the memory map is the heap's to resize. The heap's own state,
/// its start and its blocks, lives outside the memory map, so a game
/// cannot damage it by writing where it should not.
///
/// The blocks are kept as the reference interpreter keeps them: one
/// list in address order, free and allocated alike, covering the heap
/// from its start to the end of memory without gaps. An allocation
/// takes the first free block that fits, merging free neighbors on the
/// way, and extends memory by the larger of the heap's size, the
/// request, and 256 bytes when nothing fits.
/// </remarks>
public sealed class GlulxHeap
{
    // Memory cannot grow past what a byte array can hold, and an
    // allocation that would take it there fails instead.
    private const uint LargestMemory = 0x7FFF0000;

    private readonly GlulxMemory _memory;
    private readonly List<Block> _blocks = [];

    public GlulxHeap(GlulxMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        _memory = memory;
    }

    /// <summary>
    /// [glulx #opcodes_misc] The start address of the heap, which is
    /// the MAllocHeap gestalt, or zero while the heap is not active.
    /// </summary>
    public uint Start { get; private set; }

    /// <summary>Whether any block is extant.</summary>
    public bool IsActive => Start != 0;

    /// <summary>How many blocks are extant.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// [glulx op:malloc] Allocates a block of <paramref name="length"/>
    /// bytes and returns its address, or zero if memory cannot be
    /// extended to hold it.
    /// </summary>
    /// <exception cref="GlulxException">The length is zero.</exception>
    public uint Allocate(uint length)
    {
        // [glulx op:malloc] L1 must be positive.
        if (length == 0)
        {
            throw new GlulxException("A heap allocation of zero bytes.");
        }

        var index = FindFree(length);
        if (index < 0)
        {
            if (!Extend(length))
            {
                return 0;
            }

            index = FindFree(length);
            if (index < 0)
            {
                return 0;
            }
        }

        var block = _blocks[index];
        if (block.Length > length)
        {
            _blocks.Insert(index + 1, new Block(block.Address + length, block.Length - length, true));
            block.Length = length;
        }

        block.IsFree = false;
        Count++;
        return block.Address;
    }

    /// <summary>
    /// [glulx op:mfree] Frees the block at <paramref name="address"/>,
    /// and the heap itself if it was the last.
    /// </summary>
    /// <exception cref="GlulxException">
    /// The address is not that of an extant block.
    /// </exception>
    public void Free(uint address)
    {
        var block = _blocks.Find(b => b.Address == address);
        if (block is null || block.IsFree)
        {
            throw new GlulxException($"Attempt to free {address:X8}, which is not an allocated heap block.");
        }

        block.IsFree = true;
        Count--;
        if (Count == 0)
        {
            Clear();
        }
    }

    /// <summary>
    /// [glulx #opcodes_malloc] Makes the heap inactive, shrinking
    /// memory back to where the heap began; nothing if it was not
    /// active.
    /// </summary>
    public void Clear()
    {
        _blocks.Clear();
        Count = 0;
        if (Start != 0)
        {
            _memory.Resize(Start);
            Start = 0;
        }
    }

    /// <summary>
    /// [glulx #saveformat] The heap as a saved game records it: its
    /// start and the extant blocks in address order, or null while it
    /// is not active.
    /// </summary>
    public GlulxHeapSummary? Summary()
    {
        if (!IsActive)
        {
            return null;
        }

        var blocks = _blocks.Where(b => !b.IsFree).Select(b => (b.Address, b.Length)).ToArray();
        return new GlulxHeapSummary(Start, blocks);
    }

    /// <summary>
    /// [glulx #saveformat] Puts the heap back as a saved game had it,
    /// once memory has been restored to the size it had then. The
    /// blocks must be in address order, within memory, and apart.
    /// </summary>
    /// <exception cref="GlulxException">
    /// The blocks do not fit the heap, or the heap is active already.
    /// </exception>
    public void Restore(GlulxHeapSummary? summary)
    {
        if (IsActive)
        {
            throw new GlulxException("The heap is active while a saved heap is being restored.");
        }

        if (summary is null || summary.Blocks.Count == 0)
        {
            return;
        }

        if (summary.Start < _memory.Header.EndMem || summary.Start >= _memory.Length || summary.Start % GlulxHeader.Alignment != 0)
        {
            throw new GlulxException($"A saved heap starting at {summary.Start:X8} does not fit memory of {_memory.Length:X8} bytes.");
        }

        // Every block is checked before any is kept, so a bad summary
        // leaves the heap as it was: inactive.
        var end = summary.Start;
        foreach (var (address, length) in summary.Blocks)
        {
            if (address < end || length == 0 || length > _memory.Length - address)
            {
                throw new GlulxException($"A saved heap block of {length} bytes at {address:X8} is out of order or outside memory.");
            }

            end = address + length;
        }

        end = summary.Start;
        foreach (var (address, length) in summary.Blocks)
        {
            if (address > end)
            {
                _blocks.Add(new Block(end, address - end, true));
            }

            _blocks.Add(new Block(address, length, false));
            end = address + length;
        }

        if (end < _memory.Length)
        {
            _blocks.Add(new Block(end, _memory.Length - end, true));
        }

        Start = summary.Start;
        Count = summary.Blocks.Count;
    }

    // The first free block of at least the length, merging runs of
    // free blocks as it goes so that freed neighbors count together.
    private int FindFree(uint length)
    {
        var i = 0;
        while (i < _blocks.Count)
        {
            var block = _blocks[i];
            if (!block.IsFree)
            {
                i++;
                continue;
            }

            while (i + 1 < _blocks.Count && _blocks[i + 1].IsFree)
            {
                block.Length += _blocks[i + 1].Length;
                _blocks.RemoveAt(i + 1);
            }

            if (block.Length >= length)
            {
                return i;
            }

            i++;
        }

        return -1;
    }

    // [glulx #opcodes_malloc] Extends memory, activating the heap at
    // the current end if it was not active, and adds the new space to
    // the last block if that is free or as a block of its own.
    private bool Extend(uint length)
    {
        var oldEnd = _memory.Length;
        var extension = Math.Max(Math.Max(IsActive ? oldEnd - Start : 0, length), GlulxHeader.Alignment);
        var rounded = (ulong)(extension + GlulxHeader.Alignment - 1) / GlulxHeader.Alignment * GlulxHeader.Alignment;
        if (oldEnd + rounded > LargestMemory)
        {
            return false;
        }

        extension = (uint)rounded;
        _memory.Resize(oldEnd + extension);
        if (!IsActive)
        {
            Start = oldEnd;
        }

        if (_blocks.Count > 0 && _blocks[^1].IsFree)
        {
            _blocks[^1].Length += extension;
        }
        else
        {
            _blocks.Add(new Block(oldEnd, extension, true));
        }

        return true;
    }

    private sealed class Block(uint address, uint length, bool isFree)
    {
        public uint Address { get; } = address;

        public uint Length { get; set; } = length;

        public bool IsFree { get; set; } = isFree;
    }
}

using System.Buffers.Binary;
using Rezrov.Glulx.Instructions;

namespace Rezrov.Glulx.Execution;

/// <summary>
/// [glulx #callstub] The four values that let the machine jump back to
/// an earlier execution state.
/// </summary>
public readonly record struct CallStub(DestinationType DestinationType, uint DestinationAddress, uint ProgramCounter, uint FramePointer);

/// <summary>
/// The machine's stack: call frames with their locals, the values pushed
/// above them, and the call stubs between them.
/// </summary>
/// <remarks>
/// [glulx #stack] The stack is an array of bytes, separate from main
/// memory, that grows upward from zero to the size the header asks for.
/// Its format is up to the interpreter, but [glulx #saveformat] a saved
/// game writes it out whole with every value big-endian, so it is kept
/// big-endian here and a save can copy it as it is. Values are aligned
/// as the specification says: 32-bit values at multiples of four, 16-bit
/// at even positions.
///
/// [glulx #callframe] The frame of the running function begins at the
/// frame pointer with its length and the position of its locals, and
/// the values a function pushes live above the frame. Popping below
/// the frame is illegal, and the stack opcodes may only reach the
/// values above the current frame.
/// </remarks>
public sealed class GlulxStackSpace
{
    private readonly byte[] _bytes;

    public GlulxStackSpace(uint size)
    {
        _bytes = new byte[size];
    }

    /// <summary>[glulx #stack] The size the header asked for.</summary>
    public uint Size => (uint)_bytes.Length;

    /// <summary>
    /// [glulx #stack] The stack pointer, which counts in bytes and is
    /// where the next push goes.
    /// </summary>
    public uint StackPointer { get; private set; }

    /// <summary>
    /// [glulx #callframe] Where the current function's call frame begins.
    /// </summary>
    public uint FramePointer { get; private set; }

    /// <summary>
    /// [glulx #callframe] FrameLen, the distance from the frame pointer
    /// to the first pushed value.
    /// </summary>
    public uint FrameLength { get; private set; }

    /// <summary>
    /// [glulx #callframe] LocalsPos, the distance from the frame pointer
    /// to the first local.
    /// </summary>
    public uint LocalsPosition { get; private set; }

    /// <summary>
    /// The position above which pushed values live, and below which a
    /// pop may not go.
    /// </summary>
    public uint ValuesBase => FramePointer + FrameLength;

    /// <summary>
    /// [glulx op:stkcount] How many values are on the stack above the
    /// current frame.
    /// </summary>
    public uint Count => (StackPointer - ValuesBase) / 4;

    /// <summary>The bytes in use, from the bottom to the pointer.</summary>
    public ReadOnlySpan<byte> Contents => _bytes.AsSpan(0, (int)StackPointer);

    /// <summary>Reads the 32-bit value at a byte position.</summary>
    public uint ReadWord(uint position) =>
        BinaryPrimitives.ReadUInt32BigEndian(_bytes.AsSpan((int)position, 4));

    public void Push(uint value)
    {
        if (StackPointer + 4 > Size)
        {
            throw new GlulxException($"Stack overflow: the stack of {Size:X} bytes is full.");
        }

        WriteWord(StackPointer, value);
        StackPointer += 4;
    }

    public uint Pop()
    {
        // [glulx #callframe] It is illegal to pop back beyond the frame.
        if (StackPointer < ValuesBase + 4)
        {
            throw new GlulxException("Stack underflow: a pop with no value above the call frame.");
        }

        StackPointer -= 4;
        return ReadWord(StackPointer);
    }

    /// <summary>
    /// [glulx op:stkpeek] The value <paramref name="depth"/> below the
    /// top, the top itself being depth zero.
    /// </summary>
    public uint Peek(uint depth)
    {
        if (depth >= Count)
        {
            throw new GlulxException($"Stack underflow: a peek at depth {depth} with only {Count} values above the call frame.");
        }

        return ReadWord(StackPointer - (4 * (depth + 1)));
    }

    /// <summary>[glulx op:stkswap] Exchanges the top two values.</summary>
    public void Swap()
    {
        if (Count < 2)
        {
            throw new GlulxException("Stack underflow: a swap with fewer than two values above the call frame.");
        }

        var top = ReadWord(StackPointer - 4);
        var below = ReadWord(StackPointer - 8);
        WriteWord(StackPointer - 4, below);
        WriteWord(StackPointer - 8, top);
    }

    /// <summary>
    /// [glulx op:stkcopy] Pushes duplicates of the top
    /// <paramref name="count"/> values in the same order.
    /// </summary>
    public void Copy(uint count)
    {
        if (count > Count)
        {
            throw new GlulxException($"Stack underflow: a copy of {count} values with only {Count} above the call frame.");
        }

        var from = StackPointer - (4 * count);
        for (uint i = 0; i < count; i++)
        {
            Push(ReadWord(from + (4 * i)));
        }
    }

    /// <summary>
    /// [glulx op:stkroll] Rotates the top <paramref name="count"/>
    /// values by <paramref name="places"/>, upward for a positive count
    /// of places and downward for a negative one.
    /// </summary>
    /// <remarks>
    /// The specification's example: with 8 7 6 5 4 3 2 1 0 on the stack
    /// and 0 on top, rolling 5 by 1 gives 8 7 6 5 0 4 3 2 1. Each value
    /// moves one place toward the top and the top wraps to the bottom
    /// of the group, so the value at index i of the group lands at
    /// index i plus the places, modulo the count.
    /// </remarks>
    public void Roll(uint count, int places)
    {
        if ((int)count < 0)
        {
            throw new GlulxException("A negative count in stkroll.");
        }

        if (count > Count)
        {
            throw new GlulxException($"Stack underflow: a roll of {count} values with only {Count} above the call frame.");
        }

        if (count == 0 || places == 0)
        {
            return;
        }

        var shift = (int)(((places % (long)count) + count) % count);
        if (shift == 0)
        {
            return;
        }

        var bottom = StackPointer - (4 * count);
        var group = new uint[count];
        for (var i = 0; i < count; i++)
        {
            group[(i + shift) % count] = ReadWord(bottom + (uint)(4 * i));
        }

        for (var i = 0; i < count; i++)
        {
            WriteWord(bottom + (uint)(4 * i), group[i]);
        }
    }

    /// <summary>
    /// [glulx #callstub] Pushes a call stub recording where a result
    /// should go and where execution resumes, with the frame pointer
    /// last.
    /// </summary>
    public void PushCallStub(DestinationType destinationType, uint destinationAddress, uint programCounter)
    {
        Push((uint)destinationType);
        Push(destinationAddress);
        Push(programCounter);
        Push(FramePointer);
    }

    /// <summary>
    /// [glulx #callstub] Pops a call stub, which must be what is on top,
    /// and makes the frame it names the current one.
    /// </summary>
    public CallStub PopCallStub()
    {
        if (StackPointer < 16)
        {
            throw new GlulxException("Stack underflow: no call stub to pop.");
        }

        StackPointer -= 16;
        var stub = new CallStub(
            (DestinationType)ReadWord(StackPointer),
            ReadWord(StackPointer + 4),
            ReadWord(StackPointer + 8),
            ReadWord(StackPointer + 12));

        FramePointer = stub.FramePointer;
        ReadFrame();
        return stub;
    }

    /// <summary>
    /// [glulx op:throw] Cuts the stack back to a catch token, which is
    /// the pointer just above the stub the catch pushed.
    /// </summary>
    public void CutBackTo(uint token)
    {
        if (token > StackPointer || token < 16 || token % 4 != 0)
        {
            throw new GlulxException($"Invalid catch token {token:X8} with the stack pointer at {StackPointer:X8}.");
        }

        StackPointer = token;
    }

    /// <summary>
    /// [glulx #calling-and-returning] Throws away the current call frame
    /// and everything above it, leaving the stub below it on top.
    /// </summary>
    public void DiscardFrame() => StackPointer = FramePointer;

    /// <summary>
    /// [glulx #calling-and-returning] Builds the call frame for
    /// <paramref name="function"/> at the top of the stack, makes it
    /// current, and puts the arguments where its type says.
    /// </summary>
    public void PushFrame(FunctionHeader function, IReadOnlyList<uint> arguments)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(arguments);

        var frame = StackPointer;

        // [glulx #callframe] The format of locals is copied from the
        // function header, two bytes per entry, ended by a zero pair,
        // then padded with another zero pair if needed to reach a
        // four-byte boundary. The locals themselves follow, each
        // aligned to its own size, and the whole is padded to four.
        var formatLength = (uint)(2 * (function.LocalsFormat.Count + 1));
        var localsPosition = 8 + Align(formatLength, 4);

        uint localsLength = 0;
        foreach (var entry in function.LocalsFormat)
        {
            localsLength = Align(localsLength, entry.LocalType) + ((uint)entry.LocalType * entry.LocalCount);
        }

        localsLength = Align(localsLength, 4);
        var frameLength = localsPosition + localsLength;

        if (frame + frameLength > Size)
        {
            throw new GlulxException($"Stack overflow: no room for a call frame of {frameLength} bytes.");
        }

        _bytes.AsSpan((int)frame, (int)frameLength).Clear();
        WriteWord(frame, frameLength);
        WriteWord(frame + 4, localsPosition);

        var at = frame + 8;
        foreach (var entry in function.LocalsFormat)
        {
            _bytes[at++] = entry.LocalType;
            _bytes[at++] = entry.LocalCount;
        }

        FramePointer = frame;
        FrameLength = frameLength;
        LocalsPosition = localsPosition;
        StackPointer = frame + frameLength;

        if (function.Type == FunctionType.StackArguments)
        {
            // [glulx #function] Pushed last argument first, so the first
            // is topmost, and the count above them all.
            for (var i = arguments.Count - 1; i >= 0; i--)
            {
                Push(arguments[i]);
            }

            Push((uint)arguments.Count);
        }
        else
        {
            // [glulx #function] Written into the locals in order, each
            // truncated to its local's size, extras dropped, and the
            // rest left as the zeroes they already are.
            var next = 0;
            uint offset = 0;
            foreach (var entry in function.LocalsFormat)
            {
                offset = Align(offset, entry.LocalType);
                for (var i = 0; i < entry.LocalCount; i++)
                {
                    if (next < arguments.Count)
                    {
                        WriteLocal(offset, arguments[next++], entry.LocalType);
                    }

                    offset += entry.LocalType;
                }
            }
        }
    }

    /// <summary>
    /// Reads the local at <paramref name="offset"/> from the start of
    /// the locals, as a field of <paramref name="size"/> bytes.
    /// </summary>
    public uint ReadLocal(uint offset, int size)
    {
        CheckLocal(offset, size);
        var at = (int)(FramePointer + LocalsPosition + offset);
        return size switch
        {
            1 => _bytes[at],
            2 => BinaryPrimitives.ReadUInt16BigEndian(_bytes.AsSpan(at, 2)),
            _ => BinaryPrimitives.ReadUInt32BigEndian(_bytes.AsSpan(at, 4)),
        };
    }

    /// <summary>
    /// Writes the local at <paramref name="offset"/> from the start of
    /// the locals, as a field of <paramref name="size"/> bytes.
    /// </summary>
    public void WriteLocal(uint offset, uint value, int size)
    {
        CheckLocal(offset, size);
        var at = (int)(FramePointer + LocalsPosition + offset);
        switch (size)
        {
            case 1:
                _bytes[at] = (byte)value;
                break;
            case 2:
                BinaryPrimitives.WriteUInt16BigEndian(_bytes.AsSpan(at, 2), (ushort)value);
                break;
            default:
                BinaryPrimitives.WriteUInt32BigEndian(_bytes.AsSpan(at, 4), value);
                break;
        }
    }

    /// <summary>Empties the stack, for a restart.</summary>
    public void Clear()
    {
        StackPointer = 0;
        FramePointer = 0;
        FrameLength = 0;
        LocalsPosition = 0;
    }

    private static uint Align(uint value, uint alignment) => (value + alignment - 1) / alignment * alignment;

    private void WriteWord(uint position, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(_bytes.AsSpan((int)position, 4), value);

    // Takes the frame's length and locals position from the frame
    // itself, after a stub has made an older frame current again.
    private void ReadFrame()
    {
        if (StackPointer == 0)
        {
            FrameLength = 0;
            LocalsPosition = 0;
            return;
        }

        FrameLength = ReadWord(FramePointer);
        LocalsPosition = ReadWord(FramePointer + 4);
    }

    private void CheckLocal(uint offset, int size)
    {
        // [glulx #instruction] A local operand must not point outside
        // the current function's locals segment, whichever way a large
        // offset is read.
        var localsLength = FrameLength - LocalsPosition;
        if ((uint)size > localsLength || offset > localsLength - (uint)size)
        {
            throw new GlulxException($"A local at offset {offset:X} is outside the {localsLength} bytes of locals.");
        }
    }
}

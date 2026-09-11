using System.Buffers.Binary;
using Rezrov.Glulx.Instructions;

namespace Rezrov.Tests;

/// <summary>
/// Builds Glulx code by hand for machine tests: functions, labels, and
/// one instruction at a time in the encoding [glulx #instruction]
/// describes. Just enough of an assembler that a test reads like the
/// program it runs.
/// </summary>
internal sealed class GlulxAssembler
{
    /// <summary>
    /// An operand to assemble: a mode and its value, or a label to be
    /// resolved when the code is laid out, as an absolute address or as
    /// a branch offset.
    /// </summary>
    internal readonly record struct Arg(AddressingMode Mode, uint Value, string? Label = null, bool Relative = false);

    private readonly List<byte> _bytes = [];
    private readonly Dictionary<string, int> _labels = [];
    private readonly List<(int Position, string Label, bool Relative, int InstructionEnd)> _fixups = [];

    /// <param name="origin">The address the code will be placed at.</param>
    public GlulxAssembler(uint origin = 0x40)
    {
        Origin = origin;
    }

    public uint Origin { get; }

    /// <summary>The address the next byte will have.</summary>
    public uint Address => Origin + (uint)_bytes.Count;

    /// <summary>Pops a value, or pushes one as a store.</summary>
    public static Arg Sp => new(AddressingMode.Stack, 0);

    /// <summary>Throws a stored value away.</summary>
    public static Arg Discard => new(AddressingMode.Zero, 0);

    /// <summary>A constant in the smallest mode that holds it.</summary>
    public static Arg C(long value) => value switch
    {
        0 => new Arg(AddressingMode.Zero, 0),
        >= -0x80 and <= 0x7F => new Arg(AddressingMode.Constant8, (uint)value),
        >= -0x8000 and <= 0x7FFF => new Arg(AddressingMode.Constant16, (uint)value),
        _ => new Arg(AddressingMode.Constant32, (uint)value),
    };

    /// <summary>The contents of a main memory address.</summary>
    public static Arg Mem(uint address) => Sized(address, AddressingMode.Memory8, AddressingMode.Memory16, AddressingMode.Memory32);

    /// <summary>The contents of RAM at an offset from RAMSTART.</summary>
    public static Arg Ram(uint offset) => Sized(offset, AddressingMode.Ram8, AddressingMode.Ram16, AddressingMode.Ram32);

    /// <summary>A local at an offset into the frame's locals.</summary>
    public static Arg Loc(uint offset) => Sized(offset, AddressingMode.Local8, AddressingMode.Local16, AddressingMode.Local32);

    /// <summary>The absolute address of a label, as a constant.</summary>
    public static Arg At(string label) => new(AddressingMode.Constant32, 0, label);

    /// <summary>A branch offset to a label.</summary>
    public static Arg To(string label) => new(AddressingMode.Constant16, 0, label, Relative: true);

    /// <summary>Names the current address.</summary>
    public GlulxAssembler Label(string name)
    {
        _labels[name] = _bytes.Count;
        return this;
    }

    /// <summary>
    /// [glulx #function] Begins a function here: its type byte, its
    /// locals format as size and count pairs, and the zero pair.
    /// </summary>
    public GlulxAssembler Function(string? label = null, FunctionType type = FunctionType.LocalArguments, params (byte Size, byte Count)[] locals)
    {
        if (label is not null)
        {
            Label(label);
        }

        _bytes.Add((byte)type);
        foreach (var (size, count) in locals)
        {
            _bytes.Add(size);
            _bytes.Add(count);
        }

        _bytes.Add(0);
        _bytes.Add(0);
        return this;
    }

    /// <summary>
    /// [glulx #instruction] One instruction: the opcode number in its
    /// shortest form, the modes two to a byte, then the operand data.
    /// </summary>
    public GlulxAssembler Op(Opcode opcode, params Arg[] args)
    {
        var number = (uint)opcode;
        if (number < 0x80)
        {
            _bytes.Add((byte)number);
        }
        else if (number < 0x4000)
        {
            _bytes.Add((byte)(0x80 | (number >> 8)));
            _bytes.Add((byte)number);
        }
        else
        {
            _bytes.Add((byte)(0xC0 | (number >> 24)));
            _bytes.Add((byte)(number >> 16));
            _bytes.Add((byte)(number >> 8));
            _bytes.Add((byte)number);
        }

        for (var i = 0; i < args.Length; i += 2)
        {
            var pair = (int)args[i].Mode;
            if (i + 1 < args.Length)
            {
                pair |= (int)args[i + 1].Mode << 4;
            }

            _bytes.Add((byte)pair);
        }

        var positions = new int[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            positions[i] = _bytes.Count;
            Emit(args[i]);
        }

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Label is { } label)
            {
                _fixups.Add((positions[i], label, args[i].Relative, _bytes.Count));
            }
        }

        return this;
    }

    public GlulxAssembler Quit() => Op(Opcode.Quit);

    public GlulxAssembler Return(Arg value) => Op(Opcode.Return, value);

    /// <summary>The code with every label resolved.</summary>
    public byte[] ToArray()
    {
        var bytes = _bytes.ToArray();

        foreach (var (position, label, relative, instructionEnd) in _fixups)
        {
            if (!_labels.TryGetValue(label, out var target))
            {
                throw new InvalidOperationException($"No label {label}.");
            }

            if (relative)
            {
                // [glulx #opcodes_branch] The destination is the address
                // after the instruction plus the offset less two.
                var offset = target - instructionEnd + 2;
                BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(position, 2), checked((short)offset));
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(position, 4), Origin + (uint)target);
            }
        }

        return bytes;
    }

    private static Arg Sized(uint value, AddressingMode small, AddressingMode medium, AddressingMode large) => value switch
    {
        <= 0xFF => new Arg(small, value),
        <= 0xFFFF => new Arg(medium, value),
        _ => new Arg(large, value),
    };

    private void Emit(Arg arg)
    {
        switch (arg.Mode)
        {
            case AddressingMode.Zero:
            case AddressingMode.Stack:
                break;
            case AddressingMode.Constant8:
            case AddressingMode.Memory8:
            case AddressingMode.Local8:
            case AddressingMode.Ram8:
                _bytes.Add((byte)arg.Value);
                break;
            case AddressingMode.Constant16:
            case AddressingMode.Memory16:
            case AddressingMode.Local16:
            case AddressingMode.Ram16:
                _bytes.Add((byte)(arg.Value >> 8));
                _bytes.Add((byte)arg.Value);
                break;
            default:
                _bytes.Add((byte)(arg.Value >> 24));
                _bytes.Add((byte)(arg.Value >> 16));
                _bytes.Add((byte)(arg.Value >> 8));
                _bytes.Add((byte)arg.Value);
                break;
        }
    }
}

using Rezrov.Glulx.Execution;

namespace Rezrov.Glulx.Glk;

/// <summary>
/// One Glk call from the machine: the arguments as the prototype says
/// to read them, and the places its results go.
/// </summary>
/// <remarks>
/// [glulx op:glk] The arguments come off the stack as plain words. A
/// reference argument is the address of the value, or -1 for the
/// stack; a structure reference is the address of its fields, or -1;
/// an array is an address followed by a length; a string is the
/// address of a string object. Stack input references are popped after
/// the argument list, in argument order, and stack outputs are pushed
/// after the call, in argument order, before the result is stored.
///
/// A handler works in terms of logical arguments by index, asking for
/// values, objects, reference inputs, and array locations, and setting
/// reference outputs and the result. This type does the reading and
/// writing on either side.
/// </remarks>
public sealed class GlkCall
{
    private readonly GlkPrototype _prototype;
    private readonly Slot[] _slots;
    private readonly uint[] _arguments;
    private int _next;

    internal GlkCall(GlkSelector function, GlkPrototype prototype, uint[] arguments, GlulxMemory memory, GlulxStackSpace stack)
    {
        Function = function;
        _prototype = prototype;
        Memory = memory;
        Stack = stack;
        _arguments = arguments;
        _slots = new Slot[prototype.Parameters.Count];

        for (var i = 0; i < _slots.Length; i++)
        {
            _slots[i] = ReadSlot(prototype.Parameters[i]);
        }

        if (_next != arguments.Length)
        {
            throw new GlulxException($"glk_{function.Name} takes {_next} arguments as {function.Prototype}, but {arguments.Length} were given.");
        }
    }

    /// <summary>The function being called.</summary>
    public GlkSelector Function { get; }

    /// <summary>The game's memory, for arrays and memory streams.</summary>
    public GlulxMemory Memory { get; }

    /// <summary>The machine's stack, for -1 references.</summary>
    public GlulxStackSpace Stack { get; }

    /// <summary>The value to return, stored by the machine.</summary>
    public uint Result { get; set; }

    /// <summary>The value of a by-value argument.</summary>
    public uint Arg(int index) => _slots[index].Value;

    /// <summary>The identifier of an object argument, 0 for none.</summary>
    public uint ObjectId(int index) => _slots[index].Value;

    /// <summary>
    /// Whether a reference argument was null, meaning the caller does
    /// not want the value.
    /// </summary>
    public bool IsNull(int index) => _slots[index].IsNull;

    /// <summary>
    /// The value passed in through a reference, 0 for a null one.
    /// </summary>
    public uint In(int index) => _slots[index].Input.Length > 0 ? _slots[index].Input[0] : 0;

    /// <summary>Sets the value to pass out through a reference.</summary>
    public void Out(int index, uint value) => _slots[index].Output = [value];

    /// <summary>
    /// Sets the fields to pass out through a structure reference.
    /// </summary>
    public void Out(int index, params uint[] fields) => _slots[index].Output = fields;

    /// <summary>The address of an array argument.</summary>
    public uint ArrayAddress(int index) => _slots[index].Value;

    /// <summary>The length of an array argument.</summary>
    public uint ArrayLength(int index) => _slots[index].Length;

    /// <summary>The text of a string argument.</summary>
    public string Text(int index) => _slots[index].Text ?? "";

    /// <summary>The code points of a Unicode string argument.</summary>
    public uint[] UnicodeText(int index) => _slots[index].Input;

    /// <summary>
    /// After the handler has run: writes the outputs to memory or the
    /// stack, in argument order.
    /// </summary>
    internal void WriteOutputs()
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            var parameter = _prototype.Parameters[i];
            var slot = _slots[i];
            if (!parameter.PassesOut || parameter.IsArray || slot.IsNull || slot.Output is null)
            {
                continue;
            }

            for (var f = 0; f < slot.Output.Length; f++)
            {
                if (slot.IsStack)
                {
                    Stack.Push(slot.Output[f]);
                }
                else
                {
                    Memory.WriteWord(slot.Value + (uint)(4 * f), slot.Output[f]);
                }
            }
        }
    }

    private uint Take()
    {
        if (_next >= _arguments.Length)
        {
            throw new GlulxException($"glk_{Function.Name} needs more arguments than the {_arguments.Length} given, as {Function.Prototype}.");
        }

        return _arguments[_next++];
    }

    private Slot ReadSlot(GlkParameter parameter)
    {
        if (parameter.Reference == GlkReference.None)
        {
            return parameter.Type switch
            {
                GlkParameterType.Text => ReadString(Take()),
                GlkParameterType.UnicodeText => ReadUnicodeString(Take()),
                _ => new Slot { Value = Take() },
            };
        }

        var address = Take();

        if (parameter.IsArray)
        {
            // [glulx op:glk] An array is its address then its length,
            // and lives in memory whichever way it is passed.
            return new Slot { Value = address, Length = Take() };
        }

        var slot = new Slot { Value = address, IsStack = address == 0xFFFFFFFF, IsNull = address == 0 };
        var count = parameter.Fields?.Count ?? 1;

        if (slot.IsNull || !parameter.PassesIn)
        {
            return slot;
        }

        // [glulx op:glk] Input references are read now, after the whole
        // argument list has been popped, so a stack reference pops the
        // value that was below the arguments.
        slot.Input = new uint[count];
        for (var f = 0; f < count; f++)
        {
            slot.Input[f] = slot.IsStack ? Stack.Pop() : Memory.ReadWord(address + (uint)(4 * f));
        }

        return slot;
    }

    private Slot ReadString(uint address)
    {
        // [glulx op:glk] A string argument is an unencoded string
        // object: E0, the bytes, and a zero.
        if (Memory.ReadByte(address) != 0xE0)
        {
            throw new GlulxException($"The string argument to glk_{Function.Name} at {address:X8} is not an unencoded string.");
        }

        var text = new System.Text.StringBuilder();
        for (var at = address + 1; ; at++)
        {
            var character = Memory.ReadByte(at);
            if (character == 0)
            {
                break;
            }

            text.Append((char)character);
        }

        return new Slot { Value = address, Text = text.ToString() };
    }

    private Slot ReadUnicodeString(uint address)
    {
        // [glulx op:glk] E2, three padding bytes, the words, and a zero.
        if (Memory.ReadByte(address) != 0xE2)
        {
            throw new GlulxException($"The Unicode string argument to glk_{Function.Name} at {address:X8} is not an unencoded Unicode string.");
        }

        var characters = new List<uint>();
        for (var at = address + 4; ; at += 4)
        {
            var character = Memory.ReadWord(at);
            if (character == 0)
            {
                break;
            }

            characters.Add(character);
        }

        return new Slot { Value = address, Input = [.. characters] };
    }

    private sealed class Slot
    {
        public uint Value { get; init; }

        public uint Length { get; init; }

        public bool IsStack { get; init; }

        public bool IsNull { get; init; }

        public string? Text { get; init; }

        public uint[] Input { get; set; } = [];

        public uint[]? Output { get; set; }
    }
}

namespace Rezrov.Glulx.Execution;

/// <summary>
/// [glulx #opcodes_accel] The accelerated functions: the interpreter's
/// own versions of the Inform library's object and property routines,
/// which a game may ask to have run in place of its own code at given
/// addresses.
/// </summary>
/// <remarks>
/// [glulx #opcodes_accel] The game names an address and a function
/// number, and any call to that address afterwards runs the built-in
/// function instead, with the same arguments and result; branches and
/// returns to the address are not affected. The functions need values
/// compiled into the game, such as the Class object and the address of
/// the global self, which the game supplies as numbered parameters,
/// all zero to begin with. Neither the requests nor the parameters are
/// part of the saved game state.
///
/// The thirteen functions are the specification's, ported from the
/// Inform 6 source it defines them in and checked against the reference
/// interpreter: 1 is Z__Region, 2 to 7 are CP__Tab, RA__Pr, RL__Pr,
/// OC__Cl, RV__Pr, and OP__Pr as of Inform 6.31, and 8 to 13 are the
/// same as of Inform 6.33, which read the property table's position
/// from the number of attribute bytes instead of assuming seven. An
/// error found in a function is reported through the machine, which
/// prints it on the current Glk stream when that is where output goes.
/// </remarks>
public sealed class GlulxAccelerator
{
    /// <summary>
    /// [glulx #opcodes_accel] How many parameters the functions read:
    /// the classes table, the first individual property number, the
    /// four metaclasses, the address of self, the number of attribute
    /// bytes, and the common property defaults.
    /// </summary>
    public const int ParameterCount = 9;

    private const string NotAnObject = "[** Programming error: tried to find the \".\" of (something) **]";
    private const string NotAClass = "[** Programming error: tried to apply 'ofclass' with non-class **]";
    private const string NoProperty = "[** Programming error: tried to read (something) **]";

    private readonly GlulxMemory _memory;
    private readonly Action<string> _report;
    private readonly Dictionary<uint, uint> _functions = [];
    private readonly uint[] _parameters = new uint[ParameterCount];

    /// <param name="memory">
    /// The game's memory, which the functions read.
    /// </param>
    /// <param name="report">Where an error message goes.</param>
    public GlulxAccelerator(GlulxMemory memory, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(report);
        _memory = memory;
        _report = report;
    }

    /// <summary>How many addresses are accelerated.</summary>
    public int Count => _functions.Count;

    private uint ClassesTable => _parameters[0];

    private uint IndividualPropertyStart => _parameters[1];

    private uint ClassMetaclass => _parameters[2];

    private uint ObjectMetaclass => _parameters[3];

    private uint RoutineMetaclass => _parameters[4];

    private uint StringMetaclass => _parameters[5];

    private uint Self => _parameters[6];

    private uint AttributeBytes => _parameters[7];

    private uint PropertyDefaults => _parameters[8];

    /// <summary>
    /// [glulx #opcodes_misc] Whether a function number is one this
    /// interpreter accelerates, which is the AccelFunc gestalt.
    /// </summary>
    public static bool Supports(uint number) => number is >= 1 and <= 13;

    /// <summary>
    /// [glulx op:accelfunc] Asks for the function at an address to be
    /// replaced by accelerated function <paramref name="number"/>, or
    /// cancels the request for zero, or does nothing for a number not
    /// supported, which cancels any earlier request too.
    /// </summary>
    /// <exception cref="GlulxException">
    /// The address is not a function's.
    /// </exception>
    public void SetFunction(uint number, uint address)
    {
        var type = _memory.ReadByte(address);
        if (type is not (0xC0 or 0xC1))
        {
            throw new GlulxException($"Attempt to accelerate {address:X8}, which is not a function.");
        }

        if (Supports(number))
        {
            _functions[address] = number;
        }
        else
        {
            _functions.Remove(address);
        }
    }

    /// <summary>
    /// [glulx op:accelparam] Stores a parameter, or does nothing for a
    /// position the interpreter does not know.
    /// </summary>
    public void SetParameter(uint index, uint value)
    {
        if (index < ParameterCount)
        {
            _parameters[index] = value;
        }
    }

    /// <summary>
    /// The parameter at a position, zero for one unknown.
    /// </summary>
    public uint Parameter(uint index) => index < ParameterCount ? _parameters[index] : 0;

    /// <summary>
    /// Runs the accelerated function at an address, if there is one,
    /// with the arguments a call would have passed, missing ones being
    /// zero.
    /// </summary>
    public bool TryInvoke(uint address, IReadOnlyList<uint> arguments, out uint result)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (!_functions.TryGetValue(address, out var number))
        {
            result = 0;
            return false;
        }

        var first = arguments.Count > 0 ? arguments[0] : 0;
        var second = arguments.Count > 1 ? arguments[1] : 0;
        result = number switch
        {
            1 => ZRegion(first),
            2 => PropertyTableEntry(first, second, false),
            3 => PropertyAddress(first, second, false),
            4 => PropertyLength(first, second, false),
            5 => OfClass(first, second, false),
            6 => PropertyValue(first, second, false),
            7 => ProvidesProperty(first, second, false),
            8 => PropertyTableEntry(first, second, true),
            9 => PropertyAddress(first, second, true),
            10 => PropertyLength(first, second, true),
            11 => OfClass(first, second, true),
            12 => PropertyValue(first, second, true),
            _ => ProvidesProperty(first, second, true),
        };
        return true;
    }

    // [glulx #opcodes_accel] Z__Region: 1 for an object, 2 for a
    // function, 3 for a string, 0 for anything else, an object being a
    // type byte 70 to 7F in RAM.
    private uint ZRegion(uint address)
    {
        if (address < 36 || address >= _memory.Length)
        {
            return 0;
        }

        var type = _memory.ReadByte(address);
        if (type >= 0xE0)
        {
            return 3;
        }

        if (type >= 0xC0)
        {
            return 2;
        }

        return type is >= 0x70 and <= 0x7F && address >= _memory.RamStart ? 1u : 0u;
    }

    // OBJ_IN_CLASS: whether the object's parent is the Class object,
    // which is how a class is told from an instance.
    private bool IsClass(uint obj) => _memory.ReadWord(obj + 13 + AttributeBytes) == ClassMetaclass;

    // CP__Tab: the property table entry for a property, found by a
    // binary search of the object's table, whose position depends on
    // the number of attribute bytes in the newer version.
    private uint PropertyTableEntry(uint obj, uint id, bool modern)
    {
        if (ZRegion(obj) != 1)
        {
            _report(NotAnObject);
            return 0;
        }

        var table = _memory.ReadWord(obj + (modern ? 4 * (3 + (AttributeBytes / 4)) : 16));
        if (table == 0)
        {
            return 0;
        }

        var count = _memory.ReadWord(table);
        return MemorySearch.Binary(_memory, id, 2, table + 4, 10, count, 0, default);
    }

    // The entry a property lookup lands on, after the class part of a
    // property number, the rule that a class only answers for its
    // individual properties, and the private flag for anyone but self.
    private uint FindProperty(uint obj, uint id, bool modern)
    {
        uint cla = 0;
        if ((id & 0xFFFF0000) != 0)
        {
            cla = _memory.ReadWord(ClassesTable + ((id & 0xFFFF) * 4));
            if (OfClass(obj, cla, modern) == 0)
            {
                return 0;
            }

            id >>= 16;
            obj = cla;
        }

        var entry = PropertyTableEntry(obj, id, modern);
        if (entry == 0)
        {
            return 0;
        }

        if (IsClass(obj) && cla == 0 && (id < IndividualPropertyStart || id >= IndividualPropertyStart + 8))
        {
            return 0;
        }

        if (_memory.ReadWord(Self) != obj && (_memory.ReadByte(entry + 9) & 1) != 0)
        {
            return 0;
        }

        return entry;
    }

    // RA__Pr: the address of a property's data, or zero.
    private uint PropertyAddress(uint obj, uint id, bool modern)
    {
        var entry = FindProperty(obj, id, modern);
        return entry == 0 ? 0 : _memory.ReadWord(entry + 4);
    }

    // RL__Pr: the length of a property's data in bytes, or zero.
    private uint PropertyLength(uint obj, uint id, bool modern)
    {
        var entry = FindProperty(obj, id, modern);
        return entry == 0 ? 0 : 4u * _memory.ReadShort(entry + 2);
    }

    // OC__Cl: whether an object is of a class: strings and routines by
    // their metaclasses, Class and Object by whether the object is a
    // class, and any other class by the object's class list, which is
    // its property 2.
    private uint OfClass(uint obj, uint cla, bool modern)
    {
        var region = ZRegion(obj);
        if (region == 3)
        {
            return cla == StringMetaclass ? 1u : 0u;
        }

        if (region == 2)
        {
            return cla == RoutineMetaclass ? 1u : 0u;
        }

        if (region != 1)
        {
            return 0;
        }

        var classLike = IsClass(obj) || obj == ClassMetaclass || obj == StringMetaclass || obj == RoutineMetaclass || obj == ObjectMetaclass;
        if (cla == ClassMetaclass)
        {
            return classLike ? 1u : 0u;
        }

        if (cla == ObjectMetaclass)
        {
            return classLike ? 0u : 1u;
        }

        if (cla == StringMetaclass || cla == RoutineMetaclass)
        {
            return 0;
        }

        if (!IsClass(cla))
        {
            _report(NotAClass);
            return 0;
        }

        var entry = FindProperty(obj, 2, modern);
        if (entry == 0)
        {
            return 0;
        }

        var list = _memory.ReadWord(entry + 4);
        if (list == 0)
        {
            return 0;
        }

        var length = _memory.ReadShort(entry + 2);
        for (uint i = 0; i < length; i++)
        {
            if (_memory.ReadWord(list + (4 * i)) == cla)
            {
                return 1;
            }
        }

        return 0;
    }

    // RV__Pr: the value of a property, or its default for a common
    // property the object lacks.
    private uint PropertyValue(uint obj, uint id, bool modern)
    {
        var address = PropertyAddress(obj, id, modern);
        if (address != 0)
        {
            return _memory.ReadWord(address);
        }

        if (id > 0 && id < IndividualPropertyStart)
        {
            return _memory.ReadWord(PropertyDefaults + (4 * id));
        }

        _report(NoProperty);
        return 0;
    }

    // OP__Pr: whether an object provides a property: print and
    // print_to_array for strings, call for routines, the individual
    // properties for classes, and otherwise whatever RA__Pr finds.
    private uint ProvidesProperty(uint obj, uint id, bool modern)
    {
        var region = ZRegion(obj);
        if (region == 3)
        {
            return id == IndividualPropertyStart + 6 || id == IndividualPropertyStart + 7 ? 1u : 0u;
        }

        if (region == 2)
        {
            return id == IndividualPropertyStart + 5 ? 1u : 0u;
        }

        if (region != 1)
        {
            return 0;
        }

        if (id >= IndividualPropertyStart && id < IndividualPropertyStart + 8 && IsClass(obj))
        {
            return 1;
        }

        return PropertyAddress(obj, id, modern) != 0 ? 1u : 0u;
    }
}

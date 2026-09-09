using Rezrov.ZMachine.Text;

namespace Rezrov.ZMachine.Objects;

/// <summary>
/// The object table: property defaults, the object tree, and each
/// object's attributes and properties.
/// </summary>
/// <remarks>
/// [zm 12.1] The table lives in dynamic memory at the address in header
/// word $0A. Objects carry attributes, which are flags numbered from 0,
/// and properties, which are variables numbered from 1 that an object
/// need not provide.
///
/// Like the header, this is a view over memory rather than a copy. The
/// tree changes constantly during play, so every read goes to memory.
///
/// The layout differs between Versions 1 to 3 and Version 4 onward in
/// almost every dimension: how many objects, attributes, and properties
/// there can be, how wide the tree links are, and how a property's size
/// is encoded. Each difference is resolved once, in the constructor, so
/// the rest of the class reads the same way for every version.
/// </remarks>
public sealed class ObjectTable
{
    private readonly ZMemory _memory;
    private readonly ZTextDecoder _text;

    // [zm 12.3.1] and [zm 12.3.2] The two entry layouts.
    private readonly bool _early;
    private readonly int _entrySize;
    private readonly int _parentOffset;
    private readonly int _siblingOffset;
    private readonly int _childOffset;
    private readonly int _propertiesOffset;

    public ObjectTable(ZMemory memory, StoryHeader header, ZTextDecoder text)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(text);

        _memory = memory;
        _text = text;

        // [zm 12.1]
        Address = header.ObjectTableAddress;

        _early = header.Version <= ZMachineVersion.V3;

        if (_early)
        {
            // [zm 12.3.1] Up to 255 objects of 9 bytes: 32 attribute bits
            // in 4 bytes, then parent, sibling, and child as single bytes,
            // then the property table address.
            MaxObjects = 255;
            AttributeCount = 32;
            MaxPropertyNumber = 31;
            _entrySize = 9;
            _parentOffset = 4;
            _siblingOffset = 5;
            _childOffset = 6;
            _propertiesOffset = 7;
        }
        else
        {
            // [zm 12.3.2] Up to 65535 objects of 14 bytes: 48 attribute
            // bits in 6 bytes, then parent, sibling, and child as words,
            // then the property table address.
            MaxObjects = 65535;
            AttributeCount = 48;
            MaxPropertyNumber = 63;
            _entrySize = 14;
            _parentOffset = 6;
            _siblingOffset = 8;
            _childOffset = 10;
            _propertiesOffset = 12;
        }

        // [zm 12.2] The property defaults table comes first, one word per
        // possible property number, and the object entries follow it.
        FirstObjectAddress = Address + (MaxPropertyNumber * 2);

        Count = DeduceCount();
    }

    /// <summary>[zm 12.1] The byte address of the table.</summary>
    public int Address { get; }

    /// <summary>The byte address of object 1's entry.</summary>
    public int FirstObjectAddress { get; }

    /// <summary>The highest object number the layout allows.</summary>
    public int MaxObjects { get; }

    /// <summary>The number of attributes each object has.</summary>
    public int AttributeCount { get; }

    /// <summary>The highest property number the layout allows.</summary>
    public int MaxPropertyNumber { get; }

    /// <summary>
    /// The number of objects the story file appears to define.
    /// </summary>
    /// <remarks>
    /// [zm 12] The remarks note that the largest valid object number is
    /// not stored anywhere, and that tools such as infodump deduce it by
    /// assuming the object entries end where the first property table
    /// begins. This is that deduction, made once when the table is
    /// created. The interpreter itself never needs it, since opcodes name
    /// objects directly, but tools and tests do.
    /// </remarks>
    public int Count { get; }

    /// <summary>
    /// The value a property has for any object that does not provide it.
    /// </summary>
    /// <remarks>
    /// [zm 12.2] Entry n of the defaults table, which holds 31 words in
    /// Versions 1 to 3 and 63 from Version 4.
    /// </remarks>
    public ushort PropertyDefault(int property)
    {
        RequireProperty(property);

        return _memory.ReadWord(Address + ((property - 1) * 2));
    }

    /// <summary>
    /// The byte address of an object's entry in the tree.
    /// </summary>
    public int ObjectAddress(int obj)
    {
        RequireObject(obj);

        return FirstObjectAddress + ((obj - 1) * _entrySize);
    }

    /// <summary>The object's parent, or 0 for none.</summary>
    public int Parent(int obj) => ReadLink(obj, _parentOffset);

    /// <summary>The object's next sibling, or 0 for none.</summary>
    public int Sibling(int obj) => ReadLink(obj, _siblingOffset);

    /// <summary>The object's first child, or 0 for none.</summary>
    public int Child(int obj) => ReadLink(obj, _childOffset);

    public void SetParent(int obj, int parent) => WriteLink(obj, _parentOffset, parent);

    public void SetSibling(int obj, int sibling) => WriteLink(obj, _siblingOffset, sibling);

    public void SetChild(int obj, int child) => WriteLink(obj, _childOffset, child);

    /// <summary>
    /// Whether an attribute flag is set on an object.
    /// </summary>
    /// <remarks>
    /// [zm 12.3.1] Attributes are stored topmost bit first: attribute 0
    /// is bit 7 of the first byte, and the last attribute is bit 0 of the
    /// last byte. That holds for the 48 attributes of later versions too.
    /// </remarks>
    public bool HasAttribute(int obj, int attribute)
    {
        var (address, mask) = AttributeLocation(obj, attribute);

        return (_memory.ReadByte(address) & mask) != 0;
    }

    public void SetAttribute(int obj, int attribute)
    {
        var (address, mask) = AttributeLocation(obj, attribute);

        _memory.WriteByte(address, (byte)(_memory.ReadByte(address) | mask));
    }

    public void ClearAttribute(int obj, int attribute)
    {
        var (address, mask) = AttributeLocation(obj, attribute);

        _memory.WriteByte(address, (byte)(_memory.ReadByte(address) & ~mask));
    }

    /// <summary>
    /// The byte address of an object's property table.
    /// </summary>
    /// <remarks>
    /// [zm 12.4] Each table can be anywhere in dynamic memory, and a game
    /// may legally change an object's table address during play so long
    /// as the new address points at another valid table.
    /// </remarks>
    public int PropertyTableAddress(int obj) => _memory.ReadWord(ObjectAddress(obj) + _propertiesOffset);

    public void SetPropertyTableAddress(int obj, int address) =>
        _memory.WriteWord(ObjectAddress(obj) + _propertiesOffset, checked((ushort)address));

    /// <summary>
    /// The object's short name, decoded.
    /// </summary>
    /// <remarks>
    /// [zm 12.4] A property table begins with a byte giving the length of
    /// the short name in words, then the name itself as ordinary encoded
    /// text. A length of 0 means no name at all, and nothing is decoded.
    /// </remarks>
    public string ShortName(int obj)
    {
        var table = PropertyTableAddress(obj);
        var words = _memory.ReadByte(table);

        return words == 0 ? string.Empty : _text.Decode(table + 1);
    }

    /// <summary>
    /// The byte address of the first property block, which follows the
    /// short name.
    /// </summary>
    public int FirstPropertyAddress(int obj)
    {
        var table = PropertyTableAddress(obj);

        return table + 1 + (_memory.ReadByte(table) * 2);
    }

    /// <summary>
    /// The object's properties, in the order they are stored, which
    /// [zm 12.4] is descending by number.
    /// </summary>
    public IEnumerable<PropertyBlock> Properties(int obj)
    {
        var address = FirstPropertyAddress(obj);

        while (TryReadPropertyBlock(address, out var block))
        {
            yield return block;
            address = block.DataAddress + block.Length;
        }
    }

    /// <summary>
    /// Finds a property on an object, if the object provides it.
    /// </summary>
    /// <remarks>
    /// The scan stops as soon as the numbers drop below the one wanted,
    /// relying on [zm 12.4] descending order, which is also how Frotz
    /// does it. That has two consequences for real story files. A few
    /// Inform 6 games list a property number twice in a row, and the
    /// first copy is the one found. And Sherlock's object 308 has garbage
    /// after its property 43, which this never reads.
    /// </remarks>
    public bool TryFindProperty(int obj, int property, out PropertyBlock block)
    {
        RequireProperty(property);

        foreach (var candidate in Properties(obj))
        {
            if (candidate.Number == property)
            {
                block = candidate;
                return true;
            }

            // [zm 12.4] Descending order means that once the numbers have
            // dropped below the one wanted, it is not there.
            if (candidate.Number < property)
            {
                break;
            }
        }

        block = default;
        return false;
    }

    /// <summary>
    /// The length of a property given only the address of its data.
    /// </summary>
    /// <remarks>
    /// [zm 12] The remarks explain why the size bytes are laid out as they
    /// are: the machine has to be able to recover a property's length
    /// from the address of its first data byte alone, reading backward.
    /// That is why, when there are two size bytes, the second always has
    /// its top bit set: it lets this method tell a second byte from a
    /// lone one.
    /// </remarks>
    public int PropertyLength(int dataAddress)
    {
        var size = _memory.ReadByte(dataAddress - 1);

        if (_early)
        {
            // [zm 12.4.1] 32 times the length minus one, plus the number.
            return (size >> 5) + 1;
        }

        if ((size & 0x80) != 0)
        {
            // [zm 12.4.2.1] The second of two size bytes, with the length
            // in its bottom six bits and [zm 12.4.2.1.1] 0 meaning 64.
            var length = size & 0x3F;
            return length == 0 ? 64 : length;
        }

        // [zm 12.4.2.2] A lone size byte, where bit 6 chooses 1 or 2.
        return (size & 0x40) != 0 ? 2 : 1;
    }

    /// <summary>
    /// Detaches an object from its parent, leaving its own children with
    /// it.
    /// </summary>
    /// <remarks>
    /// [zm op:remove_obj] Afterward the object has no parent and, since it
    /// is no longer in any sibling chain, no sibling either.
    /// </remarks>
    public void Remove(int obj)
    {
        var parent = Parent(obj);
        if (parent == 0)
        {
            return;
        }

        // Unlink from the parent's chain of children, which starts at the
        // parent's child field and continues through sibling fields.
        if (Child(parent) == obj)
        {
            SetChild(parent, Sibling(obj));
        }
        else
        {
            var previous = Child(parent);
            while (previous != 0 && Sibling(previous) != obj)
            {
                previous = Sibling(previous);
            }

            if (previous != 0)
            {
                SetSibling(previous, Sibling(obj));
            }
        }

        SetParent(obj, 0);
        SetSibling(obj, 0);
    }

    /// <summary>
    /// Moves an object to become the first child of another.
    /// </summary>
    /// <remarks>
    /// [zm op:insert_obj] Afterward the destination's child is the object,
    /// and the object's sibling is whatever the destination's child was
    /// before. The object's own children move with it. It may start out
    /// anywhere in the tree, including with no parent at all.
    /// </remarks>
    public void Insert(int obj, int destination)
    {
        RequireObject(destination);

        Remove(obj);

        SetSibling(obj, Child(destination));
        SetChild(destination, obj);
        SetParent(obj, destination);
    }

    // [zm 12] The remark's deduction: walk the entries in order, keeping
    // the lowest property table address seen, and stop when the next
    // entry would start at or past it, since that is where the entries
    // must end and the property tables begin.
    private int DeduceCount()
    {
        var count = 0;
        var lowestPropertyTable = int.MaxValue;

        while (count < MaxObjects)
        {
            var address = FirstObjectAddress + (count * _entrySize);
            if (address + _entrySize > _memory.Length || address >= lowestPropertyTable)
            {
                break;
            }

            var propertyTable = _memory.ReadWord(address + _propertiesOffset);
            if (propertyTable < lowestPropertyTable)
            {
                lowestPropertyTable = propertyTable;
            }

            count++;
        }

        return count;
    }

    private bool TryReadPropertyBlock(int address, out PropertyBlock block)
    {
        var first = _memory.ReadByte(address);

        if (_early)
        {
            // [zm 12.4.1] A size byte of 0 ends the list.
            if (first == 0)
            {
                block = default;
                return false;
            }

            // [zm 12.4.1] The byte is 32 times the length minus one, plus
            // the number, so the number is the bottom five bits. Anything
            // else that is a multiple of 32 would have number 0, which the
            // standard says is illegal.
            var number = first & 0x1F;
            if (number == 0)
            {
                throw new InvalidDataException(
                    $"The property size byte ${first:X2} at {address:X4} is a multiple of 32, which is not allowed.");
            }

            block = new PropertyBlock(number, address + 1, (first >> 5) + 1);
            return true;
        }

        // [zm 12.4.2] The number is the bottom six bits of the first byte,
        // and a number of 0 ends the list.
        var propertyNumber = first & 0x3F;
        if (propertyNumber == 0)
        {
            block = default;
            return false;
        }

        if ((first & 0x80) != 0)
        {
            // [zm 12.4.2.1] Two size bytes. The length is the bottom six
            // bits of the second, and [zm 12.4.2.1.1] a length of 0 means
            // 64. Bit 6 of both bytes is undetermined and ignored: Infocom
            // set it in the second byte and Inform did not.
            var length = _memory.ReadByte(address + 1) & 0x3F;
            block = new PropertyBlock(propertyNumber, address + 2, length == 0 ? 64 : length);
            return true;
        }

        // [zm 12.4.2.2] One size byte, where bit 6 set means a length of
        // 2 and clear means 1.
        block = new PropertyBlock(propertyNumber, address + 1, (first & 0x40) != 0 ? 2 : 1);
        return true;
    }

    private int ReadLink(int obj, int offset)
    {
        var address = ObjectAddress(obj) + offset;

        return _early ? _memory.ReadByte(address) : _memory.ReadWord(address);
    }

    private void WriteLink(int obj, int offset, int value)
    {
        RequireObjectOrNothing(value);

        var address = ObjectAddress(obj) + offset;

        if (_early)
        {
            _memory.WriteByte(address, (byte)value);
        }
        else
        {
            _memory.WriteWord(address, (ushort)value);
        }
    }

    private (int Address, byte Mask) AttributeLocation(int obj, int attribute)
    {
        var entry = ObjectAddress(obj);

        // [zm 12] The remarks note that Sherlock tries to set and clear
        // attribute 48, one past the last. That is a bug in the game and
        // is treated as one here, at least until it turns out a lenient
        // interpreter is needed to play it.
        if (attribute < 0 || attribute >= AttributeCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attribute), attribute, $"Attributes run from 0 to {AttributeCount - 1}.");
        }

        // [zm 12.3.1] Topmost bit first.
        return (entry + (attribute / 8), (byte)(0x80 >> (attribute % 8)));
    }

    private void RequireObject(int obj)
    {
        // [zm 12.3] Object 0 means "nothing" and is not an object.
        if (obj < 1 || obj > MaxObjects)
        {
            throw new ArgumentOutOfRangeException(
                nameof(obj), obj, $"Object numbers run from 1 to {MaxObjects}, and 0 means nothing.");
        }
    }

    private void RequireObjectOrNothing(int obj)
    {
        if (obj < 0 || obj > MaxObjects)
        {
            throw new ArgumentOutOfRangeException(
                nameof(obj), obj, $"A tree link must be an object number from 1 to {MaxObjects}, or 0 for nothing.");
        }
    }

    private void RequireProperty(int property)
    {
        if (property < 1 || property > MaxPropertyNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(property), property, $"Property numbers run from 1 to {MaxPropertyNumber}.");
        }
    }
}

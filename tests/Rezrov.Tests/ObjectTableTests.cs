using Rezrov.ZMachine;
using Rezrov.ZMachine.Objects;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

public class ObjectTableTests
{
    /// <summary>
    /// A story file with an object table at $100 and room after it for
    /// property tables, filled in per test. The header's static and high
    /// memory bases sit at $40, so everything here is dynamic memory.
    /// </summary>
    private sealed class Story
    {
        public const int ObjectsBase = 0x100;

        private readonly bool _early;

        public Story(ZMachineVersion version)
        {
            _early = version <= ZMachineVersion.V3;

            Bytes[0x00] = (byte)version;
            PutWord(0x04, 0x0040);
            PutWord(0x0E, 0x0040);
            PutWord(0x0A, ObjectsBase);

            // An abbreviations table that is never used, but that has to
            // point somewhere harmless.
            PutWord(0x18, 0x0080);
        }

        public byte[] Bytes { get; } = new byte[4096];

        // [zm 12.3.1] and [zm 12.3.2]
        public int EntrySize => _early ? 9 : 14;

        // [zm 12.2] 31 or 63 words of defaults ahead of the entries.
        public int DefaultsBytes => _early ? 62 : 126;

        public int ObjectAddress(int obj) => ObjectsBase + DefaultsBytes + ((obj - 1) * EntrySize);

        /// <summary>
        /// The first byte after the entries of a table holding this many
        /// objects, which is where a compiler puts the property tables.
        /// [zm 12] The remark's deduction of the object count depends on
        /// exactly that placement, so fixtures follow it too.
        /// </summary>
        public int AfterObjects(int count) => ObjectAddress(count + 1);

        public void PutWord(int address, int value)
        {
            Bytes[address] = (byte)(value >> 8);
            Bytes[address + 1] = (byte)value;
        }

        public void Put(int address, byte[] data) => data.CopyTo(Bytes, address);

        public void SetDefault(int property, int value) =>
            PutWord(ObjectsBase + ((property - 1) * 2), value);

        public void SetObject(int obj, int parent, int sibling, int child, int properties, params int[] attributes)
        {
            var address = ObjectAddress(obj);

            // [zm 12.3.1] Attribute 0 is bit 7 of the first byte.
            foreach (var attribute in attributes)
            {
                Bytes[address + (attribute / 8)] |= (byte)(0x80 >> (attribute % 8));
            }

            if (_early)
            {
                Bytes[address + 4] = (byte)parent;
                Bytes[address + 5] = (byte)sibling;
                Bytes[address + 6] = (byte)child;
                PutWord(address + 7, properties);
            }
            else
            {
                PutWord(address + 6, parent);
                PutWord(address + 8, sibling);
                PutWord(address + 10, child);
                PutWord(address + 12, properties);
            }
        }

        /// <summary>
        /// Writes a property table: the short name, then the blocks in the
        /// order given, which the caller keeps descending, then the
        /// terminator. Sizes are encoded per version, using the two-byte
        /// form from Version 4 only when one byte cannot express the
        /// length.
        /// </summary>
        public void SetPropertyTable(int address, string name, params (int Number, byte[] Data)[] blocks)
        {
            var text = name.Length == 0 ? [] : ZChars.Text(name);

            // [zm 12.4] The length byte counts words.
            Bytes[address] = (byte)(text.Length / 2);
            Put(address + 1, text);

            var at = address + 1 + text.Length;
            foreach (var (number, data) in blocks)
            {
                if (_early)
                {
                    // [zm 12.4.1]
                    Bytes[at++] = (byte)((32 * (data.Length - 1)) + number);
                }
                else if (data.Length <= 2)
                {
                    // [zm 12.4.2.2]
                    Bytes[at++] = (byte)((data.Length == 2 ? 0x40 : 0) | number);
                }
                else
                {
                    // [zm 12.4.2.1] with [zm 12.4.2.1.1] for 64.
                    Bytes[at++] = (byte)(0x80 | number);
                    Bytes[at++] = (byte)(0x80 | (data.Length == 64 ? 0 : data.Length));
                }

                Put(at, data);
                at += data.Length;
            }

            Bytes[at] = 0;
        }

        public ObjectTable Table()
        {
            var memory = new ZMemory(Bytes);
            var header = new StoryHeader(memory);
            return new ObjectTable(memory, header, new ZTextDecoder(memory, header));
        }
    }

    // A small world used by several tests: a room holding a box and a
    // lamp, with a coin inside the box.
    //
    //   1 room
    //     2 box
    //       4 coin
    //     3 lamp
    private static Story World(ZMachineVersion version)
    {
        var story = new Story(version);
        var tables = story.AfterObjects(4);

        story.SetObject(1, parent: 0, sibling: 0, child: 2, properties: tables);
        story.SetObject(2, parent: 1, sibling: 3, child: 4, properties: tables + 0x10, 0, 9, 31);
        story.SetObject(3, parent: 1, sibling: 0, child: 0, properties: tables + 0x20);
        story.SetObject(4, parent: 2, sibling: 0, child: 0, properties: tables + 0x30);

        story.SetPropertyTable(tables, "room");
        story.SetPropertyTable(tables + 0x10, "box", (10, [0xAB, 0xCD]), (3, [0x7F]));
        story.SetPropertyTable(tables + 0x20, "brass lamp");
        story.SetPropertyTable(tables + 0x30, "");

        return story;
    }

    [Theory]
    [InlineData(ZMachineVersion.V3, 255, 32, 31, 9)]
    [InlineData(ZMachineVersion.V5, 65535, 48, 63, 14)]
    public void TheLayoutFollowsTheVersion(
        ZMachineVersion version, int maxObjects, int attributes, int maxProperty, int entrySize)
    {
        // [zm 12.3.1] and [zm 12.3.2]
        var table = World(version).Table();

        Assert.Equal(maxObjects, table.MaxObjects);
        Assert.Equal(attributes, table.AttributeCount);
        Assert.Equal(maxProperty, table.MaxPropertyNumber);
        Assert.Equal(Story.ObjectsBase, table.Address);
        Assert.Equal(Story.ObjectsBase + (maxProperty * 2), table.FirstObjectAddress);
        Assert.Equal(table.FirstObjectAddress + entrySize, table.ObjectAddress(2));
    }

    [Theory]
    [InlineData(ZMachineVersion.V3)]
    [InlineData(ZMachineVersion.V5)]
    public void ReadsPropertyDefaults(ZMachineVersion version)
    {
        // [zm 12.2] 31 words in Versions 1 to 3 and 63 from Version 4.
        var last = version <= ZMachineVersion.V3 ? 31 : 63;

        var story = World(version);
        story.SetDefault(5, 0x1234);
        story.SetDefault(last, 0x5678);

        var table = story.Table();

        // [zm 12.2] Entry n of the defaults table is the value of property
        // n for any object that does not provide it.
        Assert.Equal(0x1234, table.PropertyDefault(5));
        Assert.Equal(0x5678, table.PropertyDefault(last));
        Assert.Equal(0, table.PropertyDefault(1));
    }

    [Theory]
    [InlineData(ZMachineVersion.V3)]
    [InlineData(ZMachineVersion.V5)]
    public void ReadsTheTree(ZMachineVersion version)
    {
        var table = World(version).Table();

        Assert.Equal(0, table.Parent(1));
        Assert.Equal(2, table.Child(1));
        Assert.Equal(1, table.Parent(2));
        Assert.Equal(3, table.Sibling(2));
        Assert.Equal(4, table.Child(2));
        Assert.Equal(0, table.Sibling(3));
        Assert.Equal(2, table.Parent(4));
    }

    [Fact]
    public void TreeLinksAreWordsFromVersion4()
    {
        // [zm 12.3.2] A parent number above 255 only fits in a word.
        var story = new Story(ZMachineVersion.V5);
        story.SetObject(1, parent: 300, sibling: 0, child: 0, properties: 0x300);
        story.SetPropertyTable(0x300, "");

        Assert.Equal(300, story.Table().Parent(1));
    }

    [Theory]
    [InlineData(ZMachineVersion.V3)]
    [InlineData(ZMachineVersion.V5)]
    public void ReadsAttributesTopmostBitFirst(ZMachineVersion version)
    {
        var table = World(version).Table();

        // [zm 12.3.1] Attribute 0 is bit 7 of the first byte, 9 is bit 6
        // of the second, and 31 is bit 0 of the fourth.
        Assert.True(table.HasAttribute(2, 0));
        Assert.True(table.HasAttribute(2, 9));
        Assert.True(table.HasAttribute(2, 31));
        Assert.False(table.HasAttribute(2, 1));
        Assert.False(table.HasAttribute(2, 8));
        Assert.False(table.HasAttribute(2, 30));
        Assert.False(table.HasAttribute(1, 0));
    }

    [Fact]
    public void Version4HasFortyEightAttributes()
    {
        var story = new Story(ZMachineVersion.V5);
        story.SetObject(1, 0, 0, 0, 0x300, 47);
        story.SetPropertyTable(0x300, "");

        var table = story.Table();

        // [zm 12.3.2] 48 bits in 6 bytes, so 47 is bit 0 of the sixth.
        Assert.True(table.HasAttribute(1, 47));
        Assert.False(table.HasAttribute(1, 46));

        // [zm 12] Sherlock's famous attribute 48 does not exist.
        Assert.Throws<ArgumentOutOfRangeException>(() => table.HasAttribute(1, 48));
    }

    [Fact]
    public void Version3HasThirtyTwoAttributes()
    {
        var table = World(ZMachineVersion.V3).Table();

        Assert.Throws<ArgumentOutOfRangeException>(() => table.HasAttribute(1, 32));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.HasAttribute(1, -1));
    }

    [Theory]
    [InlineData(ZMachineVersion.V3)]
    [InlineData(ZMachineVersion.V5)]
    public void SetsAndClearsAttributesWithoutDisturbingNeighbors(ZMachineVersion version)
    {
        var table = World(version).Table();

        table.SetAttribute(2, 1);
        Assert.True(table.HasAttribute(2, 0));
        Assert.True(table.HasAttribute(2, 1));
        Assert.False(table.HasAttribute(2, 2));

        table.ClearAttribute(2, 0);
        Assert.False(table.HasAttribute(2, 0));
        Assert.True(table.HasAttribute(2, 1));
        Assert.True(table.HasAttribute(2, 9));

        // Attributes belong to one object; the neighbor is untouched.
        Assert.False(table.HasAttribute(3, 1));
    }

    [Theory]
    [InlineData(ZMachineVersion.V3)]
    [InlineData(ZMachineVersion.V5)]
    public void DecodesShortNames(ZMachineVersion version)
    {
        var table = World(version).Table();

        // [zm 12.4] The name is ordinary encoded text after a length byte.
        Assert.Equal("room", table.ShortName(1));
        Assert.Equal("brass lamp", table.ShortName(3));

        // A length of 0 is a nameless object, not a decoding error.
        Assert.Equal("", table.ShortName(4));
    }

    [Fact]
    public void ReadsVersion3PropertyBlocks()
    {
        var story = World(ZMachineVersion.V3);
        var table = story.Table();

        // [zm 12.4.1] Size byte 32(len-1)+number, so property 10 with two
        // bytes is 42 and property 3 with one byte is 3, listed in
        // descending order.
        var blocks = table.Properties(2).ToList();

        Assert.Equal(2, blocks.Count);
        Assert.Equal(10, blocks[0].Number);
        Assert.Equal(2, blocks[0].Length);
        Assert.Equal(3, blocks[1].Number);
        Assert.Equal(1, blocks[1].Length);

        // The data addresses point at the bytes that were written.
        Assert.Equal(0xAB, story.Bytes[blocks[0].DataAddress]);
        Assert.Equal(0x7F, story.Bytes[blocks[1].DataAddress]);

        // The first block follows the name: one length byte plus one word
        // of text for a three-letter name.
        Assert.Equal(story.AfterObjects(4) + 0x10 + 1 + 2, table.FirstPropertyAddress(2));
    }

    [Fact]
    public void ReadsBothVersion4SizeForms()
    {
        var story = new Story(ZMachineVersion.V5);
        story.SetObject(1, 0, 0, 0, 0x300);

        var sixtyFour = new byte[64];
        Array.Fill(sixtyFour, (byte)0x11);

        story.SetPropertyTable(
            0x300,
            "",
            (20, [1, 2, 3]),
            (10, [0xAB, 0xCD]),
            (5, [0x7F]),
            (2, sixtyFour));

        var blocks = story.Table().Properties(1).ToList();

        // [zm 12.4.2.1] Three bytes needs the two-byte form.
        Assert.Equal((20, 3), (blocks[0].Number, blocks[0].Length));

        // [zm 12.4.2.2] One and two bytes fit in a single size byte.
        Assert.Equal((10, 2), (blocks[1].Number, blocks[1].Length));
        Assert.Equal((5, 1), (blocks[2].Number, blocks[2].Length));

        // [zm 12.4.2.1.1] A stored length of 0 means 64.
        Assert.Equal((2, 64), (blocks[3].Number, blocks[3].Length));
        Assert.Equal(4, blocks.Count);
    }

    [Fact]
    public void IgnoresTheUndeterminedBitsInVersion4SizeBytes()
    {
        var story = new Story(ZMachineVersion.V5);
        story.SetObject(1, 0, 0, 0, 0x300);

        // Written by hand: property 7 of length 2 in the two-byte form,
        // with bit 6 set in both bytes the way Infocom's compiler did.
        story.Bytes[0x300] = 0;
        story.Bytes[0x301] = 0x80 | 0x40 | 7;
        story.Bytes[0x302] = 0x80 | 0x40 | 2;
        story.Bytes[0x303] = 0xAA;
        story.Bytes[0x304] = 0xBB;
        story.Bytes[0x305] = 0;

        var block = Assert.Single(story.Table().Properties(1));

        // [zm 12.4.2.1] Bit 6 is undetermined in both bytes.
        Assert.Equal((7, 0x303, 2), (block.Number, block.DataAddress, block.Length));
    }

    [Theory]
    [InlineData(ZMachineVersion.V3)]
    [InlineData(ZMachineVersion.V5)]
    public void FindsPropertiesByNumber(ZMachineVersion version)
    {
        var table = World(version).Table();

        Assert.True(table.TryFindProperty(2, 10, out var ten));
        Assert.Equal(2, ten.Length);

        Assert.True(table.TryFindProperty(2, 3, out var three));
        Assert.Equal(1, three.Length);

        // Between the two, and absent.
        Assert.False(table.TryFindProperty(2, 5, out _));

        // An object with no properties at all.
        Assert.False(table.TryFindProperty(1, 10, out _));

        // [zm 12.1] Property numbers start at 1.
        Assert.Throws<ArgumentOutOfRangeException>(() => table.TryFindProperty(2, 0, out _));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => table.TryFindProperty(2, table.MaxPropertyNumber + 1, out _));
    }

    [Fact]
    public void RecoversLengthFromTheDataAddressInVersion3()
    {
        var table = World(ZMachineVersion.V3).Table();

        // [zm 12.4.1] Reading the size byte backward from the data.
        foreach (var block in table.Properties(2))
        {
            Assert.Equal(block.Length, table.PropertyLength(block.DataAddress));
        }
    }

    [Fact]
    public void RecoversLengthFromTheDataAddressInVersion4()
    {
        var story = new Story(ZMachineVersion.V5);
        story.SetObject(1, 0, 0, 0, 0x300);

        var sixtyFour = new byte[64];
        story.SetPropertyTable(0x300, "", (20, [1, 2, 3]), (10, [1, 2]), (5, [1]), (2, sixtyFour));

        var table = story.Table();

        // [zm 12] The remark: the second size byte has its top bit set so
        // that a backward read can tell it from a lone size byte. Both
        // forms and the 64 special case have to come out right.
        foreach (var block in table.Properties(1))
        {
            Assert.Equal(block.Length, table.PropertyLength(block.DataAddress));
        }
    }

    [Fact]
    public void AVersion3SizeByteThatIsAMultipleOf32IsAnError()
    {
        var story = new Story(ZMachineVersion.V3);
        story.SetObject(1, 0, 0, 0, 0x300);
        story.Bytes[0x300] = 0;

        // [zm 12.4.1] Number 0 with a length of 2.
        story.Bytes[0x301] = 32;

        Assert.Throws<InvalidDataException>(() => story.Table().Properties(1).ToList());
    }

    [Theory]
    [InlineData(ZMachineVersion.V3)]
    [InlineData(ZMachineVersion.V5)]
    public void DeducesTheObjectCountFromTheFirstPropertyTable(ZMachineVersion version)
    {
        // [zm 12] The remark's method: the entries end where the first
        // property table begins. The World has four objects and its
        // property tables start right after the fourth entry.
        Assert.Equal(4, World(version).Table().Count);
    }

    [Fact]
    public void DeducesTheCountWhenPropertyTablesFollowImmediately()
    {
        var story = new Story(ZMachineVersion.V3);

        // Two objects whose property tables begin exactly where a third
        // entry would have started.
        var tables = story.ObjectAddress(3);
        story.SetObject(1, 0, 2, 0, tables);
        story.SetObject(2, 0, 0, 0, tables + 8);
        story.SetPropertyTable(tables, "one");
        story.SetPropertyTable(tables + 8, "two");

        Assert.Equal(2, story.Table().Count);
    }

    [Theory]
    [InlineData(ZMachineVersion.V3)]
    [InlineData(ZMachineVersion.V5)]
    public void RemoveDetachesAnObjectAndKeepsItsChildren(ZMachineVersion version)
    {
        var table = World(version).Table();

        // [zm op:remove_obj] The box leaves the room. The coin stays in
        // the box, and the lamp becomes the room's first child.
        table.Remove(2);

        Assert.Equal(0, table.Parent(2));
        Assert.Equal(0, table.Sibling(2));
        Assert.Equal(4, table.Child(2));
        Assert.Equal(2, table.Parent(4));
        Assert.Equal(3, table.Child(1));
    }

    [Theory]
    [InlineData(ZMachineVersion.V3)]
    [InlineData(ZMachineVersion.V5)]
    public void RemoveUnlinksFromTheMiddleOfASiblingChain(ZMachineVersion version)
    {
        var table = World(version).Table();

        // The lamp is the second child; removing it leaves the box alone.
        table.Remove(3);

        Assert.Equal(0, table.Parent(3));
        Assert.Equal(2, table.Child(1));
        Assert.Equal(0, table.Sibling(2));
    }

    [Theory]
    [InlineData(ZMachineVersion.V3)]
    [InlineData(ZMachineVersion.V5)]
    public void RemoveOfAParentlessObjectDoesNothing(ZMachineVersion version)
    {
        var table = World(version).Table();

        table.Remove(1);

        Assert.Equal(0, table.Parent(1));
        Assert.Equal(2, table.Child(1));
    }

    [Theory]
    [InlineData(ZMachineVersion.V3)]
    [InlineData(ZMachineVersion.V5)]
    public void InsertMakesAnObjectTheFirstChild(ZMachineVersion version)
    {
        var table = World(version).Table();

        // [zm op:insert_obj] The coin moves from the box into the room,
        // ahead of the box and the lamp.
        table.Insert(4, 1);

        Assert.Equal(1, table.Parent(4));
        Assert.Equal(4, table.Child(1));
        Assert.Equal(2, table.Sibling(4));
        Assert.Equal(3, table.Sibling(2));

        // And the box no longer holds it.
        Assert.Equal(0, table.Child(2));
    }

    [Theory]
    [InlineData(ZMachineVersion.V3)]
    [InlineData(ZMachineVersion.V5)]
    public void InsertMovesAnObjectTogetherWithItsChildren(ZMachineVersion version)
    {
        var table = World(version).Table();

        // The box, coin and all, goes into the lamp.
        table.Insert(2, 3);

        Assert.Equal(3, table.Parent(2));
        Assert.Equal(2, table.Child(3));
        Assert.Equal(0, table.Sibling(2));
        Assert.Equal(4, table.Child(2));
        Assert.Equal(2, table.Parent(4));
        Assert.Equal(3, table.Child(1));
    }

    [Theory]
    [InlineData(ZMachineVersion.V3)]
    [InlineData(ZMachineVersion.V5)]
    public void InsertAcceptsAnObjectWithNoParent(ZMachineVersion version)
    {
        var table = World(version).Table();
        table.Remove(3);

        // [zm op:insert_obj] It may legally have parent zero to start.
        table.Insert(3, 2);

        Assert.Equal(2, table.Parent(3));
        Assert.Equal(3, table.Child(2));
        Assert.Equal(4, table.Sibling(3));
    }

    [Theory]
    [InlineData(ZMachineVersion.V3)]
    [InlineData(ZMachineVersion.V5)]
    public void ObjectZeroIsNotAnObject(ZMachineVersion version)
    {
        var table = World(version).Table();

        // [zm 12.3] Object 0 means nothing.
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Parent(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.ShortName(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Parent(table.MaxObjects + 1));

        // But it is a valid value for a link.
        table.SetParent(2, 0);
        Assert.Equal(0, table.Parent(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.SetParent(2, table.MaxObjects + 1));
    }
}

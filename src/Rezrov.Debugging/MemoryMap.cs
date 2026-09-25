using System.Globalization;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Lexing;
using Rezrov.ZMachine.Objects;
using Rezrov.ZMachine.Text;

namespace Rezrov.Debugging;

/// <summary>
/// What every byte of a story file is for.
/// </summary>
/// <remarks>
/// [zm 1.1] The header names the tables and the two boundaries, and
/// almost every table's size can be worked out exactly rather than
/// guessed: the abbreviations are a fixed count of pointers, the
/// globals are 240 words, the dictionary says how many entries it has
/// and how long each one is, and the object entries end where the
/// first property table begins.
///
/// What is left over is reported as unaccounted rather than explained
/// away, so the account always adds up to the length of the file and
/// anything this does not understand is visible instead of hidden.
/// </remarks>
public static class MemoryMap
{
    /// <summary>
    /// The whole file, region by region, in address order and with no
    /// byte left out.
    /// </summary>
    /// <remarks>
    /// Regions can overlap, and in a few Infocom games they do: Zork
    /// Zero puts its object table twelve bytes inside the 240 words
    /// [zm 6.2] reserves for globals, because the game never uses the
    /// last few. Both are shown where that happens, since dropping
    /// either would be inventing an answer to a question the file does
    /// not settle. What nothing covers is reported as unaccounted, so
    /// the map always adds up to the length of the file.
    /// </remarks>
    public static IReadOnlyList<MemoryRegion> Of(ZMemory memory, StoryHeader header)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(header);

        var known = new List<MemoryRegion>();

        void Add(string name, int address, int length)
        {
            if (address > 0 && length > 0 && address + length <= memory.Length)
            {
                known.Add(new MemoryRegion(name, address, length));
            }
        }

        // [zm 11] The header itself, which every version has.
        known.Add(new MemoryRegion("header", 0, ZMemory.HeaderLength));

        Abbreviations(memory, header, Add);
        Objects(memory, header, Add);

        // [zm 6.2] The globals are 240 words, always.
        Add("global variables", header.GlobalVariablesAddress, 240 * 2);

        Dictionary(memory, header, Add);
        Tables(memory, header, Add);

        // [zm 1.1.1] Everything from here on is routines and strings.
        Add("code and text", header.HighMemoryBase, memory.Length - header.HighMemoryBase);

        var covered = new bool[memory.Length];

        foreach (var region in known)
        {
            for (var i = region.Address; i < region.EndAddress; i++)
            {
                covered[i] = true;
            }
        }

        var map = new List<MemoryRegion>(known);
        var at = 0;

        while (at < memory.Length)
        {
            if (covered[at])
            {
                at++;
                continue;
            }

            var start = at;

            while (at < memory.Length && !covered[at])
            {
                at++;
            }

            map.Add(new MemoryRegion(MemoryRegion.Unaccounted, start, at - start));
        }

        map.Sort((a, b) => a.Address != b.Address
            ? a.Address.CompareTo(b.Address)
            : a.Length.CompareTo(b.Length));

        return map;
    }

    /// <summary>Writes the map as a table.</summary>
    public static void Write(ZMemory memory, StoryHeader header, TextWriter to)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(to);

        var map = Of(memory, header);

        to.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"; Version {(int)header.Version}, release {header.Release}, serial {header.SerialCode}"));

        to.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"; static memory at {header.StaticMemoryBase:X4}, high memory at {header.HighMemoryBase:X4}"));

        to.WriteLine();
        to.WriteLine("address   bytes  region");

        foreach (var region in map)
        {
            to.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{region.Address:X4}   {region.Length,8}  {region.Name}"));
        }

        var lost = map.Where(r => r.Name == MemoryRegion.Unaccounted).Sum(r => (long)r.Length);
        var named = map.Where(r => r.Name != MemoryRegion.Unaccounted).Sum(r => (long)r.Length);
        var shared = named - (memory.Length - lost);

        to.WriteLine();
        to.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{memory.Length} bytes in the file, {lost} unaccounted, {shared} claimed twice"));
    }

    /// <summary>
    /// [zm 3.3] The abbreviation pointers, and the text they point at.
    /// </summary>
    private static void Abbreviations(ZMemory memory, StoryHeader header, Action<string, int, int> add)
    {
        // [zm 3.3] Version 1 has no abbreviations, Version 2 has 32, and
        // every later version has 96.
        var count = header.Version switch
        {
            ZMachineVersion.V1 => 0,
            ZMachineVersion.V2 => 32,
            _ => 96,
        };

        var table = header.AbbreviationsTableAddress;

        if (count == 0 || table == 0)
        {
            return;
        }

        add("abbreviation pointers", table, count * 2);

        // [zm 3.3] Each entry is a word address, which is doubled
        // whatever the version. The strings are written consecutively in
        // practice, so their extent is one region rather than 96.
        var text = new ZTextDecoder(memory, header);
        var first = int.MaxValue;
        var last = 0;

        try
        {
            for (var i = 0; i < count; i++)
            {
                var at = memory.ReadWord(table + (i * 2)) * 2;
                first = Math.Min(first, at);
                last = Math.Max(last, text.SkipString(at));
            }
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException)
        {
            return;
        }

        if (last > first)
        {
            add("abbreviation text", first, last - first);
        }
    }

    /// <summary>
    /// [zm 12] The property defaults, the object entries, and the
    /// property tables the entries point at.
    /// </summary>
    private static void Objects(ZMemory memory, StoryHeader header, Action<string, int, int> add)
    {
        if (header.ObjectTableAddress == 0)
        {
            return;
        }

        ObjectTable objects;

        try
        {
            objects = new ObjectTable(memory, header, new ZTextDecoder(memory, header));
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidDataException)
        {
            return;
        }

        // [zm 12.2] 31 words in Versions 1 to 3, and 63 from Version 4.
        add("property defaults", objects.Address, objects.MaxPropertyNumber * 2);

        // [zm 12.3] Nine bytes an object in Versions 1 to 3, fourteen
        // from Version 4.
        var entry = header.Version <= ZMachineVersion.V3 ? 9 : 14;
        add("object entries", objects.FirstObjectAddress, objects.Count * entry);

        var first = int.MaxValue;
        var last = 0;

        try
        {
            for (var obj = 1; obj <= objects.Count; obj++)
            {
                var start = objects.PropertyTableAddress(obj);
                first = Math.Min(first, start);

                // [zm 12.4] The short name, then the properties, then a
                // zero byte to say there are no more.
                var end = objects.FirstPropertyAddress(obj);

                foreach (var property in objects.Properties(obj))
                {
                    end = property.DataAddress + property.Length;
                }

                last = Math.Max(last, end + 1);
            }
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidDataException)
        {
            return;
        }

        if (last > first)
        {
            add("property tables", first, last - first);
        }
    }

    /// <summary>
    /// [zm 13] The word separators, then the entries, whose count and
    /// length the table states.
    /// </summary>
    private static void Dictionary(ZMemory memory, StoryHeader header, Action<string, int, int> add)
    {
        if (header.DictionaryAddress == 0)
        {
            return;
        }

        try
        {
            var text = new ZTextDecoder(memory, header);
            var encoder = new ZTextEncoder(header.Version, text.Alphabets);
            var words = DictionaryTable.Standard(memory, header, text, encoder);

            add(
                "dictionary",
                words.Address,
                words.FirstEntryAddress - words.Address + (words.Count * words.EntryLength));
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidDataException)
        {
        }
    }

    /// <summary>
    /// The smaller tables a later version may carry, each of which says
    /// its own length.
    /// </summary>
    private static void Tables(ZMemory memory, StoryHeader header, Action<string, int, int> add)
    {
        if (header.Version < ZMachineVersion.V5)
        {
            return;
        }

        try
        {
            // [zm 3.5.5.1] Three alphabets of 26 characters each.
            add("alphabet table", header.AlphabetTableAddress, AlphabetTable.LengthInBytes);

            // [zm 13.7] A list of terminating characters ending in zero.
            var terminators = (int)header.TerminatingCharactersTableAddress;

            if (terminators > 0)
            {
                var at = terminators;

                while (at < memory.Length && memory.ReadByte(at) != 0)
                {
                    at++;
                }

                add("terminating characters", terminators, at - terminators + 1);
            }

            // [zm 11.1] The extension table begins with the number of
            // further words it holds.
            var extension = (int)header.HeaderExtensionTableAddress;

            if (extension > 0)
            {
                add("header extension", extension, (memory.ReadWord(extension) + 1) * 2);
            }

            // [zm 3.8.5.4] The Unicode table is a count byte and that
            // many words.
            if (extension > 0 && memory.ReadWord(extension) >= 3)
            {
                var unicode = (int)header.UnicodeTranslationTableAddress;

                if (unicode > 0)
                {
                    add("unicode table", unicode, (memory.ReadByte(unicode) * 2) + 1);
                }
            }
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException)
        {
        }
    }
}

using Rezrov.ZMachine;
using Rezrov.ZMachine.Objects;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

/// <summary>
/// Runs the interpreter's building blocks over every real story file in
/// the entharion submodule. These are the checks that hand-built inputs
/// cannot give: that the code accepts every file Infocom and Inform ever
/// shipped, and agrees with what their compilers wrote.
/// </summary>
/// <remarks>
/// The submodule is optional, and CI does not fetch it, so every test here
/// skips rather than fails when it is absent. Locally, with the submodule
/// populated, they run against the full corpus.
/// </remarks>
public class StoryCorpusTests
{
    private const string SubmoduleAbsent = "The entharion submodule is not populated.";

    [Fact]
    public void EveryStoryFileHasAValidHeader()
    {
        var files = Corpus.StoryFiles();
        Assert.SkipUnless(files.Count > 0, SubmoduleAbsent);

        var failures = new List<string>();

        foreach (var file in files)
        {
            try
            {
                var header = new StoryHeader(new ZMemory(File.ReadAllBytes(file)));

                // The extension on these files is the version, so the two
                // had better agree.
                var expected = Corpus.VersionFromExtension(file);
                if ((int)header.Version != expected)
                {
                    failures.Add($"{Path.GetFileName(file)}: header says Version {(int)header.Version}");
                }
            }
            catch (InvalidDataException e)
            {
                failures.Add($"{Path.GetFileName(file)}: {e.Message}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void EveryStoryFileWithAChecksumVerifies()
    {
        var files = Corpus.StoryFiles();
        Assert.SkipUnless(files.Count > 0, SubmoduleAbsent);

        var failures = new List<string>();
        var verified = 0;

        foreach (var file in files)
        {
            var header = new StoryHeader(new ZMemory(File.ReadAllBytes(file)));

            // [zm 11.1] Early Version 3 files have nothing to verify.
            if (!header.HasFileLength)
            {
                continue;
            }

            // A declared length with a zero checksum means the compiler
            // wrote the one and not the other. Inform 1 did this in 1993,
            // and dejavu-r1-s930921.z3 is the example in this corpus. Such
            // a file fails verification honestly, so it is not a failure
            // of the checksum code and is left out of the count.
            if (header.Checksum == 0)
            {
                continue;
            }

            if (header.VerifyChecksum())
            {
                verified++;
            }
            else
            {
                failures.Add(
                    $"{Path.GetFileName(file)}: header {header.Checksum:X4}, computed {header.ComputeChecksum():X4}");
            }
        }

        Assert.Empty(failures);
        Assert.True(verified > 0, "No file in the corpus carried a checksum, which cannot be right.");
    }

    [Fact]
    public void SimpleTestFixturesDecodeTheirSentence()
    {
        var fixtures = Corpus.SimpleTestFixtures();
        Assert.SkipUnless(fixtures.Count > 0, SubmoduleAbsent);

        // One fixture per version, so all eight decoders get exercised.
        Assert.Equal(8, fixtures.Count);

        var failures = new List<string>();

        foreach (var file in fixtures)
        {
            var memory = new ZMemory(File.ReadAllBytes(file));
            var header = new StoryHeader(memory);
            var decoder = new ZTextDecoder(memory, header);

            var text = decoder.Decode(SimpleTestSentenceAddress(memory, header));

            if (text != "hello from all z machine versions")
            {
                failures.Add($"{Path.GetFileName(file)} decoded as \"{text}\"");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void EveryAbbreviationInTheCorpusDecodes()
    {
        var files = Corpus.StoryFiles();
        Assert.SkipUnless(files.Count > 0, SubmoduleAbsent);

        var decoded = 0;

        foreach (var file in files)
        {
            var memory = new ZMemory(File.ReadAllBytes(file));
            var header = new StoryHeader(memory);

            // [zm 3.5.2] Version 1 has no abbreviations at all, and
            // [zm 3.3] Version 2 has 32 where later versions have 96.
            var count = header.Version switch
            {
                ZMachineVersion.V1 => 0,
                ZMachineVersion.V2 => 32,
                _ => 96,
            };

            // A few Infocom files, both "generic" releases and every
            // "ziptest" among them, have no abbreviations table at all
            // and leave its address as zero. There is nothing to decode
            // in those.
            if (count == 0 || header.AbbreviationsTableAddress == 0)
            {
                continue;
            }

            var decoder = new ZTextDecoder(memory, header);

            for (var number = 0; number < count; number++)
            {
                // [zm 3.3] Each entry is a word address, so double it.
                var address = memory.ReadWord(header.AbbreviationsTableAddress + (number * 2)) * 2;

                // The point is that every one of these decodes without
                // throwing, across every version and every compiler in
                // the corpus. The text itself has no ground truth to
                // compare against yet.
                _ = decoder.Decode(address);
                decoded++;
            }
        }

        Assert.True(decoded > 0);
    }

    /// <summary>
    /// Objects whose property tables are known to be malformed in the
    /// shipped file, and are therefore not evidence of a bug here.
    /// </summary>
    /// <remarks>
    /// Sherlock's object 308, the opal, has a sound property list down to
    /// property 43 and garbage after it: the numbers stop descending and
    /// a two-byte size has its second byte's top bit clear, which
    /// [zm 12.4.2.1] says never happens. An interpreter never sees past
    /// the break, because a property lookup scans downward and stops once
    /// the numbers fall below the one wanted, which is what Frotz does and
    /// what <see cref="ObjectTable.TryFindProperty"/> does.
    /// </remarks>
    private static readonly HashSet<(string File, int Object)> KnownMalformed =
    [
        ("sherlock-r26-s880127.z5", 308),
    ];

    /// <summary>
    /// Files whose property tables are malformed throughout.
    /// </summary>
    /// <remarks>
    /// Destruct is the corpus's one Version 1 file compiled by Inform 6,
    /// which never properly supported Versions 1 and 2. In 11 of its 42
    /// objects the property list carries junk blocks after the last real
    /// property, out of order and in one case with a number of 0 in the
    /// middle of the list. Infocom's own Version 1 and 2 releases of Zork
    /// I are fine, so this is the compiler, not the layout.
    /// </remarks>
    private static readonly HashSet<string> KnownMalformedFiles =
    [
        "destruct-r1-s030509.z1",
    ];

    [Fact]
    public void EveryObjectTableIsWellFormed()
    {
        var files = Corpus.StoryFiles();
        Assert.SkipUnless(files.Count > 0, SubmoduleAbsent);

        var failures = new List<string>();
        var objects = 0;
        var properties = 0;

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var memory = new ZMemory(File.ReadAllBytes(file));
            var header = new StoryHeader(memory);
            var table = new ObjectTable(memory, header, new ZTextDecoder(memory, header));

            if (table.Count < 1)
            {
                failures.Add($"{name}: no objects found");
                continue;
            }

            // [zm 12.4.1] and [zm 12.4.2] The most data a property can
            // carry in each layout.
            var longest = header.Version <= ZMachineVersion.V3 ? 8 : 64;

            for (var obj = 1; obj <= table.Count; obj++)
            {
                objects++;

                // [zm 12.3.1] The links must all hold valid object
                // numbers, which is also the first thing to go wrong if
                // the count or the entry size were wrong.
                foreach (var (link, label) in new[]
                {
                    (table.Parent(obj), "parent"),
                    (table.Sibling(obj), "sibling"),
                    (table.Child(obj), "child"),
                })
                {
                    if (link > table.Count)
                    {
                        failures.Add($"{name}: object {obj} has {label} {link}, past the last object {table.Count}");
                    }
                }

                try
                {
                    _ = table.ShortName(obj);

                    if (KnownMalformedFiles.Contains(name) || KnownMalformed.Contains((name, obj)))
                    {
                        continue;
                    }

                    var previous = int.MaxValue;
                    foreach (var block in table.Properties(obj))
                    {
                        properties++;

                        // [zm 12.4] Descending, though not strictly:
                        // Inform 6 sometimes wrote the same property
                        // number twice in a row, and Curses release 16,
                        // Jigsaw, and The Magic Toyshop each carry an
                        // object like that. A lookup finds the first copy
                        // and never reaches the second.
                        if (block.Number > previous)
                        {
                            failures.Add($"{name}: object {obj} has property {block.Number} after {previous}");
                        }

                        if (block.Number > table.MaxPropertyNumber || block.Length < 1 || block.Length > longest)
                        {
                            failures.Add($"{name}: object {obj} property {block.Number} has length {block.Length}");
                        }

                        // The backward read of the size must agree with
                        // the forward one.
                        if (table.PropertyLength(block.DataAddress) != block.Length)
                        {
                            failures.Add($"{name}: object {obj} property {block.Number} reads back a different length");
                        }

                        previous = block.Number;
                    }
                }
                catch (Exception e) when (e is InvalidDataException or ArgumentOutOfRangeException)
                {
                    failures.Add($"{name}: object {obj}: {e.Message}");
                }
            }
        }

        Assert.Empty(failures.Take(20));
        Assert.True(objects > 1000, $"Only {objects} objects across the corpus, which is too few to be right.");
        Assert.True(properties > objects, "Fewer properties than objects, which cannot be right.");
    }

    [Fact]
    public void EveryInitialObjectTreeIsWellFounded()
    {
        var files = Corpus.StoryFiles();
        Assert.SkipUnless(files.Count > 0, SubmoduleAbsent);

        var failures = new List<string>();

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var memory = new ZMemory(File.ReadAllBytes(file));
            var header = new StoryHeader(memory);
            var table = new ObjectTable(memory, header, new ZTextDecoder(memory, header));

            // [zm 12.5] The three conditions, checked on the initial tree,
            // which is the only state a compiler is responsible for.
            for (var obj = 1; obj <= table.Count; obj++)
            {
                var parent = table.Parent(obj);

                // (a) An object with a sibling also has a parent.
                if (table.Sibling(obj) != 0 && parent == 0)
                {
                    failures.Add($"{name}: object {obj} has a sibling but no parent");
                }

                // (b) An object is the parent of exactly those objects in
                // the sibling list of its child. Checked from both sides:
                // everything in the chain claims this parent, and this
                // object appears in its own parent's chain.
                var seen = 0;
                for (var child = table.Child(obj); child != 0; child = table.Sibling(child))
                {
                    if (table.Parent(child) != obj)
                    {
                        failures.Add($"{name}: object {child} is in the chain of {obj} but claims parent {table.Parent(child)}");
                    }

                    if (++seen > table.Count)
                    {
                        failures.Add($"{name}: the children of {obj} form a cycle");
                        break;
                    }
                }

                if (parent != 0)
                {
                    var found = false;
                    for (var child = table.Child(parent); child != 0; child = table.Sibling(child))
                    {
                        if (child == obj)
                        {
                            found = true;
                            break;
                        }

                        if (++seen > table.Count)
                        {
                            break;
                        }
                    }

                    if (!found)
                    {
                        failures.Add($"{name}: object {obj} claims parent {parent} but is not among its children");
                    }
                }

                // (c) Every object has a finite level, so following
                // parents upward must reach the top.
                var depth = 0;
                for (var up = parent; up != 0; up = table.Parent(up))
                {
                    if (++depth > table.Count)
                    {
                        failures.Add($"{name}: the ancestors of {obj} form a cycle");
                        break;
                    }
                }
            }
        }

        Assert.Empty(failures.Take(20));
    }

    [Fact]
    public void ZorkOneNamesItsRooms()
    {
        var file = Corpus.StoryFiles("zcode-infocom")
            .FirstOrDefault(f => Path.GetFileName(f) == "zork1-r88-s840726.z3");
        Assert.SkipUnless(file is not null, SubmoduleAbsent);

        var memory = new ZMemory(File.ReadAllBytes(file));
        var header = new StoryHeader(memory);
        var table = new ObjectTable(memory, header, new ZTextDecoder(memory, header));

        var names = Enumerable.Range(1, table.Count).Select(table.ShortName).ToHashSet();

        // Ground truth at last: text that a person recognizes, produced
        // from the real file by the header, the text decoder, and the
        // object table together.
        Assert.Contains("West of House", names);
        Assert.Contains("small mailbox", names);
        Assert.Contains("brass lantern", names);
    }

    /// <summary>
    /// Finds the address of the sentence in a simple-test fixture.
    /// </summary>
    /// <remarks>
    /// Inform compiles a print of a literal string as print_paddr with a
    /// packed address, not as an inline print, and it wraps Main in a stub
    /// that calls it. Inspecting the eight fixtures shows the same shape in
    /// every one: from the entry point, the first byte $8D is the
    /// print_paddr instruction in short form with a large constant
    /// operand, and the word after it is the packed address of the
    /// string. This scan relies on that inspection rather than on a real
    /// instruction decoder, and it should be replaced with one once that
    /// exists.
    /// </remarks>
    private static int SimpleTestSentenceAddress(ZMemory memory, StoryHeader header)
    {
        // [zm 11.1] Version 6 starts by calling a packed main routine, and
        // every other version starts executing at a byte address.
        var start = header.Version == ZMachineVersion.V6
            ? header.UnpackRoutineAddress(header.MainRoutinePackedAddress)
            : header.InitialProgramCounter;

        var at = start;
        while (memory.ReadByte(at) != 0x8D)
        {
            at++;
        }

        return header.UnpackStringAddress(memory.ReadWord(at + 1));
    }
}

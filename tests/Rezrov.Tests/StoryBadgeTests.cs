using Rezrov.Core;
using Rezrov.Core.Graphics;
using Rezrov.ZMachine;

namespace Rezrov.Tests;

/// <summary>
/// What a frontend shows for a story: the name on its window, the mark
/// beside it, and the icon files those marks are drawn from.
/// </summary>
public class StoryBadgeTests
{
    [Fact]
    public void AGameInfocomMadeIsNamedAndMarkedAsTheirs()
    {
        var path = Corpus.StoryFiles().FirstOrDefault(f => Path.GetFileName(f) == "zork1-r88-s840726.z3");
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        var story = File.ReadAllBytes(path!);

        Assert.Equal("Zork 1 (Z3)", StoryBadges.Title(path!, StoryFormat.ZMachine, story, packaged: false));
        Assert.Equal(StoryIcons.Infocom, StoryBadges.Icon(StoryFormat.ZMachine, story));
    }

    [Fact]
    public void AGameNobodyCanNameIsCalledAfterItsFileAndMarkedByItsMachine()
    {
        // Curses is the case that made the catalog worth having: early
        // Inform left the compiler bytes empty, exactly as Infocom did,
        // so nothing but the catalog tells the two apart.
        var path = Corpus.StoryFiles().FirstOrDefault(f => Path.GetFileName(f) == "curses-r16-s951024.z5");
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        var story = File.ReadAllBytes(path!);

        Assert.Equal("curses-r16-s951024.z5 (Z5)", StoryBadges.Title(path!, StoryFormat.ZMachine, story, packaged: false));
        Assert.Equal("z5", StoryBadges.Icon(StoryFormat.ZMachine, story));
    }

    [Fact]
    public void AStoryThatCamePackagedSaysSoWhereTheIconCannot()
    {
        // A window has one icon and three things worth saying, so the
        // fact an icon has no room left for goes in the title instead.
        var story = new byte[] { 8 };

        Assert.Equal("bronze.z8 (Z8, Blorb)", StoryBadges.Title("bronze.z8", StoryFormat.ZMachine, story, packaged: true));
        Assert.Equal("bronze.z8 (Z8)", StoryBadges.Title("bronze.z8", StoryFormat.ZMachine, story, packaged: false));
    }

    [Fact]
    public void AStoryOfAnotherMachineWearsTheProgramsOwnMark()
    {
        Assert.Equal(StoryIcons.Rezrov, StoryBadges.Icon(StoryFormat.Glulx, [0x47, 0x6C, 0x75, 0x6C]));
        Assert.Equal(StoryIcons.Rezrov, StoryBadges.Icon(StoryFormat.AaMachine, [0x46, 0x4F, 0x52, 0x4D]));

        // And is called after its file, with nothing claimed about a
        // machine that has no versions to claim.
        Assert.Equal("advent.ulx", StoryBadges.Title("advent.ulx", StoryFormat.Glulx, [0x47], packaged: false));
        Assert.Equal("tethered.aastory (Blorb)", StoryBadges.Title("tethered.aastory", StoryFormat.AaMachine, [0x46], packaged: true));
    }

    [Fact]
    public void EveryMarkTheBadgesCanAskForIsThereAndDecodes()
    {
        var names = new List<string> { StoryIcons.Infocom, StoryIcons.Rezrov };

        for (var version = 1; version <= 8; version++)
        {
            names.Add(StoryIcons.ForVersion(version));
        }

        foreach (var name in names)
        {
            var bytes = StoryIcons.Read(name);

            Assert.True(bytes is { Length: > 0 }, $"the {name} mark is not in the program");

            var picture = IcoReader.Read(bytes!, 32);

            Assert.NotNull(picture);
            Assert.Equal(32, picture.Width);
            Assert.Equal(32, picture.Height);

            // A mark of one color is a mark that did not decode: the
            // classic ones are lettering on a ground, and the
            // program's own has more than that.
            var colors = new HashSet<(byte, byte, byte, byte)>();

            for (var y = 0; y < picture.Height; y++)
            {
                for (var x = 0; x < picture.Width; x++)
                {
                    colors.Add(picture.At(x, y));
                }
            }

            Assert.True(colors.Count > 1, $"the {name} mark came out in {colors.Count} color");
        }

        Assert.Null(StoryIcons.Read("nosuchmark"));
        Assert.Null(StoryIcons.Pixels("nosuchmark", 32));
    }

    [Fact]
    public void TheClassicMarksAreWhiteLetteringOnNavyRatherThanWashedOut()
    {
        // The reason this decoder is written out rather than handed to
        // the system: an icon of sixteen colors carries its
        // transparency in a mask, and a reader that ignores the mask
        // washes these out to nearly nothing.
        var picture = StoryIcons.Pixels(StoryIcons.ForVersion(5), 32);

        Assert.NotNull(picture);

        var navy = 0;
        var white = 0;

        for (var y = 0; y < picture.Height; y++)
        {
            for (var x = 0; x < picture.Width; x++)
            {
                var (red, green, blue, alpha) = picture.At(x, y);

                if (alpha == 0)
                {
                    continue;
                }

                if (red == 0 && green == 0 && blue == 128)
                {
                    navy++;
                }
                else if (red == 255 && green == 255 && blue == 255)
                {
                    white++;
                }
            }
        }

        Assert.True(navy > white, $"{navy} navy pixels against {white} white ones");
        Assert.True(white > 40, $"only {white} pixels of lettering");
    }

    [Fact]
    public void TheProgramsOwnMarkComesInTheSizesAWindowAsksFor()
    {
        var bytes = StoryIcons.Read(StoryIcons.Rezrov);

        Assert.NotNull(bytes);

        var sizes = IcoReader.Read(bytes).Select(picture => picture.Width).ToList();

        // Largest first, and the three a window and a task bar want.
        Assert.Equal([48, 32, 16], sizes);

        // Asking for a size gives that one, and asking for something
        // larger than any of them gives the largest there is.
        Assert.Equal(16, IcoReader.Read(bytes, 16)!.Width);
        Assert.Equal(32, IcoReader.Read(bytes, 17)!.Width);
        Assert.Equal(48, IcoReader.Read(bytes, 256)!.Width);
    }

    [Fact]
    public void AMarkCanBeHandedToASystemThatWouldRatherDecodeItItself()
    {
        // Windows builds an icon from the file's own bytes, so it is
        // told where in the file to look rather than given pixels.
        var bytes = StoryIcons.Read(StoryIcons.Rezrov);

        Assert.NotNull(bytes);

        var small = IcoReader.Find(bytes, 16);
        var large = IcoReader.Find(bytes, 48);

        Assert.NotNull(small);
        Assert.NotNull(large);
        Assert.NotEqual(small!.Value.Offset, large!.Value.Offset);

        // What it points at has to be the picture of that size, which
        // is the whole of the promise. These are bitmaps rather than
        // whole icon files, so the width is read where a bitmap keeps
        // it, which is where the system will read it too.
        Assert.Equal(16, Wide(bytes, small.Value));
        Assert.Equal(48, Wide(bytes, large.Value));

        // A mark of one size answers every question with that size.
        var one = StoryIcons.Read(StoryIcons.Infocom);

        Assert.NotNull(one);
        Assert.NotNull(IcoReader.Find(one, 16));
        Assert.NotNull(IcoReader.Find(one, 256));

        Assert.Null(IcoReader.Find([], 32));
        Assert.Null(IcoReader.Find([0x89, 0x50, 0x4E, 0x47, 0, 0], 32));
    }

    /// <summary>
    /// How wide the bitmap at a place in an icon file says it is.
    /// </summary>
    private static int Wide(byte[] icon, (int Offset, int Length) place) =>
        BitConverter.ToInt32(icon, place.Offset + 4);

    [Fact]
    public void SomethingThatIsNotAnIconIsNotReadAsOne()
    {
        Assert.Empty(IcoReader.Read([]));
        Assert.Empty(IcoReader.Read([0x89, 0x50, 0x4E, 0x47, 0, 0]));
        Assert.Null(IcoReader.Read([0, 0, 1, 0], 32));
    }
}

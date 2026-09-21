using System.Text;
using Rezrov.ZMachine;

namespace Rezrov.Tests;

/// <summary>
/// [babel legacy Z-code IFID] Naming a Z-code story: the identifier the
/// Treaty of Babel works out from its header, and the Infocom game that
/// identifier belongs to.
/// </summary>
public class IfidTests
{
    [Fact]
    public void AModernStoryIsNamedFromItsThreeNumbers()
    {
        // The treaty's own worked example, byte for byte: Savoir-Faire
        // release 8, serial 040205, checksum 6630.
        Assert.Equal("ZCODE-8-040205-6630", Ifid.Of(Header(8, "040205", 0x6630)));
    }

    [Fact]
    public void AnInfocomStoryIsNamedWithoutAChecksum()
    {
        // [babel legacy Z-code IFID] A serial that begins with eight is
        // a date in the nineteen eighties, and those files handled
        // their checksums too variously to be named by one.
        Assert.Equal("ZCODE-11-860509", Ifid.Of(Header(11, "860509", 0x1234)));
        Assert.Equal("ZCODE-12-860926", Ifid.Of(Header(12, "860926", 0x4321)));
    }

    [Fact]
    public void ASerialThatIsNotADateIsNamedAsTheTreatySaysToNameIt()
    {
        // The treaty lists these by name. A serial of letters is left
        // as it is and earns no checksum.
        Assert.Equal("ZCODE-2-AS000C", Ifid.Of(Header(2, "AS000C", 0x1111)));
        Assert.Equal("ZCODE-15-UG3AU5", Ifid.Of(Header(15, "UG3AU5", 0x2222)));

        // Anything that is not a letter or a digit becomes a hyphen,
        // nulls above all, and a serial of nothing but hyphens is one
        // of the three the treaty refuses to trust.
        Assert.Equal("ZCODE-5-------", Ifid.Of(Header(5, "\0\0\0\0\0\0", 0x3333)));
        Assert.Equal("ZCODE-15-999999", Ifid.Of(Header(15, "999999", 0x4444)));
        Assert.Equal("ZCODE-67-000000", Ifid.Of(Header(67, "000000", 0x5555)));

        // A serial that is digits but not a date is still trusted
        // enough for a checksum, which is what tells the Leather
        // Goddesses beta from its siblings.
        Assert.Equal("ZCODE-59-000001-D070", Ifid.Of(Header(59, "000001", 0xD070)));
    }

    [Fact]
    public void AStoryBrandedWithItsOwnNameIsCalledThat()
    {
        // [babel #embed-formats] A story compiled from 2006 onward may
        // carry its identifier inside it, and that beats the header.
        var story = Header(1, "090114", 0x1234, "UUID://1974A053-7DB0-4103-93A1-767C1382C0B7//");

        Assert.Equal("1974A053-7DB0-4103-93A1-767C1382C0B7", Ifid.Of(story));

        // A brand with no closing slashes is not one.
        Assert.Equal("ZCODE-1-090114-1234", Ifid.Of(Header(1, "090114", 0x1234, "UUID://NOTCLOSED")));
    }

    [Fact]
    public void AStoryOldEnoughToHaveNoBrandIsNotSearchedForOne()
    {
        // The treaty says searching is unnecessary before 2006, and it
        // is more than unnecessary: a story whose text happens to hold
        // the pattern would otherwise be named after it.
        var story = Header(88, "840726", 0xA129, "UUID://NOTTHISONE//");

        Assert.Equal("ZCODE-88-840726", Ifid.Of(story));
    }

    [Fact]
    public void AFileTooShortToHaveAHeaderIsNotNamed()
    {
        Assert.Null(Ifid.Of(new byte[4]));
        Assert.Null(Ifid.Of([]));
    }

    [Fact]
    public void TheCatalogNamesEveryInfocomStoryInTheCorpus()
    {
        var stories = Corpus.StoryFiles("zcode-infocom");
        Assert.SkipUnless(stories.Count > 0, "The entharion submodule is not populated.");

        var missed = new List<string>();
        var named = 0;

        foreach (var path in stories)
        {
            if (Ifid.Of(File.ReadAllBytes(path)) is not { } ifid)
            {
                continue;
            }

            if (InfocomCatalog.Title(ifid) is null)
            {
                missed.Add($"{Path.GetFileName(path)} is {ifid}");
            }
            else
            {
                named++;
            }
        }

        Assert.Empty(missed);
        Assert.True(named > 50, $"only {named} of Infocom's stories were named");
    }

    [Fact]
    public void TheCatalogClaimsNothingThatIsNotInfocoms()
    {
        var stories = Corpus.StoryFiles("zcode-inform");
        Assert.SkipUnless(stories.Count > 0, "The entharion submodule is not populated.");

        // A false name is worse than no name, and the eight stories in
        // the corpus that early Inform left with no compiler field are
        // exactly the ones a weaker test would have claimed.
        var claimed = stories
            .Where(path => InfocomCatalog.Title(Ifid.Of(File.ReadAllBytes(path))) is not null)
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(claimed);
    }

    [Fact]
    public void TheCatalogIsTheWholeOfIt()
    {
        // The count is here so that a table trimmed by accident is
        // noticed rather than quietly naming fewer games.
        Assert.Equal(246, InfocomCatalog.Count);

        Assert.Equal("Zork 1", InfocomCatalog.Title("ZCODE-88-840726"));
        Assert.Equal("Trinity", InfocomCatalog.Title("ZCODE-11-860509"));
        Assert.True(InfocomCatalog.Knows("ZCODE-59-000001-D070"));

        // [babel legacy Z-code IFID] Suspended and Zork I both shipped
        // as release 5 with no serial, so the catalog leaves that
        // identity unnamed rather than guessing between them.
        Assert.Null(InfocomCatalog.Title("ZCODE-5-------"));
        Assert.False(InfocomCatalog.Knows(null));
        Assert.Null(InfocomCatalog.Title("ZCODE-1-000000"));
    }

    /// <summary>
    /// A story file's first bytes, which is all the naming reads,
    /// optionally with something written after them.
    /// </summary>
    private static byte[] Header(ushort release, string serial, ushort checksum, string? after = null)
    {
        var story = new byte[0x40 + (after?.Length ?? 0)];

        story[0] = 3;
        story[0x02] = (byte)(release >> 8);
        story[0x03] = (byte)release;
        story[0x1C] = (byte)(checksum >> 8);
        story[0x1D] = (byte)checksum;

        for (var i = 0; i < 6 && i < serial.Length; i++)
        {
            story[0x12 + i] = (byte)serial[i];
        }

        if (after is not null)
        {
            Encoding.ASCII.GetBytes(after).CopyTo(story, 0x40);
        }

        return story;
    }
}

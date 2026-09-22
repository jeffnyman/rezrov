using System.Security.Cryptography;
using Rezrov.Core.Graphics;

namespace Rezrov.Tests;

/// <summary>
/// The artwork Infocom's DOS releases carried beside a Version 6 story,
/// in the three graphics standards a PC of the time might have.
/// </summary>
public class InfocomPicturesTests
{
    /// <summary>
    /// What each file in the corpus holds: how many pictures decode,
    /// how many are listed with no pixels, and how many this declines.
    /// </summary>
    /// <remarks>
    /// Measured against Mark Howell's pix2gif, which is the tool that
    /// has always decoded these and which is vendored beside the
    /// corpus.
    ///
    /// Every EGA and MCGA file reads whole. The three older CGA files
    /// read only in part, and the pictures they refuse are ones pix2gif
    /// cannot read either: it writes those out as a sixteen color GIF
    /// full of values above fifteen, which C never notices and no
    /// picture reader will open. Only the oldest CGA files are like
    /// that, zork0.cg1 being newer and whole, and every one of those
    /// games has EGA and MCGA artwork that reads perfectly. The counts
    /// are here so that a decoder which drifts says which file it
    /// drifted on and by how many pictures.
    /// </remarks>
    public static TheoryData<string, int, int, int> Files => new()
    {
        { "arthur.cg1", 44, 34, 92 },
        { "journey.cg1", 23, 0, 111 },
        { "shogun.cg1", 19, 6, 25 },
        { "zork0.cg1", 396, 107, 0 },
        { "arthur.eg1", 97, 28, 0 },
        { "arthur.eg2", 77, 24, 0 },
        { "journey.eg1", 80, 0, 0 },
        { "journey.eg2", 67, 0, 0 },
        { "shogun.eg1", 44, 6, 0 },
        { "zork0.eg1", 396, 107, 0 },
        { "arthur.mg1", 137, 34, 0 },
        { "beyondzo.mg1", 1, 0, 0 },
        { "journey.mg1", 134, 0, 0 },
        { "shogun.mg1", 42, 6, 0 },
        { "zorkzero.mg1", 396, 107, 0 },
    };

    [Theory]
    [MemberData(nameof(Files))]
    public void EveryPictureInTheCorpusReadsOrIsDeclined(string name, int drawn, int empty, int declined)
    {
        var path = Corpus.InfocomGraphics(name);

        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        var pictures = InfocomPictures.Read(File.ReadAllBytes(path));
        int drew = 0, none = 0, refused = 0;

        foreach (var picture in pictures.Pictures)
        {
            var pixels = pictures.Decode(picture.Number);

            if (pixels is null)
            {
                // A picture the file keeps no pixels for, against one
                // whose pixels this cannot make sense of.
                if (picture.HasPixels)
                {
                    refused++;
                }
                else
                {
                    none++;
                }
            }
            else
            {
                Assert.Equal((picture.Width, picture.Height), (pixels.Width, pixels.Height));
                drew++;
            }
        }

        Assert.Equal((drawn, empty, declined), (drew, none, refused));
    }

    [Fact]
    public void BeyondZorksTitleDecodesToTheSamePixelsTheReferenceGives()
    {
        var path = Corpus.InfocomGraphics("beyondzo.mg1");

        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        var pictures = InfocomPictures.Read(File.ReadAllBytes(path));
        var title = pictures.Decode(1);

        Assert.NotNull(title);
        Assert.Equal((320, 200), (title.Width, title.Height));

        // The hash is of the pixels pix2gif itself produces, taken
        // from its output rather than from ours, so this compares
        // against the reference and not against a recording of our own
        // behavior. Checked byte for byte when it was written: 192000
        // bytes, none of them different.
        var rgb = new byte[title.Width * title.Height * 3];

        for (var i = 0; i < title.Width * title.Height; i++)
        {
            rgb[(i * 3) + 0] = title.Rgba[(i * 4) + 0];
            rgb[(i * 3) + 1] = title.Rgba[(i * 4) + 1];
            rgb[(i * 3) + 2] = title.Rgba[(i * 4) + 2];
        }

        Assert.Equal(ReferenceTitle, Convert.ToHexString(SHA256.HashData(rgb)));
    }

    [Fact]
    public void ADirectoryThatDoesNotFitTheFileIsRefused()
    {
        // [infocom pictures] Sixteen bytes of header claiming a
        // thousand pictures, with nothing behind them.
        var file = new byte[16];
        file[4] = 0xE8;
        file[5] = 0x03;
        file[8] = 12;

        Assert.Throws<InvalidDataException>(() => InfocomPictures.Read(file));
        Assert.Throws<InvalidDataException>(() => InfocomPictures.Read(new byte[8]));
    }

    private const string ReferenceTitle =
        "47175597C5018D5E98E674B698CA694E348ABCF1B48BCECA9634EFCC265FED55";
}

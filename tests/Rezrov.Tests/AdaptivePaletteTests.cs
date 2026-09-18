using Rezrov.Core.Blorb;
using Rezrov.Core.Graphics;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Screen;

namespace Rezrov.Tests;

/// <summary>
/// [blorb 11.3] The pictures that take their colors from whatever was
/// plotted before them, which is how Arthur and Zork Zero shade one set
/// of drawings several ways.
/// </summary>
/// <remarks>
/// The colors here are the shape the real files have. An ordinary
/// picture carries a scene's colors; an adaptive one carries flat
/// primaries that are there only because the PNG standard insists on a
/// palette, and Arthur's really are the EGA primaries.
/// </remarks>
public class AdaptivePaletteTests
{
    private static readonly byte[] Scene =
    [
        0x00, 0x00, 0x00,
        0x01, 0x01, 0x01,
        0x20, 0x80, 0x20,
        0x80, 0x40, 0x20,
    ];

    private static readonly byte[] Placeholder =
    [
        0x09, 0x09, 0x09,
        0x08, 0x08, 0x08,
        0xAA, 0x00, 0x00,
        0x00, 0xAA, 0x00,
    ];

    private static readonly byte[] Shorter =
    [
        0x02, 0x02, 0x02,
        0x03, 0x03, 0x03,
        0x10, 0x30, 0x50,
    ];

    [Fact]
    public void TheResourceFileSaysWhichPicturesAdapt()
    {
        var blorb = Resources();

        Assert.Equal([2], blorb.AdaptivePictures.Order());
        Assert.Equal(blorb.AdaptivePictures, BlorbPictures.From(blorb).Adaptive);

        // [blorb 11.3] Journey and Shogun carry the chunk with nothing
        // in it, and a file without the chunk at all says the same
        // thing: no picture here takes another's colors.
        var plain = BlorbFile.Read(TestBlorb.Build([(1, "PNG ", Ordinary())]));
        Assert.Empty(plain.AdaptivePictures);
        Assert.False(new AdaptivePalette(plain.AdaptivePictures).Adapts);
        Assert.True(new AdaptivePalette(blorb.AdaptivePictures).Adapts);
    }

    [Fact]
    public void AnAdaptivePictureTakesTheColorsOfTheOneBeforeIt()
    {
        var pictures = BlorbPictures.From(Resources());
        var palette = new AdaptivePalette(pictures.Adaptive);
        var adaptive = pictures.Find(2)!;

        // [blorb 11.3] Nothing has been plotted yet, so there are no
        // colors to take. The specification calls this undefined; the
        // plain answer is to leave the picture with what it carries.
        Assert.Null(palette.Plot(adaptive));

        // An ordinary picture carries the colors and hands them on.
        Assert.Null(palette.Plot(pictures.Find(1)!));

        var handed = palette.Plot(adaptive);
        Assert.NotNull(handed);

        // Its own first two entries are kept, since those are the ones
        // the current palette does not cover, and the rest are the
        // scene's.
        Assert.Equal(Placeholder[..6], handed[..6]);
        Assert.Equal(Scene[6..12], handed[6..12]);

        // The same picture under the same colors comes back as the same
        // object, which is what lets a frontend see that a bitmap it
        // has already made is still good.
        Assert.Same(handed, palette.Plot(adaptive));
    }

    [Fact]
    public void AShorterPaletteChangesOnlyTheEntriesItHas()
    {
        var pictures = BlorbPictures.From(Resources());
        var palette = new AdaptivePalette(pictures.Adaptive);

        palette.Plot(pictures.Find(1)!);
        palette.Plot(pictures.Find(3)!);

        // [blorb 11.3] The third picture's palette is three entries
        // long, so it changed the first three of the current palette
        // and left the fourth as the first picture set it.
        var handed = palette.Plot(pictures.Find(2)!)!;

        Assert.Equal(Shorter[6..9], handed[6..9]);
        Assert.Equal(Scene[9..12], handed[9..12]);
    }

    [Fact]
    public void ThePictureComesOutInWhicheverColorsItWasPlottedWith()
    {
        var pictures = BlorbPictures.From(Resources());
        var palette = new AdaptivePalette(pictures.Adaptive);
        var adaptive = pictures.Find(2)!;

        // On its own it is the flat primaries it carries.
        var alone = PictureReader.Decode(adaptive)!;
        Assert.Equal(((byte)0xAA, (byte)0x00, (byte)0x00, (byte)255), alone.At(0, 0));

        // Plotted after the scene, it is the scene's colors, which is
        // the whole point of the chunk.
        palette.Plot(pictures.Find(1)!);
        var taken = PictureReader.Decode(adaptive, palette.Plot(adaptive))!;

        Assert.Equal(((byte)0x20, (byte)0x80, (byte)0x20, (byte)255), taken.At(0, 0));
        Assert.Equal(((byte)0x80, (byte)0x40, (byte)0x20, (byte)255), taken.At(1, 0));
    }

    [Fact]
    public void TheModelSettlesTheColorsWhenThePictureIsPlotted()
    {
        // The order pictures are plotted in is what decides an adaptive
        // one's colors, and the screen model is the only thing that
        // knows that order, so the colors are settled there and carried
        // on the placement to whatever draws it.
        var bytes = new byte[2048];
        bytes[0] = 6;
        bytes[0x04] = 0x04;
        bytes[0x0E] = 0x04;
        var memory = new ZMemory(bytes);
        var screen = new RecordingScreen(
            20,
            10,
            ScreenCapabilities.StatusLine | ScreenCapabilities.UpperWindow | ScreenCapabilities.Pictures);

        var model = new WindowedScreenModel(screen, new StoryHeader(memory), memory);
        model.UsePictures(BlorbPictures.From(Resources()));

        Assert.True(model.DrawPicture(1, 1, 1));
        Assert.True(model.DrawPicture(2, 2, 1));

        Assert.Equal(2, model.Pictures.Count);
        Assert.Null(model.Pictures[0].Palette);
        Assert.NotNull(model.Pictures[1].Palette);
        Assert.Equal(Scene[6..12], model.Pictures[1].Palette![6..12]);
    }

    [Fact]
    public void TheCorpusPicturesThatAdaptComeOutInAnotherPicturesColors()
    {
        var files = Corpus.ResourceFiles();
        Assert.SkipUnless(files.Count > 0, "The entharion submodule is not populated.");

        var adapting = 0;
        var took = 0;

        foreach (var path in files)
        {
            var blorb = BlorbFile.Read(File.ReadAllBytes(path));
            if (blorb.AdaptivePictures.Count == 0)
            {
                continue;
            }

            adapting++;
            var pictures = BlorbPictures.From(blorb);
            var palette = new AdaptivePalette(pictures.Adaptive);

            foreach (var picture in pictures.Pictures)
            {
                // [blorb 11.3] The specification says every picture in
                // a file with this chunk is a placeholder or an indexed
                // PNG. Arthur and Zork Zero both break that: between
                // them they carry gray, true color, and alpha pictures
                // as well, all numbered above a thousand. What does
                // hold is the part that matters, which is that every
                // picture the chunk lists is indexed and has a palette.
                if (!pictures.Adaptive.Contains(picture.Number))
                {
                    palette.Plot(picture);
                    continue;
                }

                Assert.Equal(PictureKind.Png, picture.Kind);
                Assert.NotNull(PngReader.Palette(picture.Data.Span));

                if (palette.Plot(picture) is not { } handed)
                {
                    continue;
                }

                // The colors it was handed are not the flat primaries
                // it carries, which is the whole reason for the chunk.
                var carried = PictureReader.Decode(picture)!;
                var taken = PictureReader.Decode(picture, handed)!;

                Assert.Equal((carried.Width, carried.Height), (taken.Width, taken.Height));
                if (!carried.Rgba.AsSpan().SequenceEqual(taken.Rgba))
                {
                    took++;
                }
            }
        }

        // Arthur lists three pictures and Zork Zero a hundred and
        // seventy-two; Journey and Shogun carry the chunk empty, which
        // says their graphics are of this kind without any of their
        // pictures actually adapting.
        Assert.Equal(2, adapting);
        Assert.True(took > 150, $"Only {took} pictures came out in another's colors.");
    }

    [Fact]
    public void APictureAlreadyOnTheScreenTakesTheColorsOfALaterScene()
    {
        // [blorb 11.3 deviates] The specification would rather a
        // picture kept the colors it was plotted with. Arthur draws its
        // frame and its two side borders once, at the start, and never
        // again, and expects them to take each scene's colors as the
        // game moves from the churchyard into the church, which is the
        // rendering the specification describes as the Amiga's and the
        // IBM's rather than the preferred one.
        var model = Model();

        Assert.True(model.DrawPicture(1, 1, 1));
        Assert.True(model.DrawPicture(2, 5, 1));

        var first = model.Pictures[1].Palette;
        Assert.Equal(Scene[6..12], first![6..12]);

        // A later scene, whose palette is three entries long, and the
        // picture already on the screen follows it.
        Assert.True(model.DrawPicture(3, 9, 1));

        var second = model.Pictures[1].Palette;
        Assert.Equal(Shorter[6..9], second![6..9]);
        Assert.NotSame(first, second);
    }

    /// <summary>
    /// A Version 6 screen with the three pictures on it.
    /// </summary>
    private static WindowedScreenModel Model()
    {
        var bytes = new byte[2048];
        bytes[0] = 6;
        bytes[0x04] = 0x04;
        bytes[0x0E] = 0x04;
        var memory = new ZMemory(bytes);
        var screen = new RecordingScreen(
            20,
            10,
            ScreenCapabilities.StatusLine | ScreenCapabilities.UpperWindow | ScreenCapabilities.Pictures);

        var model = new WindowedScreenModel(screen, new StoryHeader(memory), memory);
        model.UsePictures(BlorbPictures.From(Resources()));
        return model;
    }

    /// <summary>
    /// A resource file of three pictures: one carrying a scene's
    /// colors, one that takes them, and one carrying a shorter palette.
    /// </summary>
    private static BlorbFile Resources() => BlorbFile.Read(TestBlorb.Build(
        [
            (1, "PNG ", Ordinary()),
            (2, "PNG ", Adaptive()),
            (3, "PNG ", TestPng.Indexed(2, 1, Shorter, [[2, 2]])),
        ],
        adaptive: [2]));

    /// <summary>
    /// An ordinary picture, whose palette is the one that gets handed
    /// on.
    /// </summary>
    private static byte[] Ordinary() => TestPng.Indexed(2, 1, Scene, [[2, 3]]);

    /// <summary>
    /// An adaptive picture, whose two pixels are the palette entries
    /// the current colors cover.
    /// </summary>
    private static byte[] Adaptive() => TestPng.Indexed(2, 1, Placeholder, [[2, 3]]);
}

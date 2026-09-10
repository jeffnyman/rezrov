using Rezrov.Core.Blorb;

namespace Rezrov.Tests;

/// <summary>
/// The picture catalog: sizes read from each format's header, and the
/// resolution chunk's scaling rules.
/// </summary>
public class BlorbPicturesTests
{
    [Fact]
    public void MeasuresEachPictureFormat()
    {
        var blorb = BlorbFile.Read(TestBlorb.Build(
            [(1, "PNG ", TestBlorb.Png(40, 30)), (2, "JPEG", TestBlorb.Jpeg(20, 10)), (3, "Rect", TestBlorb.Rect(8, 6))],
            release: 5));

        var pictures = BlorbPictures.From(blorb);

        // [blorb 2] A PNG, a JPEG, and a placeholder rectangle, each
        // measured from its own header.
        Assert.Equal(3, pictures.Count);
        Assert.Equal(5, pictures.Release);
        Assert.Equal((PictureKind.Png, 40, 30), (pictures.Find(1)!.Kind, pictures.Find(1)!.Width, pictures.Find(1)!.Height));
        Assert.Equal((PictureKind.Jpeg, 20, 10), (pictures.Find(2)!.Kind, pictures.Find(2)!.Width, pictures.Find(2)!.Height));
        Assert.Equal((PictureKind.Rectangle, 8, 6), (pictures.Find(3)!.Kind, pictures.Find(3)!.Width, pictures.Find(3)!.Height));
        Assert.Null(pictures.Find(4));

        // [blorb 11.2] Without a resolution chunk nothing scales.
        Assert.Null(pictures.StandardWindow);
        Assert.Equal((40, 30), pictures.ScaledSize(1, 999, 999));
        Assert.Null(pictures.ScaledSize(4, 999, 999));
    }

    [Fact]
    public void ScalesByTheResolutionChunk()
    {
        var resolution = TestBlorb.Resolution(320, 200, (1, 1, 1, 0, 0, 0, 0), (2, 1, 1, 2, 1, 2, 1));
        var blorb = BlorbFile.Read(TestBlorb.Build(
            [(1, "PNG ", TestBlorb.Png(60, 40)), (2, "PNG ", TestBlorb.Png(20, 20)), (3, "PNG ", TestBlorb.Png(10, 10))],
            resolution));

        var pictures = BlorbPictures.From(blorb);

        // [blorb 11.2] The elbow room factor is the smaller of the two
        // ratios of screen to standard window; picture 1 scales freely
        // by it, picture 2 is fixed at double size, and picture 3 has
        // no entry and never scales.
        Assert.Equal((320, 200), pictures.StandardWindow);
        Assert.Equal((120, 80), pictures.ScaledSize(1, 640, 400));
        Assert.Equal((30, 20), pictures.ScaledSize(1, 160, 100));
        Assert.Equal((30, 20), pictures.ScaledSize(1, 640, 100));
        Assert.Equal((40, 40), pictures.ScaledSize(2, 160, 100));
        Assert.Equal((10, 10), pictures.ScaledSize(3, 640, 400));
    }

    [Fact]
    public void RejectsAChunkThatIsNotAPicture()
    {
        var blorb = BlorbFile.Read(TestBlorb.Build([(1, "TEXT", [1, 2, 3, 4])]));

        var e = Assert.Throws<InvalidDataException>(() => BlorbPictures.From(blorb));

        Assert.Contains("not a picture format", e.Message, StringComparison.Ordinal);
    }
}

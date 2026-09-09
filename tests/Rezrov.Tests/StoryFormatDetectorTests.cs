using Rezrov.Core;

namespace Rezrov.Tests;

/// <summary>
/// These build their input by hand rather than reading anything out of the
/// entharion submodule, because that submodule is optional and the tests
/// have to pass without it.
/// </summary>
public class StoryFormatDetectorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void RecognizesEveryZMachineVersion(byte version)
    {
        // [zm 11.1] Byte $00 of the header holds the version number.
        var story = new byte[] { version, 0, 0, 0 };

        Assert.Equal(StoryFormat.ZMachine, StoryFormatDetector.Detect(story));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(255)]
    public void RejectsVersionBytesOutsideTheDefinedRange(byte version)
    {
        var story = new byte[] { version, 0, 0, 0 };

        Assert.Equal(StoryFormat.Unknown, StoryFormatDetector.Detect(story));
    }

    [Fact]
    public void RecognizesGlulxByItsMagicNumber()
    {
        // [glulx #the-header] Magic number 47 6C 75 6C, ASCII 'Glul'.
        var story = new byte[] { 0x47, 0x6C, 0x75, 0x6C, 0, 0, 3, 1 };

        Assert.Equal(StoryFormat.Glulx, StoryFormatDetector.Detect(story));
    }

    [Fact]
    public void RecognizesBlorbByItsFormType()
    {
        // [blorb #overall-structure] 'FORM', a length, then the 'IFRS' type.
        var story = new byte[]
        {
            (byte)'F', (byte)'O', (byte)'R', (byte)'M',
            0, 0, 0, 32,
            (byte)'I', (byte)'F', (byte)'R', (byte)'S',
        };

        Assert.Equal(StoryFormat.Blorb, StoryFormatDetector.Detect(story));
    }

    [Fact]
    public void PrefersBlorbOverTheFileItWraps()
    {
        // An IFF FORM begins with 'F', which is 0x46 and so outside the
        // version range, but the ordering inside the detector is what
        // guarantees this rather than luck. Worth pinning down.
        var story = new byte[]
        {
            (byte)'F', (byte)'O', (byte)'R', (byte)'M',
            0, 0, 0, 32,
            (byte)'I', (byte)'F', (byte)'R', (byte)'S',
            3, 0, 0, 0,
        };

        Assert.Equal(StoryFormat.Blorb, StoryFormatDetector.Detect(story));
    }

    [Fact]
    public void ReturnsUnknownForAnEmptyFile()
    {
        Assert.Equal(StoryFormat.Unknown, StoryFormatDetector.Detect([]));
    }

    [Fact]
    public void ReturnsUnknownWhenTooShortToBeAnythingButZCode()
    {
        // Four bytes of 'Glul' would be Glulx, but three cannot be.
        var story = new byte[] { 0x47, 0x6C, 0x75 };

        Assert.Equal(StoryFormat.Unknown, StoryFormatDetector.Detect(story));
    }
}

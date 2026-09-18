using System.Text;
using Rezrov.ZMachine;

namespace Rezrov.Tests;

/// <summary>
/// Which stories an interpreter shows a title picture for, which is
/// Beyond Zork and nothing else.
/// </summary>
public class TitleScreenTests
{
    // A header with nothing in it but a version, a release, and a
    // serial number, which is all the recognition looks at.
    private static StoryHeader Header(ZMachineVersion version, ushort release, string serial)
    {
        var bytes = new byte[128];
        bytes[0x00] = (byte)version;
        bytes[0x02] = (byte)(release >> 8);
        bytes[0x03] = (byte)release;
        bytes[0x04] = 0x00; bytes[0x05] = 0x40;
        bytes[0x0E] = 0x00; bytes[0x0F] = 0x40;
        Encoding.ASCII.GetBytes(serial).CopyTo(bytes.AsSpan(0x12));
        return new StoryHeader(new ZMemory(bytes));
    }

    [Theory]
    [InlineData(1, "870412")]
    [InlineData(1, "870715")]
    [InlineData(47, "870915")]
    [InlineData(49, "870917")]
    [InlineData(51, "870923")]
    [InlineData(57, "871221")]
    [InlineData(60, "880610")]
    public void EveryReleaseOfBeyondZorkHasATitlePicture(int release, string serial)
    {
        // [blorb 2] The first picture of the resource file, which is the
        // only one the game carries.
        Assert.Equal(1, TitleScreen.Picture(Header(ZMachineVersion.V5, (ushort)release, serial)));
    }

    [Fact]
    public void NoOtherStoryHasOne()
    {
        // Sherlock and Border Zone, both Version 5 and both of the
        // same couple of years, and two releases of Beyond Zork that
        // were never made.
        Assert.Equal(0, TitleScreen.Picture(Header(ZMachineVersion.V5, 26, "880127")));
        Assert.Equal(0, TitleScreen.Picture(Header(ZMachineVersion.V5, 9, "871008")));
        Assert.Equal(0, TitleScreen.Picture(Header(ZMachineVersion.V5, 57, "871222")));
        Assert.Equal(0, TitleScreen.Picture(Header(ZMachineVersion.V5, 58, "871221")));
    }

    [Fact]
    public void AnotherVersionsReleaseOneIsNotMistakenForIt()
    {
        // Release one with an eighty-seven serial is a thing any number
        // of games could be, so the version has to agree as well.
        Assert.Equal(1, TitleScreen.Picture(Header(ZMachineVersion.V5, 1, "870412")));
        Assert.Equal(0, TitleScreen.Picture(Header(ZMachineVersion.V3, 1, "870412")));
        Assert.Equal(0, TitleScreen.Picture(Header(ZMachineVersion.V4, 1, "870412")));
        Assert.Equal(0, TitleScreen.Picture(Header(ZMachineVersion.V6, 1, "870412")));
    }
}

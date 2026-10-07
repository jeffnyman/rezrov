using Rezrov.Gui;
using Rezrov.ZMachine.Screen;

namespace Rezrov.Tests;

/// <summary>
/// The colors a player chooses for text and the page behind it.
/// </summary>
public class ColorSchemesTests
{
    [Theory]
    [InlineData("#1A1A1A", 0x1A1A1Au)]
    [InlineData("1a1a1a", 0x1A1A1Au)]
    [InlineData("  #F4ECD8 ", 0xF4ECD8u)]
    public void AColorIsAHashAndSixHexDigitsOrTheDigitsAlone(string text, uint color)
    {
        Assert.Equal(color, ColorSchemes.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#FFF")]
    [InlineData("#1A1A1A1A")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void AnythingElseIsNoColor(string? text)
    {
        Assert.Null(ColorSchemes.Parse(text));
    }

    [Fact]
    public void AColorIsWrittenAsItWouldBeTyped()
    {
        Assert.Equal("#05A0FF", ColorSchemes.Written(0x05A0FF));
        Assert.Equal(0x05A0FFu, ColorSchemes.Parse(ColorSchemes.Written(0x05A0FF)));
    }

    [Fact]
    public void TheListNamesTheSchemeTheColorsAre()
    {
        Assert.Equal(ColorSchemes.GameOwn, ColorSchemes.NameOf(null, null));

        foreach (var scheme in ColorSchemes.Named)
        {
            Assert.Equal(scheme.Name, ColorSchemes.NameOf(scheme.Ink, scheme.Paper));
        }

        Assert.Equal(ColorSchemes.Custom, ColorSchemes.NameOf(0x123456, 0x654321));
        Assert.Equal(ColorSchemes.Custom, ColorSchemes.NameOf(0x1A1A1A, null));
    }

    [Fact]
    public void ADarkColorIsOneThatLooksDark()
    {
        Assert.True(ColorSchemes.IsDark(0x1E1E1E));
        Assert.True(ColorSchemes.IsDark(0x0000FF));
        Assert.False(ColorSchemes.IsDark(0xF4ECD8));
        Assert.False(ColorSchemes.IsDark(0x00FF00));
    }

    [Fact]
    public void AZMachineGameWithNoColorsChosenIsToldWhiteOnBlack()
    {
        // [zm 8.3.3] What the graphical program has always told it.
        Assert.Equal((ScreenColor.White, ScreenColor.Black), ColorSchemes.Standard(null, null));
    }

    [Fact]
    public void AZMachineGameIsToldTheNearestStandardColors()
    {
        Assert.Equal((ScreenColor.Black, ScreenColor.White), ColorSchemes.Standard(0x1A1A1A, 0xFAFAF7));
        Assert.Equal((ScreenColor.White, ScreenColor.Black), ColorSchemes.Standard(0xD8D8D8, 0x1E1E1E));
        Assert.Equal(ScreenColor.White, ColorSchemes.Standard(0x5B4636, 0xF4ECD8).Paper);
    }

    [Fact]
    public void TheTwoDefaultsAreNeverTheSameNumber()
    {
        // A game told its text and its page are one color would think
        // its text invisible, whatever the window actually draws.
        foreach (var ink in new uint?[] { null, 0x000000, 0x202020, 0xFFFFFF, 0xF0F0F0 })
        {
            foreach (var paper in new uint?[] { null, 0x000000, 0x202020, 0xFFFFFF, 0xF0F0F0 })
            {
                var (text, page) = ColorSchemes.Standard(ink, paper);

                Assert.NotEqual(text, page);
                Assert.True(ScreenColors.IsActualColor(text) && ScreenColors.IsActualColor(page));
            }
        }
    }
}

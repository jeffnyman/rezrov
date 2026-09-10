using Rezrov.ZMachine;
using Rezrov.ZMachine.Screen;
using static Rezrov.Tests.Assembler;

namespace Rezrov.Tests;

/// <summary>
/// [zm 16] Font 3 as the nearest Unicode characters, and how a game
/// gets it.
/// </summary>
public class CharacterGraphicsTests
{
    [Theory]
    [InlineData('(', '│')]
    [InlineData('&', '─')]
    [InlineData('/', '┌')]
    [InlineData('1', '┘')]
    [InlineData('[', '┼')]
    [InlineData('6', '█')]
    [InlineData('9', '▌')]
    [InlineData('"', '→')]
    [InlineData('\\', '↑')]
    [InlineData('a', 'ᚪ')]
    [InlineData('z', 'ᛟ')]
    [InlineData(' ', ' ')]
    public void EachFont3CharacterHasANearestUnicodeShape(char code, char shape)
    {
        // [zm 16.1] Lines, corners, blocks, arrows, and runes.
        Assert.Equal(shape, CharacterGraphics.ToUnicode(code));
    }

    [Fact]
    public void CodesOutsideTheFontComeBackAsTheyAre()
    {
        Assert.Equal('é', CharacterGraphics.ToUnicode('é'));
        Assert.Equal('', CharacterGraphics.ToUnicode(''));
    }
}

public partial class InterpreterTests
{
    [Fact]
    public void Font3IsOfferedWhereTheFrontendCanShowItAndDrawnAsUnicode()
    {
        var screen = new RecordingScreen(20, 5, ScreenCapabilities.FixedGrid | ScreenCapabilities.CharacterGraphicsFont);
        var offered = Execute(
            new Assembler()
                .Ext(4, Small(3)).Store(G0)
                .Variable(Op.PrintChar, true, Small('('))
                .Ext(4, Small(1)).Store(G1)
                .Variable(Op.PrintChar, true, Small('('))
                .Quit(),
            screen: screen);

        // [zm op:set_font] The previous font comes back, the text carries
        // the font, and [zm 8.1.5.1] the header keeps its bit 3 in
        // Version 5.
        Assert.Equal(1, offered.Global(G0));
        Assert.Equal(3, offered.Global(G1));
        Assert.Equal([3, 1], screen.Runs.Select(r => r.Attributes.Font));

        var writer = new StringWriter();
        Execute(
            new Assembler()
                .Ext(4, Small(3)).Store(G0)
                .Variable(Op.PrintChar, true, Small('('))
                .Variable(Op.PrintChar, true, Small('/'))
                .Quit(),
            screen: new TextWriterScreen(writer));

        // A text stream shows the font as the nearest Unicode shapes.
        Assert.Equal("│┌", writer.ToString());
    }

    [Fact]
    public void Font3IsRefusedWhereTheFrontendCannotShowIt()
    {
        var screen = new RecordingScreen(20, 5, ScreenCapabilities.FixedGrid);
        var run = Execute(
            new Assembler()
                .Ext(4, Small(3)).Store(G0)
                .Variable(Op.PrintChar, true, Small('('))
                .Quit(),
            setup: story => story.PutWord(0x10, 0x0008),
            screen: screen);

        // [zm op:set_font] 0 for an unavailable font, the text stays in
        // font 1, and [zm 8.1.5.1] the header's bit 3 is cleared.
        Assert.Equal(0, run.Global(G0));
        Assert.Equal(1, screen.Runs.Single().Attributes.Font);
        Assert.False(run.Interpreter.Header.Flags2.HasFlag(Flags2.WantsPictures));
    }
}

using Rezrov.Core.Acceptance;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Screen;
using static Rezrov.Tests.Assembler;

namespace Rezrov.Tests;

/// <summary>
/// [zm 11.1.3] The interpreter number: how a person names a machine,
/// and what the choice does.
/// </summary>
public class InterpreterNumberTests
{
    [Theory]
    [InlineData("amiga", InterpreterNumber.Amiga)]
    [InlineData("IBMPC", InterpreterNumber.IbmPc)]
    [InlineData(" dec20 ", InterpreterNumber.DecSystem20)]
    [InlineData("6", InterpreterNumber.IbmPc)]
    [InlineData("11", InterpreterNumber.TandyColor)]
    public void AMachineCanBeNamedOrNumbered(string text, InterpreterNumber expected)
    {
        Assert.True(InterpreterNumbers.TryParse(text, out var number));
        Assert.Equal(expected, number);
    }

    [Theory]
    [InlineData("banana")]
    [InlineData("0")]
    [InlineData("12")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElseIsNotAMachine(string? text)
    {
        Assert.False(InterpreterNumbers.TryParse(text, out _));
    }

    [Fact]
    public void EveryNumberHasANameAndTheListIsInOrder()
    {
        Assert.Equal("amiga", InterpreterNumbers.Name(InterpreterNumber.Amiga));
        Assert.Equal("0", InterpreterNumbers.Name(InterpreterNumber.Unspecified));
        Assert.Equal(11, InterpreterNumbers.AllNames.Count);
        Assert.Equal("dec20", InterpreterNumbers.AllNames[0]);
    }

    [Fact]
    public void AScriptCanNameTheMachine()
    {
        var script = AcceptanceScript.Parse("! SEED=1\n! GAME=zork1.z3\n! INTERPRETER=amiga\nlook\n", Path.Combine(Path.GetTempPath(), "t.accept"));

        Assert.Equal("amiga", script.Interpreter);
        Assert.False(script.Tandy);
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("ON", true)]
    [InlineData("1", true)]
    [InlineData("no", false)]
    [InlineData("false", false)]
    public void AScriptCanAskForTheTandyBit(string value, bool expected)
    {
        var script = AcceptanceScript.Parse($"! SEED=1\n! GAME=zork1.z3\n! TANDY={value}\nlook\n", Path.Combine(Path.GetTempPath(), "t.accept"));

        Assert.Equal(expected, script.Tandy);
    }

    [Fact]
    public void ATandyDirectiveMustBeYesOrNo()
    {
        var e = Assert.Throws<InvalidDataException>(() => AcceptanceScript.Parse("! SEED=1\n! GAME=zork1.z3\n! TANDY=maybe\nlook\n", Path.Combine(Path.GetTempPath(), "t.accept")));

        Assert.Contains("line 3: TANDY must be yes or no", e.Message, StringComparison.Ordinal);
    }
}

public partial class InterpreterTests
{
    [Fact]
    public void TheTandyBitIsSetOnlyWhenAskedAndOnlyBeforeVersion4()
    {
        var plain = Execute(new Assembler().Quit(), version: ZMachineVersion.V3);
        var tandy = Execute(new Assembler().Quit(), version: ZMachineVersion.V3, tandy: true);

        // [zm 11.1] Bit 3 of Flags 1 in Versions 1 to 3 is the Tandy
        // bit, set at the player's request and otherwise cleared.
        Assert.False(plain.Interpreter.Header.Flags1Versions1To3.HasFlag(Flags1Versions1To3.Tandy));
        Assert.True(tandy.Interpreter.Header.Flags1Versions1To3.HasFlag(Flags1Versions1To3.Tandy));

        // From Version 4 the bit means something else, and the request
        // changes nothing.
        var later = Execute(new Assembler().Quit(), version: ZMachineVersion.V5);
        var laterTandy = Execute(new Assembler().Quit(), version: ZMachineVersion.V5, tandy: true);
        Assert.Equal(later.Interpreter.Header.Flags1FromVersion4, laterTandy.Interpreter.Header.Flags1FromVersion4);
    }
}

public partial class InterpreterTests
{
    [Fact]
    public void TheChosenMachineGoesIntoTheHeader()
    {
        var run = Execute(new Assembler().Quit(), interpreterNumber: InterpreterNumber.Macintosh);

        // [zm 11.1.3] The number the game reads is the one asked for.
        Assert.Equal(InterpreterNumber.Macintosh, run.Interpreter.Header.InterpreterNumber);
        Assert.Equal(InterpreterNumber.Macintosh, run.Interpreter.InterpreterNumber);
    }

    [Fact]
    public void UnderTheAmigaNumberAnInfocomVersion6GameSharesOnePairOfColors()
    {
        var run = RunVersion6(
            new Assembler()
                .Short0(Op.Print).Text("ab")
                .Variable(Op.SetColour, false, Small(3), Small(4), Small(2))
                .Quit(),
            setup: story => "890714"u8.CopyTo(story.Bytes.AsSpan(0x12)),
            interpreterNumber: InterpreterNumber.Amiga);

        var windows = run.Interpreter.Windows!;

        // [zm 8.3] Colors set for window 2 become every window's, and
        // the text already on the screen changes to match.
        Assert.True(windows.SharedColors);
        Assert.All(windows.Windows, w => Assert.Equal((ScreenColor.Red, ScreenColor.Green), (w.Foreground, w.Background)));
        Assert.Equal((ScreenColor.Red, ScreenColor.Green), (windows[0, 0].Attributes.Foreground, windows[0, 0].Attributes.Background));
        Assert.Equal('a', windows[0, 0].Character);
    }

    [Fact]
    public void UnderTheAmigaNumberOtherGamesKeepTheirOwnColors()
    {
        var run = RunVersion6(
            new Assembler().Variable(Op.SetColour, false, Small(3), Small(4), Small(2)).Quit(),
            setup: story => "051020"u8.CopyTo(story.Bytes.AsSpan(0x12)),
            interpreterNumber: InterpreterNumber.Amiga);

        var windows = run.Interpreter.Windows!;

        // [zm 8.3] The rule is for Infocom's games only.
        Assert.False(windows.SharedColors);
        Assert.Equal((ScreenColor.Red, ScreenColor.Green), (windows.Windows[2].Foreground, windows.Windows[2].Background));
        Assert.NotEqual(ScreenColor.Red, windows.Windows[0].Foreground);
    }
}

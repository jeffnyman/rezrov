using Rezrov.Core.Blorb;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Instructions;
using Rezrov.ZMachine.Screen;
using static Rezrov.Tests.Assembler;

namespace Rezrov.Tests;

/// <summary>
/// arc_image, the picture band an Arcturus story draws across the top
/// of an ordinary Version 5 screen, and the declaration in its resource
/// file that is the only thing telling such a story from any other.
/// </summary>
public partial class InterpreterTests
{
    private const int DrawImage = 128;

    private static ScreenCapabilities WithBand =>
        ScreenCapabilities.StatusLine | ScreenCapabilities.UpperWindow
        | ScreenCapabilities.FixedGrid | ScreenCapabilities.PictureBand;

    [Fact]
    public void TheDeclarationChunkSaysWhichBandThePicturesWerePaintedFor()
    {
        var declared = BlorbFile.Read(TestBlorb.Build([(1, "PNG ", TestBlorb.Png(320, 96))], arcImage: (1, 12)));
        var ordinary = BlorbFile.Read(TestBlorb.Build([(1, "PNG ", TestBlorb.Png(4, 4))]));

        // [arc blorb] Two bytes, the extension version and then the
        // mode. A Blorb without the chunk holds no arc_image pictures
        // and is played exactly as it always was.
        Assert.Equal(new ArcImageDeclaration(1, 12), declared.ArcImage);
        Assert.Null(ordinary.ArcImage);
    }

    [Theory]
    [InlineData(ZMachineVersion.V5)]
    [InlineData(ZMachineVersion.V7)]
    [InlineData(ZMachineVersion.V8)]
    public void DrawImageIsDecodedOutsideVersionSix(ZMachineVersion version)
    {
        // [arc opcode] EXT:0x80, in the range [zm 14.2.2] leaves to
        // private use, with two operands and neither store nor branch.
        var info = OpcodeTable.Resolve(InstructionForm.ExtendedForm, OperandCount.Var, DrawImage, version);

        Assert.NotNull(info);
        Assert.Equal(Opcode.DrawImage, info.Opcode);
        Assert.Equal("draw_image", info.Name);
        Assert.False(info.Store);
        Assert.False(info.Branch);
    }

    [Fact]
    public void VersionSixKeepsTheOpcodeByteToItself()
    {
        // Version 6 has picture opcodes of its own and the extension is
        // defined for Versions 5, 7, and 8 only, so there the byte is
        // one of the unknown ones [zm 14.2.1] says to ignore.
        var info = OpcodeTable.Resolve(InstructionForm.ExtendedForm, OperandCount.Var, DrawImage, ZMachineVersion.V6);

        Assert.NotNull(info);
        Assert.Equal(Opcode.Unknown, info.Opcode);
    }

    [Fact]
    public void TheCapabilityBitIsSetOnlyWhenTheResourceFileDeclaresAPack()
    {
        var plain = Booted(WithBand, arcImage: null);
        var declared = Booted(WithBand, arcImage: (1, 9));
        var without = Booted(ScreenCapabilities.FixedGrid, arcImage: (1, 9));

        // [arc contract 1] The bit is the story's gate: it reads it and
        // never draws when it is clear. A resource file that says
        // nothing about arc_image leaves the bit alone, and so does a
        // build with nowhere to put a band.
        Assert.False(Advertises(plain));
        Assert.True(Advertises(declared));
        Assert.False(Advertises(without));
    }

    [Fact]
    public void AnInformStoryKeepsTheBitForItsOwnMeaning()
    {
        // [zm 11.1] Inform's Version 4 and later games carry the
        // Version 3 meaning of this bit over, using it to choose
        // between a time and a score status line, and an interpreter is
        // told to leave it alone there. So a story that asked for a
        // time status line still has one on a screen with a band, and a
        // score game is not quietly turned into a clock.
        var time = new Story(ZMachineVersion.V5);
        time.Bytes[0x01] |= (byte)Flags1FromVersion4.PicturesAvailable;

        Assert.True(Advertises(Booted(WithBand, arcImage: null, time)));
        Assert.False(Advertises(Booted(WithBand, arcImage: null, new Story(ZMachineVersion.V5))));
    }

    [Fact]
    public void ADrawReachesTheScreenWithItsPictureAndItsMode()
    {
        var screen = new RecordingScreen(60, 20, WithBand);
        var run = Execute(
            new Assembler()
                .Ext(DrawImage, Small(8), Small(12))
                .Ext(DrawImage, Large(21), Small(12))
                .Ext(DrawImage, Small(0), Small(12))
                .Quit(),
            screen: screen,
            before: interpreter => interpreter.UseResources(Pack((1, 12))));

        // [arc contract 2] The picture and the band's height in rows.
        // The mode arrives on every call, a clear included, so a clear
        // still says what shape the band is; and id 0 means take the
        // band down.
        Assert.Equal([(8, 12), (21, 12), (0, 12)], screen.Bands);
        Assert.Empty(run.Interpreter.RuntimeErrors);
    }

    [Fact]
    public void ADrawIsSilentWhereThereIsNoBand()
    {
        var screen = new RecordingScreen(60, 20, ScreenCapabilities.FixedGrid);
        var run = Execute(
            new Assembler().Ext(DrawImage, Small(8), Small(9)).Quit(),
            screen: screen,
            before: interpreter => interpreter.UseResources(Pack((1, 9))));

        // [arc contract 2] A story should never arrive here, having
        // read a clear capability bit, but a saved game restored on
        // another screen arrives sure of itself. Nothing is drawn, and
        // nothing is complained about.
        Assert.Empty(screen.Bands);
        Assert.Empty(run.Interpreter.RuntimeErrors);
    }

    [Fact]
    public void TheBandTakesItsRowsOffTheTopOfTheScreen()
    {
        var screen = Banded(60, 24);
        var run = Execute(
            new Assembler()
                .Ext(DrawImage, Small(8), Small(9))
                .Quit(),
            version: ZMachineVersion.V5,
            screen: screen,
            before: interpreter => interpreter.UseResources(Pack((1, 9))));

        // [arc contract 3] Nine rows for the Arthur band, and what is
        // left is the screen the story is told it has.
        Assert.Equal(9, screen.Buffer.BandRows);
        Assert.Equal(15, run.Interpreter.Display.Height);
        Assert.Equal(15, run.Interpreter.Header.ScreenHeightLines);
        Assert.Equal(15, run.Interpreter.Header.ScreenHeightUnits);
    }

    [Fact]
    public void ClearingTheBandGivesTheRowsBackToTheText()
    {
        var screen = Banded(60, 24);
        var run = Execute(
            new Assembler()
                .Ext(DrawImage, Small(8), Small(12))
                .Ext(DrawImage, Small(0), Small(12))
                .Quit(),
            screen: screen,
            before: interpreter => interpreter.UseResources(Pack((1, 12))));

        // [arc contract 3] The releasing choice: id 0 takes the picture
        // down and hands its rows back, rather than keeping a blank
        // strip reserved. The next picture re-bases the screen again.
        Assert.Equal(0, screen.Buffer.BandRows);
        Assert.Equal(0, screen.BandPicture);
        Assert.Equal(24, run.Interpreter.Display.Height);
        Assert.Equal(24, run.Interpreter.Header.ScreenHeightLines);
    }

    [Fact]
    public void TheStatusLineAndUpperWindowSitBelowTheBand()
    {
        var screen = Banded(40, 20);
        var model = Model(screen, ZMachineVersion.V3);

        model.DrawImageBand(8, 9);
        model.ShowStatusLine("Churchyard", timeGame: false, 0, 0);
        model.SplitWindow(2);
        screen.UpdateUpperWindow(model);

        // [arc contract 3] The whole text screen lives strictly below
        // the band: nine rows of picture, then the status line, then
        // the upper window, and the lower window under all of it.
        Assert.Equal(9, screen.Buffer.BandRows);
        Assert.Equal(1, screen.Buffer.StatusRows);
        Assert.Equal(2, screen.Buffer.UpperLines);
        Assert.Equal(12, screen.Buffer.LowerTop);
    }

    [Fact]
    public void ReleasingTheBandLeavesNoStatusLineStrandedBehindIt()
    {
        var screen = Banded(40, 20);
        var model = Model(screen, ZMachineVersion.V3);

        model.DrawImageBand(8, 9);
        model.ShowStatusLine("Churchyard", timeGame: false, 0, 0);
        screen.UpdateUpperWindow(model);
        Assert.Contains("Churchyard", screen.Buffer.RowText(9));

        model.DrawImageBand(0, 9);
        model.ShowStatusLine("Open Lawn", timeGame: false, 0, 0);
        screen.UpdateUpperWindow(model);

        // [arc contract 3] Releasing the band moves the status line up
        // the screen with it. The row it used to be on belongs to the
        // lower window now, and what it painted there has to go, or the
        // player is left looking at two status lines with a hole
        // between them.
        Assert.Contains("Open Lawn", screen.Buffer.RowText(0));
        Assert.DoesNotContain("Churchyard", screen.Buffer.RowText(9));
    }

    [Fact]
    public void ARebaseThatWouldCoverUnreadTextPausesFirst()
    {
        var screen = new RecordingScreen(40, 20, WithBand);
        var model = Model(screen, ZMachineVersion.V5);

        for (var line = 0; line < 16; line++)
        {
            model.Print('x');
            model.NewLine();
        }

        model.Flush();
        var before = screen.MorePrompts;
        model.DrawImageBand(8, 12);

        // [arc contract 3] The re-base never eats a line. Sixteen lines
        // have gone by unread and only eight rows are left below a
        // band of twelve, so the player is given the chance to read
        // them before any of it is covered.
        Assert.Equal(before + 1, screen.MorePrompts);
    }

    [Fact]
    public void ARebaseWithRoomBelowItPausesForNothing()
    {
        var screen = new RecordingScreen(40, 20, WithBand);
        var model = Model(screen, ZMachineVersion.V5);

        model.Print('x');
        model.NewLine();
        model.Flush();

        var before = screen.MorePrompts;
        model.DrawImageBand(8, 9);

        // [arc contract 3] An intro that fits below the band boots as
        // one composition, picture above and all its text below, with
        // no pause anywhere in it.
        Assert.Equal(before, screen.MorePrompts);
    }

    [Fact]
    public void ARestartTakesTheBandDown()
    {
        var screen = Banded(60, 24);
        var model = Model(screen, ZMachineVersion.V5);

        model.DrawImageBand(8, 9);
        Assert.Equal(9, screen.Buffer.BandRows);

        model.Reset();

        // [arc contract 1] A restart is the screen as at the start of a
        // game. The story is told again that pictures are available and
        // draws its first room's scene afresh.
        Assert.Equal(0, screen.Buffer.BandRows);
        Assert.Equal(24, model.Height);
    }

    [Fact]
    public void TheRabensteinWalkthroughDrawsTheBandItsAuthorSaysItWill()
    {
        var story = Corpus.ArcturusStory("rabenstein-r1-s260825.z5");
        var pack = Corpus.ArcturusPack("rabenstein-r1-s260825.blorb");

        Assert.SkipUnless(story is not null && pack is not null, "The entharion submodule is not populated.");

        var screen = new RecordingScreen(60, 24, WithBand);
        var interpreter = new Interpreter(
            new ZMemory(File.ReadAllBytes(story)),
            screen,
            new ScriptedInput(
                "north", "north", "north", "south", "south",
                "take lantern", "light lantern", "north", "north",
                "sleep", "sleep", "sleep", "quit", "y"));

        interpreter.UseResources(BlorbFile.Read(File.ReadAllBytes(pack)));
        interpreter.Run();

        // The walkthrough in the story's own source header, which is
        // written for interpreter authors and names the expected band
        // at every step: the path, the churchyard, a room with no
        // picture where the band must clear, the dark bedchamber's own
        // scene, back out and in again with the lantern lit for the
        // reveal, and then SLEEP changing the picture in place.
        //
        // The leading 0 is the library clearing a band that was never
        // there. [arc contract 3] A clear that arrives before any
        // picture was shown reserves nothing and is a no-op, which is
        // why it does not count as the first draw and re-base nothing.
        //
        // [arc contract 2] The mode rides every call, a clear included.
        Assert.Equal("0,8,1,0,21,0,1,0,7,9,7,9", string.Join(",", screen.Bands.Select(b => b.Picture)));
        Assert.All(screen.Bands, band => Assert.Equal(12, band.Mode));
    }

    [Fact]
    public void EveryArcturusPackInTheCorpusDeclaresABandWeKnow()
    {
        var packs = Corpus.ArcturusPacks();

        Assert.SkipUnless(packs.Count > 0, "The entharion submodule is not populated.");

        foreach (var path in packs)
        {
            var blorb = BlorbFile.Read(File.ReadAllBytes(path));
            var name = Path.GetFileName(path);

            // [arc blorb] arcimg writes the declaration into every
            // Blorb it makes, so a pack of Arcturus's own that lacks
            // one would mean this reader, not that tool, is wrong.
            Assert.True(blorb.ArcImage is not null, $"{name} declares no arc_image pack.");
            Assert.Equal(1, blorb.ArcImage.Version);

            // [arc contract 2] Two band shapes exist, the Arthur one of
            // 9 rows and the DAAD one of 12, and 0 where a pack states
            // no preference.
            Assert.True(blorb.ArcImage.Mode is 0 or 9 or 12, $"{name} declares band mode {blorb.ArcImage.Mode}.");

            // [blorb 2] And the pictures themselves are there to show.
            Assert.Contains(blorb.Resources, r => r.Usage == ResourceUsage.Picture);
        }
    }

    private static bool Advertises(Interpreter interpreter) =>
        interpreter.Header.Flags1FromVersion4.HasFlag(Flags1FromVersion4.PicturesAvailable);

    private static ScreenModel Model(IScreen screen, ZMachineVersion version)
    {
        var story = new Story(version);
        var memory = new ZMemory(story.Bytes);

        return new ScreenModel(screen, new StoryHeader(memory), memory);
    }

    private static BufferedScreen Banded(int width, int height) =>
        new(
            width,
            height,
            cursorStartsAtBottom: false,
            repaint: () => { },
            waitForKey: () => 13,
            fontWidth: 1,
            fontHeight: 1,
            capabilities: WithBand);

    private static BlorbFile Pack((int Version, int Mode) arcImage) =>
        BlorbFile.Read(TestBlorb.Build([(1, "PNG ", TestBlorb.Png(320, 96))], arcImage: arcImage));

    private static Interpreter Booted(ScreenCapabilities capabilities, (int Version, int Mode)? arcImage, Story? story = null)
    {
        story ??= new Story(ZMachineVersion.V5);

        var interpreter = new Interpreter(
            new ZMemory(story.Bytes),
            new RecordingScreen(60, 20, capabilities),
            new ScriptedInput());

        interpreter.UseResources(BlorbFile.Read(TestBlorb.Build(
            [(1, "PNG ", TestBlorb.Png(320, 72))],
            arcImage: arcImage)));

        return interpreter;
    }
}

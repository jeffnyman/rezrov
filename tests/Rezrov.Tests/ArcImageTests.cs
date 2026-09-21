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

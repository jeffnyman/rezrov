using Rezrov.AaMachine;
using Rezrov.AaMachine.Execution;
using Rezrov.Gui;

namespace Rezrov.Tests;

/// <summary>
/// [aam output] An Aa-machine story shown in a window: the status area
/// across the top, the pictures the story carries actually drawn, and
/// the words a player can click.
/// </summary>
/// <remarks>
/// The display is driven by a real machine playing a real story, but
/// with no window under it, so what the player would see is read off
/// the page the display lays out. The face is invented, which is what
/// lets the places things land be stated exactly.
/// </remarks>
public class GuiAaDisplayTests
{
    private const double Width = 800;
    private const double Height = 600;

    [Fact]
    public void AWindowDeclinesNothingTheOutputModelAsksFor()
    {
        var story = Story("picture-test.aastory");
        var display = Play(story, []);

        // [aam opcode] What the frontend says here is what the game is
        // told through VM_INFO, and the games take their better paths
        // when they hear it. A terminal has to say no to pictures and
        // to links; a window has no reason to say no to anything.
        Assert.True(display.HasLinks);
        Assert.True(display.HasStyles);
        Assert.True(display.HasColor);
        Assert.True(display.HasAlignment);
    }

    [Fact]
    public void AGameGetsItsStatusAreaAcrossTheTopOfTheWindow()
    {
        var display = Play(Story("picture-test.aastory"), ["look"]);

        // [aam story] The story's status class asks for a line's worth
        // of room, and the status area keeps it whether or not what is
        // in it fills it.
        Assert.True(display.StatusHeight > 0, "the story made no status area");

        var status = display.Status.Lay(display.Column);

        Assert.Contains("Foyer of the Opera House", Words(status), StringComparison.Ordinal);
        Assert.Contains("Score:", Words(status), StringComparison.Ordinal);

        // [aam story] The score is a block set aside at the right edge
        // seventeen characters wide, so it begins seventeen characters
        // from the right rather than after the room name.
        var score = Pieces(status).First(piece => piece.Words.StartsWith("Score", StringComparison.Ordinal));

        Assert.Equal(display.Column - (17 * AaRuler.Size), score.Left);

        // And a line is drawn under the whole of it.
        Assert.True(display.Rule > 0, "nothing sets the status area off from the text");
    }

    [Fact]
    public void ThePictureAStoryCarriesIsDrawnRatherThanDescribed()
    {
        // The story puts its picture in its own banner, so it is
        // drawn before the player has said anything.
        var display = Play(Story("picture-test.aastory"), []);
        var page = display.Main.Lay(display.Column);
        var drawn = page.Lines.SelectMany(line => line.Pictures).ToList();

        Assert.NotEmpty(drawn);

        // [aam opcode] The game asks whether the picture can be shown
        // and says one thing or the other. A frontend that cannot draw
        // is told so and prints the words the story carries instead;
        // this one draws it, so neither appears.
        Assert.DoesNotContain("cannot embed", Words(page), StringComparison.Ordinal);
        Assert.DoesNotContain("[a test image", Words(page), StringComparison.Ordinal);

        var picture = drawn[0];

        Assert.True(picture.Width > 0 && picture.Height > 0);
        Assert.True(
            picture.Width <= display.Column + 0.01,
            $"the picture is {picture.Width} wide in a column of {display.Column}");
    }

    [Fact]
    public void AFileThatIsNotAPictureIsDeclinedRatherThanDrawnWrong()
    {
        // This story carries sheet music as PDFs beside its one
        // picture. Nothing here can draw a PDF, and the honest answer
        // is no, which is what makes the game print the words it
        // carries in its place instead.
        var story = Story("pas-de-deux.aastory");
        var display = Play(story, []);

        var pdfs = Enumerable.Range(0, story.Resources.Count)
            .Where(i => story.Resources[i].Url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(pdfs);
        Assert.All(pdfs, resource => Assert.False(display.CanEmbedResource(resource)));

        var pictures = Enumerable.Range(0, story.Resources.Count)
            .Where(i => story.Resources[i].Url.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(pictures);
        Assert.All(pictures, resource => Assert.True(display.CanEmbedResource(resource)));
    }

    [Fact]
    public void ALinkTypesItsOwnWordsWhenItIsUsed()
    {
        var display = Blank();

        display.Write("You see a ");
        display.EnterSelfLink();
        display.Write("Brass Lantern");
        display.LeaveSelfLink();
        display.Write(" here.");

        // [aam output] A self link types back the words it is made of,
        // which a game prints so a player can name a thing by pointing
        // at it rather than by typing it.
        Assert.Equal("brass lantern", display.Typed(1));
        Assert.True(display.IsLive(1));

        // The words are where they were printed, so the link can be
        // found again by pointing at it.
        var found = display.Main.LinkAt(
            10.5 * AaRuler.Size, AaRuler.LineHeight / 2, display.Column, Height);

        Assert.Equal(1, found);

        // [aam opcode] A story may retire the links it has shown. The
        // words stay where they are and stop being something to click.
        display.ClearLinks();

        Assert.False(display.IsLive(1));
        Assert.Equal(string.Empty, display.Typed(1));
    }

    [Fact]
    public void ALinkThePlayerUsesArrivesAsTheWholeCommand()
    {
        var display = Blank();

        display.EnterLink("take the lantern");
        display.Write("lantern");
        display.LeaveLink();

        // A click is put on the same queue as the keys, as something
        // no key could be, and comes back as a line rather than as a
        // letter.
        display.Enqueue(-1);

        Assert.Equal("take the lantern", display.ReadLine());

        // And it is echoed where the player was typing, so the text
        // reads the same as if they had typed it themselves.
        Assert.Contains("take the lantern", Words(display.Main.Lay(display.Column)), StringComparison.Ordinal);
    }

    [Fact]
    public void AKeyIsWaitedForAndAClickIsNot()
    {
        var display = Blank();

        display.EnterLink("north");
        display.Write("north");
        display.LeaveLink();

        // A story waiting for a keypress is not waiting for a click,
        // so the click is passed over and the key behind it answers.
        display.Enqueue(-1);
        display.Enqueue(' ');

        Assert.Equal(' ', display.ReadKey());
    }

    [Fact]
    public void TheTextIsKeptToAMeasureHoweverWideTheWindowIs()
    {
        var display = Blank();

        // A line of prose across a large screen is hard to read back
        // to, so the column stops at sixty characters and sits in the
        // middle of whatever room there is.
        Assert.Equal(60 * AaRuler.Size, display.Column);
        Assert.Equal((Width - display.Column) / 2, display.Left);

        // [aam opcode] And what the story is told when it asks how
        // wide the page is is that same column, not the window.
        Assert.Equal(60, display.Measure(0));

        // A window too narrow for the measure gives what it has.
        display.Resize(300, Height);

        Assert.Equal(300, display.Column);
        Assert.Equal(0, display.Left);
        Assert.Equal(30, display.Measure(0));
    }

    [Fact]
    public void AStoryThatSetsTheBodyStyleChangesThePageItIsOn()
    {
        // [aam opcode] The one conformance story that sets a body
        // style asks for green on black.
        var story = Conformance("body_not_status");
        var display = Play(story, []);
        var body = Enumerable.Range(0, story.Styles.Count)
            .Single(i => story.Styles.Name(i) == "body");

        display.SetBody(body);

        Assert.Equal(0xFF000000, display.Background);

        // The text that follows takes the color and the slant the body
        // style asked for, which is what a story sets one for.
        display.Write("after");

        var piece = display.Main.Lay(display.Column).Lines[^1].Pieces[^1];

        Assert.Equal(0xFF008000, piece.Look.Ink);
        Assert.True(piece.Look.Italic);
    }

    [Fact]
    public void ATranscriptRecordsWhatTheStoryPrinted()
    {
        var written = new StringWriter();
        var display = Blank(() => written);

        Assert.False(display.IsScripting);
        Assert.True(display.ScriptOn());
        Assert.True(display.IsScripting);

        display.Write("The cave is dark.");
        display.EndParagraph();
        display.Write("You cannot see a thing.");
        display.ScriptOff();

        Assert.Contains("The cave is dark.", written.ToString(), StringComparison.Ordinal);
        Assert.Contains("You cannot see a thing.", written.ToString(), StringComparison.Ordinal);
        Assert.False(display.IsScripting);

        // A player who declines the dialog gets no transcript, and the
        // story is told as much rather than left believing it has one.
        var declined = Blank();

        Assert.False(declined.ScriptOn());
        Assert.False(declined.IsScripting);
    }

    [Fact]
    public void ClearingTheScreenLeavesTheStatusAreaAndTheDivsAlone()
    {
        var story = Story("picture-test.aastory");
        var display = Play(story, ["look"]);

        Assert.True(display.StatusHeight > 0);

        display.Clear();

        Assert.Empty(display.Main.Lay(display.Column).Lines.SelectMany(line => line.Pieces));
        Assert.True(display.StatusHeight > 0, "clearing the screen took the status area with it");

        // [aam opcode] Clearing everything takes the status area too,
        // and the line under it goes with it.
        display.ClearAll();

        Assert.Equal(0, display.StatusHeight);
        Assert.Equal(0, display.Rule);
    }

    /// <summary>
    /// Plays a story on a display with no window under it, feeding it
    /// the lines given and stopping when it runs out of them.
    /// </summary>
    private static GuiAaDisplay Play(AaStory story, string[] input)
    {
        var display = new GuiAaDisplay(story, new AaRuler(), AaRuler.Plain, () => { }, () => null);

        display.Resize(Width, Height);

        var machine = new Machine(story, display, seed: 1);
        var status = machine.Start();
        var fed = 0;

        while (status != AaStatus.Quit && fed < input.Length)
        {
            var line = input[fed++];

            if (status == AaStatus.GetInput)
            {
                foreach (var character in line)
                {
                    display.Enqueue(character);
                }

                display.Enqueue(AaKeys.Return);
                status = machine.ProceedWithInput(display.ReadLine());
            }
            else
            {
                display.Enqueue(line.Length > 0 ? line[0] : AaKeys.Return);
                status = machine.ProceedWithKey(display.ReadKey());
            }
        }

        return display;
    }

    /// <summary>
    /// A display with no story running on it, for the parts that are
    /// the frontend's own rather than any game's.
    /// </summary>
    private static GuiAaDisplay Blank(Func<TextWriter?>? transcripts = null)
    {
        var display = new GuiAaDisplay(
            Story("picture-test.aastory"),
            new AaRuler(),
            AaRuler.Plain,
            () => { },
            transcripts ?? (() => null));

        display.Resize(Width, Height);
        return display;
    }

    private static AaStory Story(string name)
    {
        var path = Corpus.AaStoryFile(name);
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        return AaStory.Read(File.ReadAllBytes(path!));
    }

    private static AaStory Conformance(string name)
    {
        var path = Corpus.AaConformanceFile(name);
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        return AaStory.Read(File.ReadAllBytes(path!));
    }

    private static IEnumerable<AaPiece> Pieces(AaPage page) =>
        page.Lines.SelectMany(line => line.Pieces);

    private static string Words(AaPage page) =>
        string.Join('\n', page.Lines.Select(line => string.Concat(line.Pieces.Select(piece => piece.Words))));
}

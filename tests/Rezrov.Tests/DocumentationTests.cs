using Rezrov.Debugging;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Screen;

namespace Rezrov.Tests;

/// <summary>
/// The examples in the documentation are output the tools really
/// produce, rather than output they produced once.
/// </summary>
/// <remarks>
/// A page of prose about a disassembler is checkable in a way most
/// prose is not: the listings in it either come out of the
/// disassembler or they do not. Addresses move when the scan changes
/// its mind about where a routine begins, and the wording of what the
/// debugger says is the sort of thing that gets tidied without anyone
/// thinking about the page it was quoted into. Either way the page
/// starts lying quietly, and a reader has no way of telling.
///
/// What a line is checked against depends on what wrote it. A line a
/// reader types begins with one of the two prompts and is not output
/// at all. Everything else has to be a line the libraries wrote while
/// this test ran, and a stretch of lines with no typing in it has to
/// appear unbroken, so that a block cannot be assembled out of lines
/// that never stood together.
///
/// The games live in the entharion submodule, which CI does not fetch,
/// so this skips without them.
/// </remarks>
public class DocumentationTests
{
    private const string Zork1 = "zork1-r88-s840726.z3";
    private const string Zork0 = "zork0-r393-s890714.z6";

    [Fact]
    public void EveryExampleInTheDebuggingPageIsOutputTheToolsProduce()
    {
        var root = Corpus.FindRepositoryRoot();
        var page = root is null ? null : Path.Combine(root, "docs", "debugging.md");
        var games = root is null ? null : Path.Combine(root, "entharion", "zcode-infocom");

        Assert.SkipUnless(
            page is not null && File.Exists(page)
                && File.Exists(Path.Combine(games!, Zork1))
                && File.Exists(Path.Combine(games!, Zork0)),
            "The entharion submodule is not populated.");

        var produced = Produced(games!);
        var blocks = Examples(File.ReadAllText(page!));

        Assert.NotEmpty(blocks);

        var failures = new List<string>();
        var counted = 0;

        foreach (var (number, block) in blocks.Index())
        {
            foreach (var paragraph in Paragraphs(block))
            {
                var typed = paragraph.Any(Typed);
                var said = paragraph.Where(line => !Typed(line)).ToList();
                counted += said.Count;

                if (said.Count == 0)
                {
                    continue;
                }

                // A paragraph nobody typed into is a stretch the
                // programs wrote in one go, so it has to be there in
                // one piece.
                if (!typed)
                {
                    if (!produced.Contains(string.Join('\n', said), StringComparison.Ordinal))
                    {
                        failures.Add($"example {number}: these lines are not produced together:\n{string.Join('\n', said)}");
                    }

                    continue;
                }

                // A session is the two programs taking turns, so only
                // the lines themselves can be looked for.
                foreach (var line in said)
                {
                    if (!produced.Contains('\n' + line + '\n', StringComparison.Ordinal))
                    {
                        failures.Add($"example {number}: this line is not produced: {line}");
                    }
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of the {counted} lines of example output in docs/debugging.md are not what the "
                + $"tools produce now:\n\n{string.Join("\n\n", failures)}");
    }

    /// <summary>
    /// Whether a reader typed this line rather than read it.
    /// </summary>
    private static bool Typed(string line) =>
        line.StartsWith("(rezrov) ", StringComparison.Ordinal) || line.StartsWith('>');

    /// <summary>The text of every fenced example on the page.</summary>
    private static List<string> Examples(string page)
    {
        var blocks = new List<string>();
        var lines = page.ReplaceLineEndings("\n").Split('\n');
        var open = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            if (open < 0 && lines[i] == "```text")
            {
                open = i + 1;
            }
            else if (open >= 0 && lines[i] == "```")
            {
                blocks.Add(string.Join('\n', lines[open..i]));
                open = -1;
            }
        }

        return blocks;
    }

    /// <summary>
    /// An example split on its blank lines, each piece without them.
    /// </summary>
    private static List<List<string>> Paragraphs(string block) =>
        [.. block.Split("\n\n").Select(piece =>
            piece.Split('\n').Where(line => line.Trim().Length > 0).ToList())];

    /// <summary>
    /// Everything the tools write while reading and debugging the two
    /// games the page is written from.
    /// </summary>
    private static string Produced(string games)
    {
        var said = new StringWriter();
        said.Write('\n');

        var (memory, header) = Read(Path.Combine(games, Zork1));
        MemoryMap.Write(memory, header, said);
        var listing = Disassembly.Of(memory, header);
        listing.Write(said);

        var (zero, zeroHeader) = Read(Path.Combine(games, Zork0));
        MemoryMap.Write(zero, zeroHeader, said);

        // The command line program's own note, which no library writes.
        // Only its two numbers can be checked here, so those are the
        // part taken from the listing rather than from the page.
        said.Write($"\nrezrov: listed {listing.Routines.Count} routines and {listing.Gaps.Count} gaps to zork1.lst\n");

        // Breaking on a routine the opening calls, which is reached
        // before the game has read anything.
        Play(games, [], ["delete", "break 5472", "continue", "where", "locals", "next", "next", "finish"], said);

        // Watching the global the status line scores from, and walking
        // into the house, which is worth ten points.
        Play(
            games,
            ["superbrief", "n", "e", "open window", "w"],
            ["delete", "watch G01", "continue", "where"],
            said);

        return said.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Plays Zork I under the debugger, writing down both halves of
    /// what the two of them say.
    /// </summary>
    private static void Play(string games, string[] commands, string[] typed, TextWriter said)
    {
        var (memory, header) = Read(Path.Combine(games, Zork1));
        var screen = new StringWriter();
        var machine = new Interpreter(memory, new TextWriterScreen(screen), new ScriptedInput(commands));
        var session = new DebugSession(machine, Disassembly.Of(memory, header));

        // The command line program breaks on the reads before it hands
        // the prompt over, so a session in the page begins with them
        // set and the examples start by giving them up.
        said.Write('\n');
        said.Write(session.Obey("break reads"));
        said.Write('\n');

        foreach (var line in typed)
        {
            said.Write('\n');
            said.Write(session.Obey(line));
            said.Write('\n');
        }

        // What the game printed, and then the same again with the
        // prompts taken off the front. A console echoes each command
        // after the prompt it was asked at, so in the page the game's
        // answer begins a line of its own; here nothing echoes and the
        // answer follows the prompt on the same one. Which of the two
        // happens belongs to whatever is reading the keyboard rather
        // than to the game.
        var printed = screen.ToString().ReplaceLineEndings("\n");
        said.Write('\n');
        said.Write(printed);
        said.Write('\n');
        said.Write(string.Join('\n', printed.Split('\n').Select(line => line.TrimStart('>'))));
        said.Write('\n');
    }

    private static (ZMemory Memory, StoryHeader Header) Read(string path)
    {
        var memory = new ZMemory(File.ReadAllBytes(path));

        return (memory, new StoryHeader(memory));
    }
}

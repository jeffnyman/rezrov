using Rezrov.AaMachine;
using Rezrov.AaMachine.Execution;

namespace Rezrov.Cli;

/// <summary>
/// Plays an Aa-machine story on the console, as a stream of text.
/// </summary>
/// <remarks>
/// [aam output] The Aa-machine's own output model has divs, spans,
/// links and status areas, and a stream of text has none of those. It
/// keeps the line breaks and the paragraph breaks, wraps to the width
/// of the terminal, and hears nothing a story writes into a status
/// area, which is what the reference interpreter does at a terminal
/// too.
/// </remarks>
internal static class AaMachinePlayer
{
    public static int Play(byte[] bytes, string? commands, string? save, int? seed)
    {
        AaStory story;

        try
        {
            story = AaStory.Read(bytes);
        }
        catch (InvalidDataException e)
        {
            Console.Error.WriteLine($"rezrov: {e.Message}");
            return 1;
        }

        var output = new TextOutput(Console.Out, story.Styles, resource => AltText(story, resource), Width());
        var machine = new Machine(story, output, seed);
        var typed = new Queue<string>(commands is null ? [] : File.ReadAllLines(commands));

        machine.SaveFileName = writing => save ?? Ask(output, writing);

        try
        {
            var status = machine.Start();

            while (status != AaStatus.Quit)
            {
                var fromFile = typed.Count > 0;

                if (Next(typed) is not { } line)
                {
                    break;
                }

                // A terminal echoes what the player types. A command
                // read from a file has nobody to type it, so it is
                // echoed here and the transcript reads the same way.
                if (fromFile)
                {
                    output.Sync();
                    Console.Out.WriteLine(line);
                }

                if (status == AaStatus.GetInput)
                {
                    output.Typed();
                    status = machine.ProceedWithInput(line);
                    continue;
                }

                // A story waiting for one key is given the line one
                // key at a time, and the return at the end counts.
                foreach (var key in line)
                {
                    if (status != AaStatus.GetKey)
                    {
                        break;
                    }

                    status = machine.ProceedWithKey(key);
                }

                if (status == AaStatus.GetKey)
                {
                    status = machine.ProceedWithKey('\r');
                }
            }
        }
        catch (AaMachineException e)
        {
            output.Finish();
            Console.Error.WriteLine($"rezrov: {e.Message}");
            return 1;
        }

        output.Finish();

        return 0;
    }

    private static string? Next(Queue<string> typed) =>
        typed.Count > 0 ? typed.Dequeue() : Console.In.ReadLine();

    // [aam opcode] Where a saved game goes. There is no file dialog at
    // a terminal, so the story's own screen is where the name is
    // asked for, as Infocom's interpreters did it.
    private static string? Ask(TextOutput output, bool writing)
    {
        output.Sync();
        Console.Write(writing ? "Save to: " : "Restore from: ");

        var name = Console.In.ReadLine();

        output.Typed();

        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    /// <summary>
    /// What to say in place of a resource that cannot be shown, which
    /// is the alternative text the story carries for it.
    /// </summary>
    internal static string AltText(AaStory story, int resource) =>
        resource >= 0 && resource < story.Resources.Count
            ? story.Text.At(story.Resources[resource].AltText)
            : string.Empty;

    private static int Width()
    {
        try
        {
            return Console.WindowWidth > 1 ? Console.WindowWidth - 1 : 80;
        }
        catch (IOException)
        {
            // Nothing is attached, which is what happens when the
            // output is being piped somewhere.
            return 80;
        }
    }
}

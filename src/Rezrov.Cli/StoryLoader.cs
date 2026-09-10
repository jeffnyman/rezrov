using Rezrov.Core;
using Rezrov.Core.Blorb;

namespace Rezrov.Cli;

/// <summary>
/// A story ready to run: the Z-code, and the resources that go with it
/// if there are any.
/// </summary>
internal sealed record LoadedStory(byte[] Bytes, BlorbFile? Resources);

/// <summary>
/// Turns a path on the command line into a story to run, whichever way
/// the game and its resources are packaged.
/// </summary>
internal static class StoryLoader
{
    /// <summary>
    /// Loads the story at the path, with the resource file named or
    /// else one found beside it, or returns null after saying why on
    /// <paramref name="errors"/>.
    /// </summary>
    public static LoadedStory? Load(string path, string? blorbPath, TextWriter errors)
    {
        if (!File.Exists(path))
        {
            errors.WriteLine($"rezrov: no such file: {path}");
            return null;
        }

        // [zm 1.1.4] A story file is at most 512K, so reading the whole
        // thing up front costs nothing worth avoiding.
        var bytes = File.ReadAllBytes(path);
        var format = StoryFormatDetector.Detect(bytes);

        if (format == StoryFormat.Blorb)
        {
            // [blorb 5] A resource file with an executable chunk has
            // everything needed to run the game.
            if (ReadBlorb(bytes, path, errors) is not { } packaged)
            {
                return null;
            }

            if (packaged.Executable is not { ChunkType: "ZCOD" } executable)
            {
                errors.WriteLine($"rezrov: {Path.GetFileName(path)} has no Z-code game in it");
                return null;
            }

            return new LoadedStory(executable.Data.ToArray(), packaged);
        }

        if (format != StoryFormat.ZMachine)
        {
            errors.WriteLine($"rezrov: only Z-machine story files can be run yet, and this is {format}");
            return null;
        }

        return new LoadedStory(bytes, FindResources(path, blorbPath, errors));
    }

    /// <summary>
    /// Reads a Blorb file, or returns null after saying what was wrong
    /// with it.
    /// </summary>
    public static BlorbFile? ReadBlorb(byte[] bytes, string path, TextWriter errors)
    {
        try
        {
            return BlorbFile.Read(bytes);
        }
        catch (InvalidDataException e)
        {
            errors.WriteLine($"rezrov: {Path.GetFileName(path)}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// [blorb 5] A resource file without an executable is used in tandem
    /// with the story: the one named on the command line, or else one
    /// beside the story with the same name and a Blorb extension, which
    /// is how the Infocom sound files are distributed.
    /// </summary>
    private static BlorbFile? FindResources(string storyPath, string? blorbPath, TextWriter errors)
    {
        if (blorbPath is null)
        {
            foreach (var extension in new[] { ".blb", ".blorb", ".zblorb" })
            {
                var candidate = Path.ChangeExtension(storyPath, extension);
                if (File.Exists(candidate))
                {
                    blorbPath = candidate;
                    break;
                }
            }
        }

        if (blorbPath is null)
        {
            return null;
        }

        if (!File.Exists(blorbPath))
        {
            errors.WriteLine($"rezrov: no such file: {blorbPath}");
            return null;
        }

        return ReadBlorb(File.ReadAllBytes(blorbPath), blorbPath, errors);
    }
}

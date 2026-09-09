using Rezrov.Core;

namespace Rezrov.Cli;

internal static class Program
{
    internal static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: rezrov <story-file>");
            return 2;
        }

        var path = args[0];

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"rezrov: no such file: {path}");
            return 1;
        }

        // Only the leading bytes distinguish the formats, so there is no
        // reason to pull a whole story file into memory to answer this.
        // Some Infocom files are small, so a short read is expected rather
        // than exceptional.
        Span<byte> prefix = stackalloc byte[64];

        int read;
        using (var stream = File.OpenRead(path))
        {
            read = stream.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
        }

        var format = StoryFormatDetector.Detect(prefix[..read]);

        Console.WriteLine($"{Path.GetFileName(path)}: {format}");

        return format is StoryFormat.Unknown ? 1 : 0;
    }
}

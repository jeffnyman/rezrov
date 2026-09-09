using Rezrov.Core;
using Rezrov.ZMachine;
using Rezrov.ZMachine.Objects;
using Rezrov.ZMachine.Text;

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

        // [zm 1.1.4] A story file is at most 512K, so reading the whole
        // thing up front costs nothing worth avoiding.
        var bytes = File.ReadAllBytes(path);
        var format = StoryFormatDetector.Detect(bytes);

        Console.WriteLine($"{Path.GetFileName(path)}: {format}");

        return format switch
        {
            StoryFormat.ZMachine => DescribeZMachine(bytes),
            StoryFormat.Unknown => 1,
            _ => 0,
        };
    }

    private static int DescribeZMachine(byte[] bytes)
    {
        var memory = new ZMemory(bytes);

        StoryHeader header;
        try
        {
            header = new StoryHeader(memory);
        }
        catch (InvalidDataException e)
        {
            Console.Error.WriteLine($"rezrov: {e.Message}");
            return 1;
        }

        Console.WriteLine($"  version {(int)header.Version}, release {header.Release}, serial {header.SerialCode}");

        if (header.InformVersion.Trim('\0').Length > 0)
        {
            Console.WriteLine($"  compiled by Inform {header.InformVersion}");
        }

        Console.WriteLine(
            $"  dynamic memory to {header.StaticMemoryBase:X4}, high memory from {header.HighMemoryBase:X4}, {bytes.Length} bytes on disk");

        if (header.HasFileLength)
        {
            var verdict = header.VerifyChecksum() ? "matches" : "does not match";
            Console.WriteLine($"  declared length {header.FileLength}, checksum {header.Checksum:X4} {verdict}");
        }
        else
        {
            Console.WriteLine("  no length or checksum recorded");
        }

        var objects = new ObjectTable(memory, header, new ZTextDecoder(memory, header));
        if (objects.Count > 0)
        {
            Console.WriteLine($"  {objects.Count} objects, the first named \"{objects.ShortName(1)}\"");
        }

        return 0;
    }
}

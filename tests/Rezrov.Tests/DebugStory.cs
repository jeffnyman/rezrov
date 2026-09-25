using Rezrov.ZMachine;

namespace Rezrov.Tests;

/// <summary>
/// The smallest story file a scan can be pointed at: a header that says
/// where high memory begins, and then whatever bytes the test wants
/// there.
/// </summary>
/// <remarks>
/// Nothing is left over at the end, because trailing bytes are part of
/// high memory and the scan would have an opinion about them.
/// </remarks>
internal static class DebugStory
{
    /// <summary>Where high memory begins in these fixtures.</summary>
    public const int HighMemory = 0x40;

    public static (ZMemory Memory, StoryHeader Header) Of(
        byte[] high,
        ZMachineVersion version = ZMachineVersion.V3)
    {
        var bytes = new byte[HighMemory + high.Length];

        bytes[0x00] = (byte)version;
        Put(bytes, 0x04, HighMemory);      // [zm 11.1] high memory base
        Put(bytes, 0x06, HighMemory + 1);  // [zm 11.1] initial PC
        Put(bytes, 0x0E, HighMemory);      // [zm 11.1] static memory base

        high.CopyTo(bytes, HighMemory);

        var memory = new ZMemory(bytes);
        return (memory, new StoryHeader(memory));
    }

    private static void Put(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 8);
        bytes[offset + 1] = (byte)value;
    }
}

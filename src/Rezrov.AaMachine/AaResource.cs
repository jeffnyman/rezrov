using System.Buffers.Binary;

namespace Rezrov.AaMachine;

/// <summary>
/// [aam story] One entry of the URLS chunk: something the game can
/// show or link to, and the words to use instead if it cannot be shown.
/// </summary>
/// <param name="AltText">
/// The address in the WRIT chunk of the alternative text.
/// </param>
/// <param name="Url">
/// Where the thing lives. The scheme "file" means a FILE chunk of this
/// same story, as in "file:title.png".
/// </param>
/// <param name="Options">
/// A comma-separated list, each item either a word or a key and a
/// value parted by a colon. Anything unrecognized is to be ignored.
/// </param>
public readonly record struct AaResource(int AltText, string Url, string Options)
{
    internal static AaResource[] Read(ReadOnlySpan<byte> chunk, AaCharacterSet characters, int shift)
    {
        if (chunk.IsEmpty)
        {
            return [];
        }

        if (chunk.Length < 2)
        {
            throw new InvalidDataException("The URLS chunk is too short to say how many resources it holds.");
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(chunk);

        if (2 + (count * 2) > chunk.Length)
        {
            throw new InvalidDataException($"The URLS chunk says it holds {count} resources but has room for fewer.");
        }

        var resources = new AaResource[count];

        for (var i = 0; i < count; i++)
        {
            var offset = BinaryPrimitives.ReadUInt16BigEndian(chunk[(2 + (i * 2))..]);

            if (offset + 3 > chunk.Length)
            {
                throw new InvalidDataException($"Resource {i} lies past the end of the URLS chunk.");
            }

            var at = chunk[offset..];

            // [aam story] Three bytes of address, shifted by the amount
            // the header gives. This is a plain number, not one of the
            // tiny, short and long pointers that bytecode operands use,
            // because the field here is a fixed three bytes wide and so
            // has no need to say which of the three it is.
            var address = (((at[0] << 16) | (at[1] << 8) | at[2]) << shift);

            at = at[3..];

            // Read in order: the two strings follow one another
            // and each read moves the span on.
            var url = Terminated(ref at, characters, i);
            var options = Terminated(ref at, characters, i);

            resources[i] = new AaResource(address, url, options);
        }

        return resources;
    }

    private static string Terminated(ref ReadOnlySpan<byte> at, AaCharacterSet characters, int which)
    {
        var end = at.IndexOf((byte)0);

        if (end < 0)
        {
            throw new InvalidDataException($"Resource {which} of the URLS chunk is not terminated.");
        }

        var text = characters.Text(at[..end]);
        at = at[(end + 1)..];

        return text;
    }
}

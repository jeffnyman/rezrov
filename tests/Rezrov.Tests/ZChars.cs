namespace Rezrov.Tests;

/// <summary>
/// Builds encoded text by hand for tests, so that a test can describe its
/// input as Z-characters and read like the clause it is checking.
/// </summary>
internal static class ZChars
{
    /// <summary>
    /// The Z-character for a lower case letter. [zm 3.5.3] 'a' is 6 and
    /// the rest follow in order.
    /// </summary>
    public static int Letter(char c) => c - 'a' + 6;

    /// <summary>
    /// Packs Z-characters into words the way [zm 3.2] describes: three to
    /// a word, most significant first, padded with 5s to fill the last
    /// word, and with the top bit set on that last word.
    /// </summary>
    public static byte[] Words(params int[] zCharacters)
    {
        var padded = zCharacters.ToList();
        while (padded.Count % 3 != 0)
        {
            // [zm 3.6] Padding is conventionally 5s.
            padded.Add(5);
        }

        var bytes = new byte[padded.Count / 3 * 2];
        for (var i = 0; i < padded.Count; i += 3)
        {
            var word = (padded[i] << 10) | (padded[i + 1] << 5) | padded[i + 2];
            if (i + 3 == padded.Count)
            {
                word |= 0x8000;
            }

            bytes[i / 3 * 2] = (byte)(word >> 8);
            bytes[(i / 3 * 2) + 1] = (byte)word;
        }

        return bytes;
    }

    /// <summary>
    /// Encodes text made only of lower case letters and spaces, which
    /// needs no shifts and so is the same in every version.
    /// </summary>
    public static byte[] Text(string text) =>
        Words(text.Select(c => c == ' ' ? 0 : Letter(c)).ToArray());
}

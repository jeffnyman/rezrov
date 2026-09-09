namespace Rezrov.Core;

/// <summary>
/// Identifies a story file from its leading bytes.
/// </summary>
/// <remarks>
/// This lives in Rezrov.Core rather than in either virtual machine project
/// because deciding which machine should run a file necessarily happens
/// before either one is involved.
/// </remarks>
public static class StoryFormatDetector
{
    /// <summary>
    /// Identifies <paramref name="story"/> from its leading bytes. Only the
    /// first twelve bytes are ever examined, so a caller does not need to
    /// read a whole file to ask.
    /// </summary>
    public static StoryFormat Detect(ReadOnlySpan<byte> story)
    {
        // [blorb #overall-structure] A Blorb file is an IFF FORM whose type
        // is IFRS: the four bytes 'FORM', a four byte length, then 'IFRS'.
        // The container has to be recognized first, because the story file
        // it wraps is inside it and would otherwise be read as a header.
        if (story.Length >= 12
            && story[..4].SequenceEqual("FORM"u8)
            && story[8..12].SequenceEqual("IFRS"u8))
        {
            return StoryFormat.Blorb;
        }

        // [glulx #the-header] The header is the first 36 bytes, and its
        // first four are the magic number 47 6C 75 6C, ASCII 'Glul'.
        if (story.Length >= 4 && story[..4].SequenceEqual("Glul"u8))
        {
            return StoryFormat.Glulx;
        }

        // [zm 11.1] The header table puts the version number in byte $00.
        // Z-code has no magic number, so the version byte is the only thing
        // available to identify it, which is why this check comes last: it
        // is the weakest of the three and would otherwise claim files that
        // belong to another format.
        if (story.Length >= 1 && story[0] is >= 1 and <= 8)
        {
            return StoryFormat.ZMachine;
        }

        return StoryFormat.Unknown;
    }
}

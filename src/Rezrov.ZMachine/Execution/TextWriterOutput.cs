using Rezrov.ZMachine.Text;

namespace Rezrov.ZMachine.Execution;

/// <summary>
/// Prints to a <see cref="TextWriter"/>, turning each ZSCII code into a
/// character by the rules of [zm 3.8] and dropping any code that has no
/// printable form.
/// </summary>
/// <remarks>
/// Enough for a transcript, a test, or a terminal. It has no notion of
/// windows, styles, or a status line; those are the screen model's.
/// </remarks>
public sealed class TextWriterOutput : IOutput
{
    private readonly TextWriter _writer;
    private readonly ZMachineVersion _version;
    private readonly UnicodeTranslationTable _extraCharacters;

    public TextWriterOutput(TextWriter writer, StoryHeader header, ZMemory memory)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(header);

        _writer = writer;
        _version = header.Version;
        _extraCharacters = UnicodeTranslationTable.ForStory(header, memory);
    }

    public void Print(ushort zscii)
    {
        if (Zscii.ToUnicode(zscii, _version, _extraCharacters) is { } character)
        {
            _writer.Write(character);
        }
    }
}

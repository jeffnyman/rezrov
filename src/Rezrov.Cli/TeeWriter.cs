using System.Text;

namespace Rezrov.Cli;

/// <summary>
/// A writer that writes to two others, so that a play can be kept for
/// comparison and shown as it happens at the same time.
/// </summary>
internal sealed class TeeWriter : TextWriter
{
    private readonly TextWriter _first;
    private readonly TextWriter _second;

    public TeeWriter(TextWriter first, TextWriter second)
    {
        _first = first;
        _second = second;
    }

    public override Encoding Encoding => _first.Encoding;

    public override void Write(char value)
    {
        _first.Write(value);
        _second.Write(value);
    }

    public override void Write(string? value)
    {
        _first.Write(value);
        _second.Write(value);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        _first.Write(buffer, index, count);
        _second.Write(buffer, index, count);
    }

    public override void Flush()
    {
        _first.Flush();
        _second.Flush();
    }
}

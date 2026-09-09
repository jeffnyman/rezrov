namespace Rezrov.ZMachine.Execution;

/// <summary>
/// Where printed text goes: one ZSCII code at a time.
/// </summary>
/// <remarks>
/// The interpreter produces ZSCII, not strings, because that is what the
/// machine operates on. Turning it into something a person can see is a
/// separate step, and the editor's note on [zm 3.1] draws exactly that
/// line. The output streams of section 7, the screen model of section 8,
/// and a plain text writer are all things that can sit behind this.
/// </remarks>
public interface IOutput
{
    /// <summary>Receives one ZSCII code to print.</summary>
    void Print(ushort zscii);
}

namespace Rezrov.Glulx;

/// <summary>
/// A fault the Glulx machine cannot carry on from: a read outside memory,
/// a write into ROM, a stack that has run out, and the like. The game
/// stops and the frontend reports the message.
/// </summary>
/// <remarks>
/// The Glulx specification does not grade its errors the way the
/// Z-Machine standard's Appendix A does. Where it says an operation is
/// illegal, the reference interpreter halts with a message, and this
/// exception is that halt.
/// </remarks>
public sealed class GlulxException : Exception
{
    public GlulxException()
    {
    }

    public GlulxException(string message)
        : base(message)
    {
    }

    public GlulxException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

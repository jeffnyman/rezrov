namespace Rezrov.AaMachine;

/// <summary>
/// A fault the Aa-machine cannot carry on from: an opcode it does not
/// have, an instruction that runs off the end of the bytecode, and the
/// like. The game stops and the frontend reports the message.
/// </summary>
/// <remarks>
/// [aam story] This is not the same thing as the machine's own runtime
/// errors, which are numbered and which the specification handles by
/// restarting the story with the number in R00. Those are conditions
/// the story can be written to expect. This is the story being wrong.
/// </remarks>
public sealed class AaMachineException : Exception
{
    public AaMachineException()
    {
    }

    public AaMachineException(string message)
        : base(message)
    {
    }

    public AaMachineException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

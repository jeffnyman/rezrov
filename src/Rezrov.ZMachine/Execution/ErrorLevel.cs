namespace Rezrov.ZMachine.Execution;

/// <summary>
/// How much to make of a story file doing something the standard leaves
/// undefined, such as operating on object 0.
/// </summary>
/// <remarks>
/// [zm A] Infocom's files are close to bug free, but many games released
/// since have errors of this kind, most often illegal operations on
/// object 0. Interpreters that ignored them silently made the bugs hard
/// to find; interpreters that halted made the games unplayable. The
/// standard's answer is four levels the player can choose between, and
/// these are those four.
/// </remarks>
public enum ErrorLevel
{
    /// <summary>Never report the bug.</summary>
    Never,

    /// <summary>Report the first instance of each type of error.</summary>
    ReportOnce,

    /// <summary>Report every error.</summary>
    ReportAll,

    /// <summary>Treat every error as fatal and end the game.</summary>
    Fatal,
}

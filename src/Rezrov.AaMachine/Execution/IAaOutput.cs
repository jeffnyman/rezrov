namespace Rezrov.AaMachine.Execution;

/// <summary>
/// [aam output] Where an Aa-machine story's output goes.
/// </summary>
/// <remarks>
/// Output is a stream of characters, line breaks and paragraph breaks,
/// divided into nested divs and spans, going either to the main area
/// or to one of the status areas. A frontend is free to ignore the
/// status areas, and a plain stream of text does.
///
/// The machine keeps every pair of enter and leave calls balanced, and
/// the one exception is <see cref="LeaveAll"/>, which unwinds the lot
/// after a runtime error.
/// </remarks>
public interface IAaOutput
{
    /// <summary>Whether the frontend can show a clickable link.</summary>
    bool HasLinks { get; }

    /// <summary>Whether bold and italic can be told apart.</summary>
    bool HasStyles { get; }

    /// <summary>Whether text can be colored.</summary>
    bool HasColor { get; }

    /// <summary>Whether text can be aligned.</summary>
    bool HasAlignment { get; }

    /// <summary>Whether a transcript is being written.</summary>
    bool IsScripting { get; }

    void Write(string text);

    /// <summary>
    /// A space that does not break a line, and does not join the run
    /// of pending spaces either.
    /// </summary>
    void NoBreakSpace();

    void Space();

    /// <summary>A run of spaces, as wide as the game asked.</summary>
    void Spaces(int count);

    void Newline();

    void EndParagraph();

    void EnterDiv(int styleClass);

    void LeaveDiv(int styleClass);

    void EnterSpan(int styleClass);

    void LeaveSpan();

    /// <summary>
    /// [aam opcode] Applies a style class to the body of the whole
    /// document, which a stream of text has no use for.
    /// </summary>
    void SetBody(int styleClass);

    void EnterStatus(int area, int styleClass);

    void LeaveStatus();

    /// <summary>A link that repeats the words it is made of.</summary>
    void EnterSelfLink();

    void LeaveSelfLink();

    /// <summary>
    /// A link that enters <paramref name="input"/> when used.
    /// </summary>
    void EnterLink(string input);

    void LeaveLink();

    void EnterLinkResource(int resource);

    void LeaveLinkResource();

    void EmbedResource(int resource);

    bool CanEmbedResource(int resource);

    void ProgressBar(int amount, int total);

    /// <summary>
    /// The deprecated style bits: reverse, bold, italic, fixed.
    /// </summary>
    void SetStyle(int bits);

    void ResetStyle(int bits);

    void Unstyle();

    /// <summary>Empties the divs but keeps them.</summary>
    void Clear();

    void ClearStatus();

    void ClearAll();

    /// <summary>Turns the links already shown into ordinary text.</summary>
    void ClearLinks();

    /// <summary>
    /// Clears the current div, if that means anything here.
    /// </summary>
    void ClearDiv();

    /// <summary>
    /// Clears what the player has already had a chance to read.
    /// </summary>
    void ClearOld();

    /// <summary>Unwinds every div, span and status area at once.</summary>
    void LeaveAll();

    /// <summary>
    /// Puts the frontend back as it was before the story started,
    /// which is what a restart wants.
    /// </summary>
    void Restart();

    /// <summary>Makes sure everything written so far is visible.</summary>
    void Sync();

    /// <summary>
    /// [aam opcode] How wide or how tall the current div is, in
    /// characters. Zero means the frontend does not know.
    /// </summary>
    int Measure(int which);

    /// <summary>
    /// Starts a transcript, and says whether it managed to.
    /// </summary>
    bool ScriptOn();

    void ScriptOff();
}

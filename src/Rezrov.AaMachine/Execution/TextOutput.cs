namespace Rezrov.AaMachine.Execution;

/// <summary>
/// [aam output] An Aa-machine story as a plain stream of text, wrapped
/// to a width.
/// </summary>
/// <remarks>
/// This is the same frontend the reference interpreter offers at a
/// terminal, and it is written to behave the same way, because the
/// conformance transcripts were recorded through it and a difference
/// of one space would show.
///
/// It holds back the word it is in the middle of, and the spaces
/// before it, until it knows whether they will fit. That is what makes
/// the wrapping work, and it is also why a line break has to flush
/// first. Blank lines are counted rather than written, so that a line
/// break after a paragraph break adds nothing.
///
/// The status areas are not shown at all. A stream of text has nowhere
/// to put them, and a story that writes one is simply not heard.
/// </remarks>
public sealed class TextOutput : IAaOutput
{
    private readonly TextWriter _writer;
    private readonly AaStyles _styles;
    private readonly Func<int, string?> _resourceText;

    private string _word = string.Empty;
    private int _spaces;
    private int _column;
    private int _newlines = 1;
    private bool _hidden;

    /// <param name="writer">Where the text goes.</param>
    /// <param name="styles">
    /// The story's style sheet, which is read for the margins alone: a
    /// margin of so many ems is so many blank lines.
    /// </param>
    /// <param name="resourceText">
    /// What to say in place of a resource that cannot be shown.
    /// </param>
    /// <param name="width">
    /// The column to wrap at, or zero not to wrap at all.
    /// </param>
    public TextOutput(TextWriter writer, AaStyles styles, Func<int, string?> resourceText, int width = 80)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(resourceText);

        _writer = writer;
        _styles = styles;
        _resourceText = resourceText;

        Width = width;
    }

    /// <summary>The column the text wraps at.</summary>
    public int Width { get; set; }

    public bool HasLinks => false;

    public bool HasStyles => false;

    public bool HasColor => false;

    public bool HasAlignment => false;

    public bool IsScripting => false;

    public void Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (_hidden)
        {
            return;
        }

        foreach (var character in text)
        {
            if (character == ' ')
            {
                // A space ends the word being held back and joins the
                // run of spaces waiting in front of the next one.
                Flush();
                _spaces++;
            }
            else if (character == '-')
            {
                // A hyphen is a place a line may break, so the word so
                // far goes out and the rest starts afresh.
                _word += character;
                Flush();
            }
            else
            {
                _word += character;
            }
        }
    }

    public void NoBreakSpace()
    {
        if (!_hidden)
        {
            _word += ' ';
        }
    }

    public void Space() => Write(" ");

    public void Spaces(int count)
    {
        if (_hidden)
        {
            return;
        }

        Flush();

        if (Width > 0 && count > Width - _column)
        {
            count = Width - _column;
        }

        for (var i = 0; i < count; i++)
        {
            _writer.Write(' ');
            _column++;
        }

        _newlines = 0;
    }

    public void Newline() => Break(0);

    public void EndParagraph() => Break(1);

    public void EnterDiv(int styleClass) => Break(Blank(styleClass, "margin-top"));

    public void LeaveDiv(int styleClass) => Break(Blank(styleClass, "margin-bottom"));

    public void EnterSpan(int styleClass)
    {
    }

    public void LeaveSpan()
    {
    }

    public void SetBody(int styleClass)
    {
    }

    public void EnterStatus(int area, int styleClass)
    {
        Newline();
        _hidden = true;
    }

    public void LeaveStatus() => _hidden = false;

    public void EnterSelfLink()
    {
    }

    public void LeaveSelfLink()
    {
    }

    public void EnterLink(string input)
    {
    }

    public void LeaveLink()
    {
    }

    public void EnterLinkResource(int resource)
    {
    }

    public void LeaveLinkResource()
    {
    }

    public void EmbedResource(int resource)
    {
        // Nothing here can show a picture, so the words the story
        // offered in its place are the next best thing.
        Write("[");
        Write(_resourceText(resource) ?? string.Empty);
        Write("]");
    }

    public bool CanEmbedResource(int resource) => false;

    public void ProgressBar(int amount, int total)
    {
        if (_hidden)
        {
            return;
        }

        // End caps and a safety margin, as the reference frontend
        // leaves them. A bar told it is further along than it can be
        // is not corrected: it simply runs off the line, which is how
        // the reference behaves and what it is worth showing.
        var full = (Width > 0 ? Width : 80) - 3;
        var filled = total == 0 ? 0 : (int)Math.Floor((full * ((double)amount / total)) + 0.5);

        EnterDiv(-1);
        Write("[");
        Write(new string('=', Math.Max(filled, 0)));
        Write(new string(' ', Math.Max(full - filled, 0)));
        Write("]");
        LeaveDiv(-1);
    }

    public void SetStyle(int bits)
    {
    }

    public void ResetStyle(int bits)
    {
    }

    public void Unstyle()
    {
    }

    public void Clear() => EndParagraph();

    public void ClearStatus()
    {
    }

    public void ClearAll() => EndParagraph();

    public void ClearLinks()
    {
    }

    public void ClearDiv()
    {
    }

    public void ClearOld()
    {
    }

    public void LeaveAll()
    {
        Newline();
        _hidden = false;
    }

    public void Restart()
    {
        _hidden = false;
        _word = string.Empty;
        _spaces = 0;
        _column = 0;
        _newlines = 1;
    }

    public void Sync() => Flush();

    public int Measure(int which) => which == 0 && Width > 0 ? Width : 0;

    public bool ScriptOn() => false;

    public void ScriptOff()
    {
    }

    /// <summary>
    /// Ends the play, which means one last line so that the text does
    /// not stop in the middle of one.
    /// </summary>
    public void Finish()
    {
        Newline();
        Flush();
    }

    /// <summary>
    /// Starts a fresh line for input, which the player's own typing
    /// has already moved on to.
    /// </summary>
    public void Typed()
    {
        _column = 0;
        _spaces = 0;
        _newlines = 1;
    }

    private void Flush()
    {
        // The word being held back is what makes wrapping possible:
        // if it will not fit on this line, the line ends here, and
        // the spaces that would have gone in front of it are dropped.
        if (Width > 0 && _column + _spaces + _word.Length > Width)
        {
            VerticalSpace(0);
        }

        while (_spaces > 0)
        {
            // Spaces at the start of a line are dropped, which is what
            // keeps a wrapped line from starting with one.
            if (_column > 0)
            {
                _writer.Write(' ');
                _column++;
            }

            _spaces--;
        }

        if (_word.Length > 0)
        {
            _writer.Write(_word);

            _column += _word.Length;
            _newlines = 0;
            _word = string.Empty;
        }
    }

    // Puts out whatever is waiting and then ends the line.
    // [aam output deviates] How many blank lines a margin comes
    // to. The reference frontend reads one with a pattern that
    // wants digits and then "em" with nothing in between, so a
    // margin of a line and a half is no margin at all to it rather
    // than a margin of one line. That is not the better reading,
    // and the display on a terminal does round it down, but the
    // transcripts this frontend exists to match came from there.
    private int Blank(int styleClass, string key)
    {
        var margin = _styles.Length(styleClass, key);

        return margin.Unit == AaUnit.Em && margin.Amount == Math.Floor(margin.Amount)
            ? (int)margin.Amount
            : 0;
    }

    private void Break(int blank)
    {
        if (_hidden)
        {
            return;
        }

        Flush();
        VerticalSpace(blank);
    }

    // Ends the line, and then as many more as it takes to leave the
    // asked-for number of blank ones. Counting rather than writing is
    // what makes a line break after a paragraph break do nothing.
    private void VerticalSpace(int blank)
    {
        while (_newlines < blank + 1)
        {
            _writer.Write('\n');
            _newlines++;
        }

        _column = 0;
        _spaces = 0;
    }
}

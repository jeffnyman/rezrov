using System.Globalization;
using System.Text;
using Rezrov.AaMachine.Instructions;

namespace Rezrov.AaMachine.Execution;

/// <summary>
/// Everything the machine says, and the whitespace model that decides
/// where the spaces between the words go.
/// </summary>
/// <remarks>
/// [aam opcode] A story never asks for a space directly. It says what
/// kind of gap it wants next, and the gap is only made when something
/// follows it. That is why a game can end with a paragraph break and
/// leave no blank line behind, and why punctuation can be printed
/// without the space that would otherwise come before it.
/// </remarks>
public sealed partial class Machine
{
    private void Output(Opcode opcode, ReadOnlySpan<Operand> ops)
    {
        switch (opcode)
        {
            case Opcode.PrintAStrA:
            case Opcode.PrintAStrN:
                Gap(before: true);
                _output.Write(StringAt(ops[0].Number));
                _spc = opcode == Opcode.PrintAStrA ? Spacing.Auto : Spacing.NoSpace;
                break;

            case Opcode.PrintNStrA:
            case Opcode.PrintNStrN:
                Gap(before: false);
                _output.Write(StringAt(ops[0].Number));
                _spc = opcode == Opcode.PrintNStrA ? Spacing.Auto : Spacing.NoSpace;
                break;

            case Opcode.NoSpace:
                Ask(Spacing.NoSpace);
                break;

            case Opcode.Space:
                Ask(Spacing.PendingSpace);
                break;

            case Opcode.Line:
                if (_cwl == 0 && _spc < Spacing.Line)
                {
                    _output.Newline();
                    _spc = Spacing.Line;
                }

                break;

            case Opcode.Par:
                if (_cwl == 0 && _spc < Spacing.Paragraph)
                {
                    // A paragraph break inside a span is two line
                    // breaks, since a span may not be broken across
                    // paragraphs.
                    if (_spans > 0)
                    {
                        _output.Newline();
                        _output.Newline();
                    }
                    else
                    {
                        _output.EndParagraph();
                    }

                    _spc = Spacing.Paragraph;
                }

                break;

            case Opcode.SpaceN:
            {
                var count = Deref(Value(ops[0]));

                // [aam opcode] A run of no spaces at all is not a
                // request for anything, so it leaves the gap alone.
                if (_cwl == 0 && count > 0x4000 && count < 0x8000)
                {
                    _output.Spaces(AaValue.Value(count));
                    _spc = Spacing.Space;
                }

                break;
            }

            case Opcode.PrintVal:
                PrintValue(Deref(Value(ops[0])));
                break;

            case Opcode.EnterDiv:
            {
                var styleClass = Value(ops[0]);

                if (_cwl == 0)
                {
                    if (_spans > 0)
                    {
                        throw new RuntimeError(7);
                    }

                    _output.EnterDiv(styleClass);
                    _divs.Add(styleClass);
                    _spc = Spacing.Paragraph;
                }

                break;
            }

            case Opcode.LeaveDiv:
                if (_cwl == 0 && _divs.Count > 0)
                {
                    var styleClass = _divs[^1];

                    _divs.RemoveAt(_divs.Count - 1);
                    _output.LeaveDiv(styleClass);
                    _spc = Spacing.Line;
                }

                break;

            case Opcode.EnterSpan:
                if (_cwl == 0)
                {
                    Gap(before: true, noBreak: false);
                    _output.EnterSpan(Value(ops[0]));
                    _spc = Spacing.NoSpace;
                    _spans++;
                }

                break;

            case Opcode.LeaveSpan:
                if (_cwl == 0)
                {
                    _output.LeaveSpan();
                    _spc = Spacing.Auto;
                    _spans--;
                }

                break;

            case Opcode.EnterStatus:
            {
                // [aam opcode] The older form of this opcode always
                // meant the top area, and carried only its class.
                var area = ops.Length > 1 ? Value(ops[0]) : 0;
                var styleClass = Value(ops[^1]);

                if (_inStatus || _spans > 0)
                {
                    throw new RuntimeError(7);
                }

                if (_cwl == 0)
                {
                    _output.EnterStatus(area, styleClass);
                    _spc = Spacing.Paragraph;
                    _inStatus = true;
                }

                break;
            }

            case Opcode.LeaveStatus:
                if (_cwl == 0)
                {
                    _output.LeaveStatus();
                    _spc = Spacing.Paragraph;
                    _inStatus = false;
                }

                break;

            case Opcode.SetBody:
                if (_inStatus || _spans > 0)
                {
                    throw new RuntimeError(7);
                }

                _output.SetBody(Value(ops[0]));
                break;

            case Opcode.EnterLink:
            {
                // The words are gathered before the link is entered,
                // since a span may not be read from inside one.
                var words = Deref(Value(ops[0]));

                EnterLink(() => _output.EnterLink(LinkText(words)));
                break;
            }

            case Opcode.EnterLinkRes:
            {
                var resource = AaValue.Value(Deref(Value(ops[0])));

                EnterLink(() => _output.EnterLinkResource(resource));
                break;
            }

            case Opcode.EnterSelfLink:
                // [aam opcode] A link that types back the words it is
                // made of leaves the gap after it as a space, since
                // the words go on.
                EnterLink(_output.EnterSelfLink, after: Spacing.Space);
                break;

            case Opcode.LeaveLink:
                LeaveLink(_output.LeaveLink);
                break;

            case Opcode.LeaveLinkRes:
                LeaveLink(_output.LeaveLinkResource);
                break;

            case Opcode.LeaveSelfLink:
                LeaveLink(_output.LeaveSelfLink);
                break;

            case Opcode.SetStyle:
                if (_cwl == 0)
                {
                    Gap(before: true, noBreak: false);
                    _output.SetStyle(Value(ops[0]));
                    _spc = Spacing.Space;
                }

                break;

            case Opcode.ResetStyle:
                if (_cwl == 0)
                {
                    _output.ResetStyle(Value(ops[0]));
                }

                break;

            case Opcode.EmbedRes:
                if (_cwl == 0)
                {
                    _output.EmbedResource(AaValue.Value(Deref(Value(ops[0]))));
                }

                break;

            case Opcode.CanEmbedRes:
                Store(
                    ops[1],
                    _output.CanEmbedResource(AaValue.Value(Deref(Value(ops[0])))) ? (ushort)1 : AaValue.Null);
                break;

            case Opcode.Progress:
            {
                var amount = Deref(Value(ops[0]));
                var total = Deref(Value(ops[1]));

                if (_cwl == 0
                    && AaValue.Tag(amount) == AaTag.Number
                    && AaValue.Tag(total) == AaTag.Number)
                {
                    _output.ProgressBar(AaValue.Value(amount), AaValue.Value(total));
                }

                break;
            }

            default:
                throw new AaMachineException($"The opcode {opcode} is not implemented.");
        }
    }

    // [aam opcode] The gap a print owes before it starts. A string
    // that wants a space in front of it takes one where the machine
    // is undecided; one that does not takes only a space already
    // asked for.
    private void Gap(bool before, bool noBreak = true)
    {
        if (_spc == Spacing.PendingSpace || (before && _spc == Spacing.Auto))
        {
            _output.Space();
        }
        else if (noBreak && _spc == Spacing.NoBreakSpace)
        {
            _output.NoBreakSpace();
        }
    }

    // A request for a wider gap wins; a request for a narrower one is
    // ignored, since something has already asked for more.
    private void Ask(Spacing spacing)
    {
        if (_cwl == 0 && _spc < spacing)
        {
            _spc = spacing;
        }
    }

    private void EnterLink(Action enter, Spacing after = Spacing.NoSpace)
    {
        if (_cwl != 0)
        {
            return;
        }

        // [aam opcode] A link inside a link is not a link. Only the
        // outermost one is told to the frontend.
        if (_links == 0)
        {
            Gap(before: true, noBreak: false);
            enter();
            _spc = after;
        }

        _links++;
        _spans++;
    }

    private void LeaveLink(Action leave)
    {
        if (_cwl != 0)
        {
            return;
        }

        _spans--;
        _links--;

        if (_links == 0)
        {
            leave();
        }
    }

    // [aam opcode] The words a link would type if it were clicked,
    // which is its list of words with single spaces between them.
    private string LinkText(ushort list)
    {
        var upper = _upper;
        var text = new StringBuilder();

        _upper = false;

        while (AaValue.IsPair(list))
        {
            var word = Deref(_heap[list & 0x1fff]);

            if (AaValue.Tag(word) is AaTag.Word or AaTag.Character or AaTag.ExtDict or AaTag.Number)
            {
                if (text.Length > 0)
                {
                    text.Append(' ');
                }

                text.Append(ValueText(word));
            }

            list = Deref(_heap[(list & 0x1fff) + 1]);
        }

        _upper = upper;

        return text.ToString();
    }

    private void PrintValue(ushort value)
    {
        if (_cwl != 0)
        {
            // [aam opcode] While words are being collected rather
            // than shown, a value is put on the stack instead.
            PushSerialized(value);
            return;
        }

        if (AaValue.Tag(value) == AaTag.Character)
        {
            var character = AaValue.Value(value);

            // Punctuation says for itself whether it wants a space in
            // front of it and whether it wants one after.
            if (_spc == Spacing.PendingSpace
                || (_spc == Spacing.Auto && !_story.Language.NoSpaceBefore.Contains((byte)character)))
            {
                _output.Space();
            }

            _output.Write(Character((byte)character));

            _spc = _story.Language.NoSpaceAfter.Contains((byte)character)
                ? Spacing.NoSpace
                : Spacing.Auto;

            return;
        }

        Gap(before: true, noBreak: false);
        _output.Write(ValueText(value));
        _spc = Spacing.Auto;
    }

    /// <summary>
    /// [aam opcode] A value written out the way a story shows it: a
    /// list in brackets, a number in decimal, a variable that is still
    /// unbound as a dollar sign, and an object by the name its author
    /// gave it.
    /// </summary>
    private string ValueText(ushort value)
    {
        value = Deref(value);

        switch (AaValue.Tag(value))
        {
            case AaTag.ExtDict:
            {
                var text = new StringBuilder();

                for (var i = 0; i < 2; i++)
                {
                    var part = _heap[(value & 0x1fff) + i];

                    if (part < 0x3f00)
                    {
                        text.Append(ValueText(part));
                        continue;
                    }

                    while (AaValue.IsPair(part))
                    {
                        text.Append(ValueText(_heap[part & 0x1fff]));
                        part = _heap[(part & 0x1fff) + 1];
                    }
                }

                return text.ToString();
            }

            case AaTag.Pair:
            {
                var text = new StringBuilder("[");
                var spaced = false;

                _upper = false;

                while (AaValue.IsPair(value))
                {
                    if (spaced)
                    {
                        text.Append(' ');
                    }

                    text.Append(ValueText(AaValue.Reference(value & 0x1fff)));
                    spaced = true;
                    value = Deref(AaValue.Reference((value & 0x1fff) + 1));
                }

                // A list that does not end in the empty list shows
                // what it does end in, after a bar.
                return value == AaValue.Empty
                    ? text.Append(']').ToString()
                    : text.Append(" | ").Append(ValueText(value)).Append(']').ToString();
            }

            case AaTag.Reference:
                _upper = false;
                return "$";

            case AaTag.Number:
                _upper = false;
                return AaValue.Value(value).ToString(CultureInfo.InvariantCulture);

            case AaTag.Empty:
                _upper = false;
                return "[]";

            case AaTag.Character:
                return Character((byte)AaValue.Value(value));

            case AaTag.Word:
                return Text(_story.Dictionary.Characters(AaValue.Value(value)));

            case AaTag.Thing:
                _upper = false;
                return "#" + _story.ObjectNames.Name(value);

            default:
                _upper = false;
                return string.Empty;
        }
    }

    private string StringAt(int address)
    {
        _characters.Clear();
        _story.Text.Characters(address, _characters);

        return Text(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_characters));
    }

    private string Text(ReadOnlySpan<byte> characters)
    {
        var text = new StringBuilder(characters.Length);

        foreach (var character in characters)
        {
            text.Append(Character(character));
        }

        return text.ToString();
    }

    // [aam opcode] One character, with the pending request to make it
    // uppercase spent on it if there is one. A character the game
    // never defined is shown as two question marks, which is what the
    // reference interpreter does and what the transcripts record.
    private string Character(byte character)
    {
        var set = _story.Language.Characters;

        if (_upper)
        {
            character = set.ToUpper(character);
            _upper = false;
        }

        if (character < 0x80)
        {
            return ((char)character).ToString();
        }

        var index = character & 0x7f;

        if (index >= set.Count)
        {
            return "??";
        }

        return char.ConvertFromUtf32(set[index].Codepoint);
    }

    // [aam opcode] The extended operations, which share one opcode
    // between them. Most do something and carry on; quitting is the
    // one that does not.
    private AaStatus? Extended(int operation)
    {
        switch (operation)
        {
            case 0x00:
                _output.Sync();
                return AaStatus.Quit;

            case 0x01:
                ClearOutputState();
                Reinitialize();
                Reset(AaValue.Null);
                _output.Restart();

                // A restart is a fresh story, so there is nothing to
                // undo back to.
                _undo = null;
                break;

            case 0x02:
                Restore();
                break;

            case 0x03:
                Undo();
                break;

            case 0x04:
                if (_cwl == 0)
                {
                    _output.Unstyle();
                }

                break;

            case 0x05:
                if (_cwl == 0)
                {
                    Gap(before: true, noBreak: false);
                    _output.Write(_story.Serial);
                    _spc = Spacing.Auto;
                }

                break;

            case 0x06:
            case 0x07:
                if (_cwl == 0)
                {
                    if (_inStatus || _spans > 0)
                    {
                        throw new RuntimeError(7);
                    }

                    // [aam opcode] The divs are left before the clear
                    // and entered again after it, so that what the
                    // story is inside survives being wiped.
                    var open = _divs.ToArray();

                    ClearOutputState();

                    if (operation == 0x06)
                    {
                        _output.Clear();
                    }
                    else
                    {
                        _output.ClearAll();
                    }

                    foreach (var styleClass in open)
                    {
                        _output.EnterDiv(styleClass);
                    }

                    _divs.AddRange(open);
                }

                break;

            case 0x08:
                if (!_output.ScriptOn())
                {
                    Fail();
                }

                break;

            case 0x09:
                _output.ScriptOff();
                break;

            case 0x0a:
            case 0x0b:
                // Tracing is for an interpreter that can show it, and
                // this one says nothing.
                break;

            case 0x0c:
                _cwl++;
                break;

            case 0x0d:
                _cwl--;
                break;

            case 0x0e:
                if (_cwl == 0)
                {
                    _upper = true;
                }

                break;

            case 0x0f:
                _output.ClearLinks();
                break;

            case 0x10:
                if (_spans > 0)
                {
                    throw new RuntimeError(7);
                }

                _output.ClearOld();
                break;

            case 0x11:
                _output.ClearDiv();
                break;

            case 0x12:
                if (_cwl == 0)
                {
                    if (_inStatus || _spans > 0)
                    {
                        throw new RuntimeError(7);
                    }

                    _output.ClearStatus();
                }

                break;

            case 0x13:
                Ask(Spacing.NoBreakSpace);
                break;

            default:
                throw new AaMachineException($"The extended operation {operation:x2} is not implemented.");
        }

        return null;
    }

    // [aam opcode] What the story is allowed to ask about the machine
    // it finds itself in.
    private ushort Information(int selector)
    {
        // The features, which answer with a plain one or nothing.
        if ((selector & 0x40) != 0)
        {
            var supported = (selector & 0x3f) switch
            {
                0x00 => true,
                0x01 => true,
                0x02 => _output.HasLinks,
                0x03 => true,
                0x04 => _output.HasStyles,
                0x05 => _output.HasColor,
                0x06 => _output.HasAlignment,
                0x10 => _output.IsScripting,
                0x20 => false,
                0x21 => false,

                // [aam opcode] Anything unrecognized below $80 answers
                // no rather than refusing the question.
                _ => false,
            };

            return supported ? (ushort)1 : AaValue.Null;
        }

        // How wide or how tall the area being written to is.
        if ((selector & 0x20) != 0)
        {
            return AaValue.Number(_output.Measure(selector & 0x1f));
        }

        // [aam opcode] How much of each memory has ever been touched,
        // which is measured by counting what is no longer marked
        // unused.
        var used = selector switch
        {
            0 => Count(_heap),
            1 => Count(_aux),
            2 => Count(_ram.AsSpan(_longTermBottom)),
            _ => 0,
        };

        return AaValue.Number(used);
    }

    private static int Count(ReadOnlySpan<ushort> memory)
    {
        var used = 0;

        foreach (var word in memory)
        {
            if (word != AaValue.Unused)
            {
                used++;
            }
        }

        return used;
    }

    // [aam output] Winding the frontend back to nothing: out of every
    // span, link, div and status area at once. Everything that starts
    // the story over goes through here, and so does a clear.
    private void ClearOutputState()
    {
        _output.LeaveAll();

        _inStatus = false;
        _spans = 0;
        _links = 0;
        _divs.Clear();
    }

    // [aam opcode] Before the story waits for the player, whatever it
    // was in the middle of saying has to be on the screen.
    private void BeforeInput()
    {
        Gap(before: true);
        _output.Sync();
    }
}

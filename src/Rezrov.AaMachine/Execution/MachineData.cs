using Rezrov.AaMachine.Instructions;

namespace Rezrov.AaMachine.Execution;

/// <summary>
/// Lists, pairs, and the two places a value can be put away: the
/// auxiliary stack, and the long-term heap inside the game's own
/// memory.
/// </summary>
public sealed partial class Machine
{
    // [aam opcode] MAKE_PAIR runs both ways. Asked to store, it builds
    // a pair and hands back its two halves. Asked to unify, it takes a
    // pair apart, or builds one if what it was given is still unbound.
    private void Pair(ReadOnlySpan<Operand> ops)
    {
        // The three-operand literal forms write their first operand
        // into the cell instead of handing a variable back.
        var literal = ops[0].Kind != OperandKind.Dest;
        var head = ops[^3];
        var tail = ops[^2];
        var whole = ops[^1];

        if (!whole.Unify)
        {
            var built = Allocate(2);

            HalfPair(literal, head, built);
            HalfPair(false, tail, built + 1);
            Store(whole, AaValue.Pair(built));
            return;
        }

        var existing = Deref(Value(whole));

        if (AaValue.IsPair(existing))
        {
            // A pair taken apart hands back a variable for each of
            // its cells, which is the same word with the tag changed
            // from pair to reference.
            var cell = AaValue.Reference(existing & 0x1fff);

            if (literal)
            {
                if (!Unify(Value(head), cell))
                {
                    Fail();
                }
            }
            else
            {
                Store(head, cell);
            }

            Store(tail, AaValue.Reference((existing & 0x1fff) + 1));
            return;
        }

        if (!AaValue.IsReference(existing))
        {
            Fail();
            return;
        }

        var address = Allocate(2);

        HalfPair(literal, head, address);
        HalfPair(false, tail, address + 1);

        if (!Unify(existing, AaValue.Pair(address)))
        {
            Fail();
        }
    }

    // One half of a new pair: a literal goes straight in, a
    // destination that unifies contributes whatever it holds, and a
    // destination that stores is handed a variable pointing at the
    // cell.
    private void HalfPair(bool literal, Operand operand, int cell)
    {
        if (literal || operand.Unify)
        {
            _heap[cell] = Value(operand);
            return;
        }

        _heap[cell] = AaValue.Null;
        Store(operand, AaValue.Reference(cell));
    }

    // [aam opcode] Flattening a value onto the auxiliary stack, where
    // it survives the heap being given back at a choice point. A list
    // goes on head first with its length last, so that popping it
    // rebuilds it in order.
    private void PushSerialized(ushort value)
    {
        value = Deref(value);

        if (AaValue.IsPair(value))
        {
            var count = 0;

            while (true)
            {
                PushSerialized(AaValue.Reference(value & 0x1fff));
                count++;

                value = Deref(AaValue.Reference((value & 0x1fff) + 1));

                if (value == AaValue.Empty)
                {
                    value = (ushort)(0xc000 + count);
                    break;
                }

                if (!AaValue.IsPair(value))
                {
                    // A list that does not end in the empty list is
                    // improper, and its last value goes on too.
                    PushSerialized(value);
                    value = (ushort)(0xe000 + count);
                    break;
                }
            }
        }
        else if (AaValue.IsExtDict(value))
        {
            PushSerialized(_heap[(value & 0x1fff) + 1]);
            PushSerialized(_heap[value & 0x1fff]);
            value = 0x8100;
        }
        else if (AaValue.IsReference(value))
        {
            // An unbound variable keeps no identity across this: it
            // comes back as a fresh one.
            value = 0x8000;
        }

        PushAux(value);
    }

    private ushort PopSerialized()
    {
        var value = _aux[--_auxTop];

        if (value == 0x8000)
        {
            var address = Allocate(1);

            _heap[address] = AaValue.Null;

            return AaValue.Reference(address);
        }

        if (value == 0x8100)
        {
            var address = Allocate(2);

            _heap[address] = PopSerialized();
            _heap[address + 1] = PopSerialized();

            return AaValue.ExtDict(address);
        }

        if ((value & 0xc000) == 0xc000)
        {
            var count = value & 0x1fff;
            var list = (value & 0x2000) != 0 ? PopSerialized() : AaValue.Empty;

            while (count-- > 0)
            {
                var address = Allocate(2);

                _heap[address] = PopSerialized();
                _heap[address + 1] = list;
                list = AaValue.Pair(address);
            }

            return list;
        }

        return value;
    }

    private ushort PopSerializedList()
    {
        var list = AaValue.Empty;

        while (PopSerialized() is var value && value != AaValue.Null)
        {
            var address = Allocate(2);

            _heap[address] = value;
            _heap[address + 1] = list;
            list = AaValue.Pair(address);
        }

        return list;
    }

    // [aam opcode] Empties the auxiliary stack down to its marker and
    // says whether the value was among what came off.
    private bool PopListContains(ushort key)
    {
        var found = false;

        while (_aux[--_auxTop] is var value && value != AaValue.Null)
        {
            if (value == key)
            {
                found = true;
            }
        }

        return found;
    }

    // [aam opcode] Whether every part of the key could match
    // something on the stack. The heap this borrows is given straight
    // back, since nothing built here is kept.
    private bool PopListMatches(ushort key)
    {
        var mark = _top;
        var list = PopSerializedList();

        while (AaValue.IsPair(key))
        {
            var candidate = list;
            var matched = false;

            while (AaValue.IsPair(candidate) && !matched)
            {
                if (WouldUnify(
                    AaValue.Reference(candidate & 0x1fff),
                    AaValue.Reference(key & 0x1fff)))
                {
                    matched = true;
                }

                candidate = _heap[(candidate & 0x1fff) + 1];
            }

            if (!matched)
            {
                return false;
            }

            key = _heap[(key & 0x1fff) + 1];
        }

        _top = mark;

        return true;
    }

    // [aam opcode] A fresh copy of the front of a list, up to the
    // point where the second list begins.
    private ushort SplitList(ushort list, ushort end)
    {
        list = Deref(list);
        end = Deref(end);

        if (list == end || !AaValue.IsPair(list))
        {
            return AaValue.Empty;
        }

        var first = Allocate(2);
        var current = first;

        while (true)
        {
            _heap[current] = _heap[list & 0x1fff];
            list = Deref(_heap[(list & 0x1fff) + 1]);

            if (list == end || !AaValue.IsPair(list))
            {
                break;
            }

            var next = Allocate(2);

            _heap[current + 1] = AaValue.Pair(next);
            current = next;
        }

        _heap[current + 1] = AaValue.Empty;

        return AaValue.Pair(first);
    }

    // [aam opcode] A word taken to pieces: a character on its own, a
    // dictionary word spelled out, or a number's digits.
    private ushort? SplitWord(ushort value)
    {
        switch (AaValue.Tag(value))
        {
            case AaTag.Character:
                return MakePair(value, AaValue.Empty);

            case AaTag.Word:
                return PrependCharacters(value, AaValue.Empty);

            case AaTag.ExtDict:
            {
                var known = _heap[value & 0x1fff];

                return AaValue.IsPair(known)
                    ? known
                    : PrependCharacters(known, _heap[(value & 0x1fff) + 1]);
            }

            case AaTag.Number:
            {
                var number = AaValue.Value(value);
                var digits = AaValue.Empty;

                do
                {
                    digits = MakePair(AaValue.Number(number % 10), digits);
                    number /= 10;
                }
                while (number != 0);

                return digits;
            }

            default:
                return null;
        }
    }

    private ushort PrependCharacters(ushort word, ushort list)
    {
        var characters = new List<byte>();

        WordCharacters(word, characters);

        for (var i = characters.Count - 1; i >= 0; i--)
        {
            // [aam opcode deviates] A digit comes out as a number
            // rather than as a character. The specification's
            // pseudocode says character, but the reference
            // interpreter, which the conformance transcripts were
            // recorded from, says number, and so does the parser when
            // it splits a word the same way.
            var character = characters[i];

            list = MakePair(
                character is >= (byte)'0' and <= (byte)'9'
                    ? AaValue.Number(character - '0')
                    : AaValue.Character(character),
                list);
        }

        return list;
    }

    // [aam opcode] A list of characters and words put back together
    // into one word, which is how the parser rejoins what a player
    // typed with punctuation in the middle of it.
    private ushort? JoinWords(ushort value)
    {
        if (!AaValue.IsPair(value))
        {
            return null;
        }

        var head = Deref(_heap[value & 0x1fff]);
        var tail = Deref(_heap[(value & 0x1fff) + 1]);

        // A stop character on its own stays what it is.
        if (AaValue.Tag(head) == AaTag.Character && tail == AaValue.Empty)
        {
            return head;
        }

        var characters = new List<byte>();

        return WordListCharacters(value, characters) ? ParseWord(characters) : null;
    }

    private bool WordListCharacters(ushort list, List<byte> into)
    {
        do
        {
            var value = Deref(_heap[list & 0x1fff]);

            switch (AaValue.Tag(value))
            {
                case AaTag.Character:
                {
                    var character = AaValue.Value(value);

                    // Whitespace and punctuation cannot be in the
                    // middle of a word, so this is not one.
                    if (character <= 0x20 || _story.Language.StopCharacters.Contains((byte)character))
                    {
                        return false;
                    }

                    into.Add((byte)character);
                    break;
                }

                case AaTag.Word:
                    into.AddRange(_story.Dictionary.Characters(AaValue.Value(value)));
                    break;

                case AaTag.ExtDict:
                    // A known part that is itself a list is followed;
                    // otherwise the node is read as a list of its own,
                    // whose head is the known word.
                    if (!WordListCharacters(
                        _heap[value & 0x1fff] >= 0x8000 ? _heap[value & 0x1fff] : value,
                        into))
                    {
                        return false;
                    }

                    break;

                case AaTag.Number:
                    foreach (var digit in AaValue.Value(value).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    {
                        into.Add((byte)digit);
                    }

                    break;

                default:
                    return false;
            }

            list = Deref(_heap[(list & 0x1fff) + 1]);
        }
        while (AaValue.IsPair(list));

        return list == AaValue.Empty;
    }

    // The characters a word is spelled with, following an unknown
    // word into the part of it that is known and the part that is not.
    private void WordCharacters(ushort value, List<byte> into)
    {
        if (AaValue.Tag(value) == AaTag.Word)
        {
            into.AddRange(_story.Dictionary.Characters(AaValue.Value(value)));
            return;
        }

        if (!AaValue.IsExtDict(value))
        {
            return;
        }

        var known = _heap[value & 0x1fff];

        if (AaValue.IsPair(known))
        {
            while (AaValue.IsPair(known))
            {
                into.Add((byte)AaValue.Value(_heap[known & 0x1fff]));
                known = _heap[(known & 0x1fff) + 1];
            }

            return;
        }

        WordCharacters(known, into);

        var rest = _heap[(value & 0x1fff) + 1];

        while (AaValue.IsPair(rest))
        {
            into.Add((byte)AaValue.Value(_heap[rest & 0x1fff]));
            rest = _heap[(rest & 0x1fff) + 1];
        }
    }

    // [aam opcode] A field too big to sit in one word is kept in the
    // long-term heap and named from the field, with the high bit set.
    private ushort LongTerm(ushort value)
    {
        if ((value & 0x8000) == 0)
        {
            return value;
        }

        _tmp = value & 0x7fff;
        _tmp += _ram[_tmp];

        return PopLongTerm();
    }

    private ushort PopLongTerm()
    {
        var value = _ram[--_tmp];

        if (value == 0x8000)
        {
            var address = Allocate(1);

            _heap[address] = AaValue.Null;

            return AaValue.Reference(address);
        }

        if (value == 0x8100)
        {
            var address = Allocate(2);

            _heap[address] = PopLongTerm();
            _heap[address + 1] = PopLongTerm();

            return AaValue.ExtDict(address);
        }

        if ((value & 0xc000) == 0xc000)
        {
            var count = value & 0x1fff;
            var list = (value & 0x2000) != 0 ? PopLongTerm() : AaValue.Empty;

            while (count-- > 0)
            {
                var address = Allocate(2);

                _heap[address] = PopLongTerm();
                _heap[address + 1] = list;
                list = AaValue.Pair(address);
            }

            return list;
        }

        return value;
    }

    private void StoreLongTermField(ReadOnlySpan<Operand> ops)
    {
        var subject = Subject(ops);
        var value = Value(ops[^1]);

        // Clearing a field of something that is not an object is
        // meaningless, so it is only done when there is something to
        // put there.
        if (subject == AaValue.Null || AaValue.Tag(subject) == AaTag.Thing || value != AaValue.Null)
        {
            StoreLongTerm(FieldAddress(Value(ops[^2]), subject), value);
        }
    }

    private void StoreLongTerm(int address, ushort value)
    {
        ClearLongTerm(address);

        value = Deref(value);

        if (!AaValue.IsPair(value) && !AaValue.IsExtDict(value) && !AaValue.IsReference(value))
        {
            _ram[address] = value;
            return;
        }

        _tmp = _longTermTop + 2;

        if (_tmp > _ram.Length)
        {
            throw new RuntimeError(6);
        }

        PushLongTerm(value);

        _ram[address] = (ushort)(0x8000 + _longTermTop);
        _ram[_longTermTop] = (ushort)(_tmp - _longTermTop);
        _ram[_longTermTop + 1] = (ushort)address;
        _longTermTop = (ushort)_tmp;
    }

    private void PushLongTerm(ushort value)
    {
        value = Deref(value);

        if (AaValue.IsPair(value))
        {
            var count = 0;

            while (true)
            {
                PushLongTerm(_heap[value & 0x1fff]);
                count++;

                value = Deref(_heap[(value & 0x1fff) + 1]);

                if (value == AaValue.Empty)
                {
                    value = (ushort)(0xc000 | count);
                    break;
                }

                if (!AaValue.IsPair(value))
                {
                    PushLongTerm(value);
                    value = (ushort)(0xe000 | count);
                    break;
                }
            }
        }
        else if (AaValue.IsExtDict(value))
        {
            PushLongTerm(_heap[(value & 0x1fff) + 1]);
            PushLongTerm(_heap[value & 0x1fff]);
            value = 0x8100;
        }
        else if (AaValue.IsReference(value))
        {
            // Nothing unbound can be kept: it would name a heap cell
            // that will not be there when it is read back.
            throw new RuntimeError(4);
        }

        if (_tmp >= _ram.Length)
        {
            throw new RuntimeError(6);
        }

        _ram[_tmp++] = value;
    }

    // Taking a chunk out of the long-term heap closes the gap and
    // moves everything above it down, so every field that named one
    // of those chunks has to be told where it went.
    private void ClearLongTerm(int address)
    {
        var value = _ram[address];

        if ((value & 0x8000) == 0)
        {
            return;
        }

        _ram[address] = AaValue.Null;

        var at = value & 0x7fff;
        var size = _ram[at];

        for (var i = at; i < _longTermTop - size; i++)
        {
            _ram[i] = _ram[i + size];
        }

        _longTermTop -= size;

        while (at < _longTermTop)
        {
            _ram[_ram[at + 1]] -= size;
            at += _ram[at];
        }
    }
}

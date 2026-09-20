namespace Rezrov.AaMachine.Execution;

/// <summary>
/// Turning what the player typed into something the story can reason
/// about.
/// </summary>
/// <remarks>
/// [aam opcode] A line of input becomes a list of values, one for each
/// word: a dictionary word where the game knows it, a number where it
/// is one, a character where it is a single one, and otherwise a word
/// the game does not know, kept as whatever part of it the game does
/// know and the letters left over. That last kind is what lets a game
/// understand "lanterns" when it was only told about "lantern".
/// </remarks>
public sealed partial class Machine
{
    private ushort ParseInput(string line)
    {
        var characters = new byte[line.Length];

        for (var i = 0; i < line.Length; i++)
        {
            characters[i] = FromUnicode(line[i]);
        }

        // [aam opcode] Spaces part one word from the next, and every
        // stop character is a word of its own, so that "ball.north" is
        // three words rather than one.
        var words = new List<ArraySegment<byte>>();
        var start = 0;

        for (var i = 0; i < characters.Length; i++)
        {
            if (characters[i] == ' ')
            {
                if (i != start)
                {
                    words.Add(new ArraySegment<byte>(characters, start, i - start));
                }

                start = i + 1;
            }
            else if (_story.Language.StopCharacters.Contains(characters[i]))
            {
                if (i != start)
                {
                    words.Add(new ArraySegment<byte>(characters, start, i - start));
                }

                words.Add(new ArraySegment<byte>(characters, i, 1));
                start = i + 1;
            }
        }

        if (start != characters.Length)
        {
            words.Add(new ArraySegment<byte>(characters, start, characters.Length - start));
        }

        var list = AaValue.Empty;

        for (var i = words.Count - 1; i >= 0; i--)
        {
            list = MakePair(ParseWord(words[i]), list);
        }

        return list;
    }

    // [aam text] A character the player typed, in the game's own set
    // and in lowercase. Anything the game has no character for becomes
    // a question mark, which will not be in the dictionary either.
    private byte FromUnicode(char character)
    {
        if (character is >= 'A' and <= 'Z')
        {
            return (byte)(character + 32);
        }

        if (character < 0x80)
        {
            return (byte)character;
        }

        var set = _story.Language.Characters;

        for (var i = set.Count - 1; i >= 0; i--)
        {
            if (set[i].Codepoint == character)
            {
                return set[i].Lower;
            }
        }

        return (byte)'?';
    }

    private ushort ParseWord(IReadOnlyList<byte> characters)
    {
        var word = new byte[characters.Count];

        for (var i = 0; i < word.Length; i++)
        {
            word[i] = characters[i];
        }

        // A word the game knows outright is the commonest case and is
        // looked for first.
        if (word.Length > 1 && Lookup(word, word.Length) is { } known)
        {
            return AaValue.Word(known);
        }

        // [aam opcode] Then a number, which is any run of digits that
        // will fit, and which is why a single digit comes back as a
        // number rather than as a character.
        var value = 0;
        var digits = 0;

        while (digits < word.Length && word[digits] is >= (byte)'0' and <= (byte)'9')
        {
            value = (value * 10) + (word[digits] - '0');
            digits++;

            if (value > AaValue.MaxNumber)
            {
                break;
            }
        }

        if (digits == word.Length)
        {
            return AaValue.Number(value);
        }

        if (word.Length == 1)
        {
            return AaValue.Character(word[0]);
        }

        return UnknownWord(word);
    }

    // [aam story] The word endings decoder: a little program that
    // takes one letter at a time off the end of a word and tries the
    // dictionary again, so that a game told about "lantern" can be
    // handed "lanterns" and know what to do with it.
    private ushort UnknownWord(byte[] word)
    {
        var decoder = _story.Language.WordEndings;
        var ending = new List<byte>();
        var state = 0;
        var length = word.Length;

        while (true)
        {
            var instruction = state < decoder.Length ? decoder[state++] : (byte)0;

            if (instruction == 0)
            {
                // Nothing worked, so the whole word is kept as its own
                // letters and the game is left to make of it what it
                // can.
                while (length > 0)
                {
                    ending.Insert(0, word[--length]);
                }

                return ExtendedWord(Letters(ending), AaValue.Empty);
            }

            if (instruction == 1)
            {
                if (Lookup(word, length) is { } known)
                {
                    return ExtendedWord(AaValue.Word(known), Letters(ending));
                }

                continue;
            }

            var next = state < decoder.Length ? decoder[state++] : (byte)0;

            // A word has to keep at least two letters of itself, or
            // there would be nothing left to look up.
            if (length > 2 && instruction == word[length - 1])
            {
                ending.Insert(0, instruction);
                length--;
                state = next;
            }
        }
    }

    private ushort ExtendedWord(ushort known, ushort rest)
    {
        var address = Allocate(2);

        _heap[address] = known;
        _heap[address + 1] = rest;

        return AaValue.ExtDict(address);
    }

    private ushort Letters(List<byte> characters)
    {
        var list = AaValue.Empty;

        for (var i = characters.Count - 1; i >= 0; i--)
        {
            var character = characters[i];

            list = MakePair(
                character is >= (byte)'0' and <= (byte)'9'
                    ? AaValue.Number(character - '0')
                    : AaValue.Character(character),
                list);
        }

        return list;
    }

    // [aam story] The dictionary is sorted by the characters of its
    // words, so a word is found by halving.
    private int? Lookup(byte[] word, int length)
    {
        var low = 0;
        var high = _story.Dictionary.Count;

        while (low < high)
        {
            var middle = (low + high) / 2;
            var order = Compare(word.AsSpan(0, length), _story.Dictionary.Characters(middle));

            if (order == 0)
            {
                return middle;
            }

            if (order < 0)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return null;
    }

    private static int Compare(ReadOnlySpan<byte> word, ReadOnlySpan<byte> entry)
    {
        var shared = Math.Min(word.Length, entry.Length);

        for (var i = 0; i < shared; i++)
        {
            if (word[i] != entry[i])
            {
                return word[i] - entry[i];
            }
        }

        return word.Length - entry.Length;
    }
}

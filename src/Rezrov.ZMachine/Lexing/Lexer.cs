using Rezrov.ZMachine.Text;

namespace Rezrov.ZMachine.Lexing;

/// <summary>
/// Lexical analysis: breaking typed text into words and looking each one
/// up in a dictionary.
/// </summary>
/// <remarks>
/// [zm 13.6] This happens both when a game command is accepted and when
/// the tokenise opcode asks for it, and the only difference is which
/// dictionary is used, so the dictionary is a parameter here.
/// </remarks>
public static class Lexer
{
    /// <summary>
    /// Breaks text into words and looks each one up.
    /// </summary>
    /// <remarks>
    /// [zm 13.6.2] Each word is encoded in dictionary form and searched
    /// for. [zm 3.7] Typed text is converted to lower case first, which is
    /// done here for the ASCII letters. Words are then matched exactly as
    /// the encoder produces them, so a long word matches the dictionary
    /// entry it truncates to, which is what makes "lanter" and "lantern"
    /// the same word to a Version 3 game.
    /// </remarks>
    public static List<Token> Analyze(ReadOnlySpan<byte> text, DictionaryTable dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        var tokens = new List<Token>();

        foreach (var (start, length) in Split(text, dictionary.WordSeparators))
        {
            var word = text.Slice(start, length).ToArray();
            for (var i = 0; i < word.Length; i++)
            {
                if (word[i] is >= (byte)'A' and <= (byte)'Z')
                {
                    word[i] += 'a' - 'A';
                }
            }

            tokens.Add(new Token(start, length, dictionary.Lookup(word)));
        }

        return tokens;
    }

    /// <summary>
    /// Breaks text into words, returning where each begins and how long
    /// it is.
    /// </summary>
    /// <remarks>
    /// [zm 13.6.1] Spaces divide words and are otherwise ignored. Word
    /// separators divide words too, but each one is a word in its own
    /// right, so "fred,go fishing" is four words: fred, the comma, go,
    /// and fishing.
    /// </remarks>
    public static List<(int Start, int Length)> Split(ReadOnlySpan<byte> text, ReadOnlySpan<byte> separators)
    {
        var words = new List<(int Start, int Length)>();
        var start = -1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var isSpace = c == Zscii.Space;

            if (!isSpace && !separators.Contains(c))
            {
                if (start < 0)
                {
                    start = i;
                }

                continue;
            }

            if (start >= 0)
            {
                words.Add((start, i - start));
                start = -1;
            }

            if (!isSpace)
            {
                words.Add((i, 1));
            }
        }

        if (start >= 0)
        {
            words.Add((start, text.Length - start));
        }

        return words;
    }
}

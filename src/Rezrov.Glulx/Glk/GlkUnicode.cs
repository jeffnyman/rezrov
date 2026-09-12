using System.Text;

namespace Rezrov.Glulx.Glk;

/// <summary>
/// [glk #encoding_hilo] and [glk #encoding_uninorm] The Unicode case
/// changes and canonical normalizations, on the code points of a
/// buffer as the Glk buffer functions take them.
/// </summary>
/// <remarks>
/// The one-to-one case mappings come from the runtime, whose invariant
/// globalization mode still carries the Unicode tables for them. The
/// mappings that change how many characters there are, the combining
/// classes, and the decompositions are the tables in
/// <see cref="GlkUnicodeData"/>. Every function returns a new array,
/// which can be longer or shorter than the text it was given, and a
/// value that is not a Unicode scalar goes through as it is.
/// </remarks>
public static class GlkUnicode
{
    // [glk #encoding_uninorm] The Hangul syllables are not in the
    // tables: Unicode chapter 3.12 builds each from a leading consonant,
    // a vowel, and perhaps a trailing consonant by arithmetic.
    private const uint SyllableFirst = 0xAC00;
    private const uint SyllableLast = 0xD7A3;
    private const uint LeadFirst = 0x1100;
    private const uint VowelFirst = 0x1161;
    private const uint TrailFirst = 0x11A7;
    private const uint LeadCount = 19;
    private const uint VowelCount = 21;
    private const uint TrailCount = 28;

    private enum CaseForm
    {
        Lower,
        Upper,
        Title,
    }

    /// <summary>
    /// [glk #encoding_hilo] Every character to its lower-case
    /// equivalent, where it has one.
    /// </summary>
    public static uint[] ToLowerCase(ReadOnlySpan<uint> text) => ChangeCase(text, CaseForm.Lower, CaseForm.Lower);

    /// <summary>
    /// [glk #encoding_hilo] Every character to its upper-case
    /// equivalent, where it has one; a few become two or three
    /// characters, the way the sharp s becomes "SS".
    /// </summary>
    public static uint[] ToUpperCase(ReadOnlySpan<uint> text) => ChangeCase(text, CaseForm.Upper, CaseForm.Upper);

    /// <summary>
    /// [glk #encoding_hilo] The first character to title case, which
    /// is upper case except for the few characters, like the "dz"
    /// digraph with a caron and the "ffi" ligature, that have a form
    /// of their own for the start of a word; the rest of the text is
    /// lower-cased when <paramref name="lowerRest"/> is set and left
    /// alone when it is not.
    /// </summary>
    public static uint[] ToTitleCase(ReadOnlySpan<uint> text, bool lowerRest) =>
        ChangeCase(text, CaseForm.Title, lowerRest ? CaseForm.Lower : null);

    /// <summary>
    /// [glk #encoding_uninorm] Normalization Form D: every character
    /// taken apart into its canonical decomposition, and every run of
    /// combining marks put into canonical order.
    /// </summary>
    public static uint[] Decompose(ReadOnlySpan<uint> text)
    {
        var output = new List<uint>(text.Length);
        foreach (var character in text)
        {
            AppendDecomposition(output, character);
        }

        OrderCombiningMarks(output);
        return [.. output];
    }

    /// <summary>
    /// [glk #encoding_uninorm] Normalization Form C: the decomposition,
    /// and then each starter put back together with the marks after
    /// it, as far as the standard's canonical composition goes.
    /// </summary>
    public static uint[] Normalize(ReadOnlySpan<uint> text)
    {
        var characters = Decompose(text);
        if (characters.Length == 0)
        {
            return characters;
        }

        // Unicode chapter 3.11, the canonical composition algorithm: a
        // mark composes with the last starter unless a mark between
        // them has a class at least as high, which blocks it; a
        // starter composes with the last starter only when nothing
        // came between them at all.
        var starterAt = 0;
        var starter = characters[0];
        var lastClass = CombiningClass(starter) == 0 ? 0 : 256;
        var written = 1;

        for (var i = 1; i < characters.Length; i++)
        {
            var character = characters[i];
            var characterClass = CombiningClass(character);
            var composite = Compose(starter, character);

            if (composite != 0 && (lastClass < characterClass || lastClass == 0))
            {
                characters[starterAt] = composite;
                starter = composite;
                continue;
            }

            if (characterClass == 0)
            {
                starterAt = written;
                starter = character;
            }

            lastClass = characterClass;
            characters[written++] = character;
        }

        return characters[..written];
    }

    /// <summary>
    /// The canonical combining class of a character: zero for a
    /// starter, and the class that orders it among other marks
    /// otherwise.
    /// </summary>
    public static int CombiningClass(uint character)
    {
        var table = GlkUnicodeData.CombiningClasses;
        var low = 0;
        var high = (table.Length / 3) - 1;

        while (low <= high)
        {
            var middle = (low + high) / 2;
            var run = table.Slice(middle * 3, 3);
            if (character < run[0])
            {
                high = middle - 1;
            }
            else if (character > run[1])
            {
                low = middle + 1;
            }
            else
            {
                return (int)run[2];
            }
        }

        return 0;
    }

    private static uint[] ChangeCase(ReadOnlySpan<uint> text, CaseForm first, CaseForm? rest)
    {
        var output = new List<uint>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var form = i == 0 ? first : rest;
            if (form is null)
            {
                output.Add(text[i]);
            }
            else
            {
                AppendCase(output, text[i], form.Value);
            }
        }

        return [.. output];
    }

    private static void AppendCase(List<uint> output, uint character, CaseForm form)
    {
        if (!Rune.IsValid(character))
        {
            output.Add(character);
            return;
        }

        var special = SpecialCase(character, form);
        if (special.Length > 0)
        {
            output.AddRange(special);
            return;
        }

        var rune = new Rune(character);
        var changed = form == CaseForm.Lower ? Rune.ToLowerInvariant(rune) : Rune.ToUpperInvariant(rune);
        output.Add((uint)changed.Value);
    }

    /// <summary>
    /// The full case mapping of a character when it is not the
    /// one-to-one one, and nothing otherwise. A title mapping falls
    /// back to the upper one, since they differ for few characters.
    /// </summary>
    private static ReadOnlySpan<uint> SpecialCase(uint character, CaseForm form)
    {
        var row = FindRow(GlkUnicodeData.SpecialCases, 4, character);
        if (row < 0)
        {
            return ReadOnlySpan<uint>.Empty;
        }

        var entry = GlkUnicodeData.SpecialCases.Slice(row, 4);
        var offset = form switch
        {
            CaseForm.Lower => entry[2],
            CaseForm.Upper => entry[1],
            _ => entry[3] != 0 ? entry[3] : entry[1],
        };

        return offset == 0 ? ReadOnlySpan<uint>.Empty : Parts(GlkUnicodeData.SpecialCaseParts, offset);
    }

    private static void AppendDecomposition(List<uint> output, uint character)
    {
        if (character is >= SyllableFirst and <= SyllableLast)
        {
            var index = character - SyllableFirst;
            output.Add(LeadFirst + (index / (VowelCount * TrailCount)));
            output.Add(VowelFirst + (index % (VowelCount * TrailCount) / TrailCount));
            var trail = index % TrailCount;
            if (trail != 0)
            {
                output.Add(TrailFirst + trail);
            }

            return;
        }

        var row = FindRow(GlkUnicodeData.Decompositions, 2, character);
        if (row < 0)
        {
            output.Add(character);
            return;
        }

        output.AddRange(Parts(GlkUnicodeData.DecompositionParts, GlkUnicodeData.Decompositions[row + 1]));
    }

    /// <summary>
    /// Unicode chapter 3.11, canonical ordering: each run of marks is
    /// sorted by class, with marks of one class kept in the order they
    /// came. An insertion sort does that, and never moves a mark past
    /// a starter, whose class of zero is below every mark's.
    /// </summary>
    private static void OrderCombiningMarks(List<uint> text)
    {
        for (var i = 1; i < text.Count; i++)
        {
            var mark = text[i];
            var markClass = CombiningClass(mark);
            if (markClass == 0)
            {
                continue;
            }

            var at = i;
            while (at > 0 && CombiningClass(text[at - 1]) > markClass)
            {
                text[at] = text[at - 1];
                at--;
            }

            text[at] = mark;
        }
    }

    /// <summary>
    /// The character two characters compose into, or zero when they
    /// do not compose.
    /// </summary>
    private static uint Compose(uint first, uint second)
    {
        if (first is >= LeadFirst and < LeadFirst + LeadCount && second is >= VowelFirst and < VowelFirst + VowelCount)
        {
            return SyllableFirst + ((((first - LeadFirst) * VowelCount) + (second - VowelFirst)) * TrailCount);
        }

        if (first is >= SyllableFirst and <= SyllableLast && (first - SyllableFirst) % TrailCount == 0 && second is > TrailFirst and < TrailFirst + TrailCount)
        {
            return first + (second - TrailFirst);
        }

        var table = GlkUnicodeData.Compositions;
        var low = 0;
        var high = (table.Length / 3) - 1;

        while (low <= high)
        {
            var middle = (low + high) / 2;
            var pair = table.Slice(middle * 3, 3);
            var order = pair[0] != first ? pair[0].CompareTo(first) : pair[1].CompareTo(second);
            if (order == 0)
            {
                return pair[2];
            }

            if (order < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// The start of the row of a table whose first word is the key,
    /// or -1; the table is rows of <paramref name="stride"/> words
    /// sorted by their first.
    /// </summary>
    private static int FindRow(ReadOnlySpan<uint> table, int stride, uint key)
    {
        var low = 0;
        var high = (table.Length / stride) - 1;

        while (low <= high)
        {
            var middle = (low + high) / 2;
            var found = table[middle * stride];
            if (found == key)
            {
                return middle * stride;
            }

            if (found < key)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }

    /// <summary>
    /// The characters a pool entry holds: the count at the offset,
    /// then that many characters.
    /// </summary>
    private static ReadOnlySpan<uint> Parts(ReadOnlySpan<uint> pool, uint offset) =>
        pool.Slice((int)offset + 1, (int)pool[(int)offset]);
}

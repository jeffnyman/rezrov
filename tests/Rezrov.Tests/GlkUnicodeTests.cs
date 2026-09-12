using Rezrov.Glulx.Glk;

namespace Rezrov.Tests;

/// <summary>
/// [glk #encoding_hilo] and [glk #encoding_uninorm] The Unicode case
/// changes and canonical normalizations behind the buffer functions.
/// </summary>
public class GlkUnicodeTests
{
    private static uint[] Text(string text) => [.. text.EnumerateRunes().Select(rune => (uint)rune.Value)];

    [Theory]
    [InlineData("Ordinary 'line' of WORDS.", "ORDINARY 'LINE' OF WORDS.")]
    [InlineData("àøþ", "ÀØÞ")]
    [InlineData("πμб", "ΠΜБ")]
    [InlineData("ǆ", "Ǆ")]
    [InlineData("ẖ", "H̱")]
    public void UpperCaseMapsEveryCharacterWithAnUpperCaseForm(string text, string expected)
    {
        // [glk #encoding_hilo] Latin-1, Greek, Cyrillic, a digraph, and a
        // letter whose upper-case form is a letter and a mark.
        Assert.Equal(Text(expected), GlkUnicode.ToUpperCase(Text(text)));
    }

    [Theory]
    [InlineData("ß", "SS")]
    [InlineData("ﬃ", "FFI")]
    [InlineData("ΐ", "Ϊ́")]
    [InlineData("ᾀ", "ἈΙ")]
    [InlineData("aßb", "ASSB")]
    public void UpperCaseCanMakeMoreCharactersThanItWasGiven(string text, string expected)
    {
        // [glk #encoding_hilo] The sharp s, a ligature, a Greek letter
        // with two marks, and a letter with the iota subscript all
        // become more than one character.
        Assert.Equal(Text(expected), GlkUnicode.ToUpperCase(Text(text)));
    }

    [Theory]
    [InlineData("Ordinary 'line' of WORDS.", "ordinary 'line' of words.")]
    [InlineData("ÀØÞ", "àøþ")]
    [InlineData("ǅ", "ǆ")]
    [InlineData("İ", "i̇")]
    [InlineData("ﬃ", "ﬃ")]
    public void LowerCaseMapsEveryCharacterWithALowerCaseForm(string text, string expected)
    {
        // [glk #encoding_hilo] The capital I with a dot above is the one
        // character whose lower-case form is two; a ligature has no
        // lower-case form but itself.
        Assert.Equal(Text(expected), GlkUnicode.ToLowerCase(Text(text)));
    }

    [Theory]
    [InlineData("hello WORLD", false, "Hello WORLD")]
    [InlineData("hello WORLD", true, "Hello world")]
    [InlineData("ǄǄ.", false, "ǅǄ.")]
    [InlineData("ǄǄ.", true, "ǅǆ.")]
    [InlineData("ǆǆ.", false, "ǅǆ.")]
    [InlineData("ﬄﬁXY", false, "FflﬁXY")]
    [InlineData("ﬄﬁXY", true, "Fflﬁxy")]
    [InlineData("ß", false, "Ss")]
    [InlineData("ᾀ", false, "ᾈ")]
    public void TitleCaseChangesTheFirstCharacterAndTheRestOnlyWhenAsked(string text, bool lowerRest, string expected)
    {
        // [glk #encoding_hilo] The first character takes its title-case
        // form, which for the digraphs, the ligatures, and the sharp s
        // is not its upper-case one; the flag says whether the rest is
        // lower-cased or left alone.
        Assert.Equal(Text(expected), GlkUnicode.ToTitleCase(Text(text), lowerRest));
    }

    [Fact]
    public void ValuesThatAreNotUnicodeGoThroughUnchanged()
    {
        uint[] text = [0xD800, 0x110000, 'a'];
        uint[] upper = [0xD800, 0x110000, 'A'];

        // A lone surrogate and a value past the last code point are not
        // characters, so nothing happens to them.
        Assert.Equal(upper, GlkUnicode.ToUpperCase(text));
        Assert.Equal(text, GlkUnicode.ToLowerCase(text));
        Assert.Equal(text, GlkUnicode.Decompose(text));
        Assert.Equal(text, GlkUnicode.Normalize(text));
    }

    [Fact]
    public void EmptyTextStaysEmpty()
    {
        Assert.Empty(GlkUnicode.ToUpperCase([]));
        Assert.Empty(GlkUnicode.ToTitleCase([], true));
        Assert.Empty(GlkUnicode.Decompose([]));
        Assert.Empty(GlkUnicode.Normalize([]));
    }

    [Theory]
    [InlineData("è", "è")]
    [InlineData("ḉ", "ḉ")]
    [InlineData("ᾂ", "ᾂ")]
    [InlineData("̈́", "̈́")]
    [InlineData("Ω", "Ω")]
    [InlineData("plain", "plain")]
    public void DecompositionTakesCharactersApartAllTheWayDown(string text, string expected)
    {
        // [glk #encoding_uninorm] The spec's own example, a character
        // whose decomposition decomposes again, a mark that is two
        // marks, and the ohm sign, which is a singleton.
        Assert.Equal(Text(expected), GlkUnicode.Decompose(Text(text)));
    }

    [Theory]
    [InlineData("á̧", "á̧")]
    [InlineData("á̀", "á̀")]
    [InlineData("á̧ͅb́", "á̧ͅb́")]
    public void DecompositionPutsCombiningMarksInCanonicalOrder(string text, string expected)
    {
        // [glk #encoding_uninorm] Marks sort by combining class, with
        // marks of one class staying in the order they came, and never
        // move past a base character.
        Assert.Equal(Text(expected), GlkUnicode.Decompose(Text(text)));
    }

    [Theory]
    [InlineData("가", "가")]
    [InlineData("각", "각")]
    [InlineData("힣", "힣")]
    public void HangulSyllablesDecomposeIntoTheirJamo(string text, string expected)
    {
        // [glk #encoding_uninorm] The first syllable, the one after it,
        // and the last, taken apart by arithmetic rather than a table.
        Assert.Equal(Text(expected), GlkUnicode.Decompose(Text(text)));
    }

    [Theory]
    [InlineData("è", "è")]
    [InlineData("ḉ", "ḉ")]
    [InlineData("ᾂ", "ᾂ")]
    [InlineData("가", "가")]
    [InlineData("각", "각")]
    [InlineData("è", "è")]
    [InlineData("Ω", "Ω")]
    public void NormalizationPutsCharactersBackTogether(string text, string expected)
    {
        // [glk #encoding_uninorm] The spec's example the other way, a
        // composition in two steps, Hangul, text already composed, and
        // a singleton, which goes to its canonical form and stays.
        Assert.Equal(Text(expected), GlkUnicode.Normalize(Text(text)));
    }

    [Theory]
    [InlineData("á̧", "á̧")]
    [InlineData("e̐́", "e̐́")]
    [InlineData("क़", "क़")]
    [InlineData("̀a", "̀a")]
    public void NormalizationFollowsTheBlockingAndExclusionRules(string text, string expected)
    {
        // [glk #encoding_uninorm] A mark of a lower class between a base
        // and its accent does not block the accent; a mark of the same
        // class does. A composition exclusion decomposes and stays
        // apart, and a mark with no base before it stays as it is.
        Assert.Equal(Text(expected), GlkUnicode.Normalize(Text(text)));
    }

    [Theory]
    [InlineData("ḉᾂ 각 èक़")]
    [InlineData("á̧ͅb́")]
    public void BothNormalizationsAreIdempotent(string text)
    {
        var decomposed = GlkUnicode.Decompose(Text(text));
        var normalized = GlkUnicode.Normalize(Text(text));

        // [glk #encoding_uninorm] Running either again changes nothing,
        // and normalizing decomposed text is the same as normalizing
        // the original.
        Assert.Equal(decomposed, GlkUnicode.Decompose(decomposed));
        Assert.Equal(normalized, GlkUnicode.Normalize(normalized));
        Assert.Equal(normalized, GlkUnicode.Normalize(decomposed));
    }

    [Theory]
    [InlineData(0x61u, 0)]
    [InlineData(0x0300u, 230)]
    [InlineData(0x0327u, 202)]
    [InlineData(0x0345u, 240)]
    [InlineData(0x0F73u, 0)]
    [InlineData(0x110000u, 0)]
    public void CombiningClassesComeFromTheTables(uint character, int expected)
    {
        Assert.Equal(expected, GlkUnicode.CombiningClass(character));
    }

    [Fact]
    public void TheLibraryPromisesTheNormalizationFunctions()
    {
        // [glk #gestalt] gestalt_UnicodeNorm says the two canonical
        // functions are there.
        Assert.Equal(1u, GlkLibrary.Gestalt((uint)GestaltSelector.UnicodeNorm, 0, null));
    }
}

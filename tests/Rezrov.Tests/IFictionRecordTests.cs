using Rezrov.Core.Blorb;

namespace Rezrov.Tests;

/// <summary>
/// [babel #the-ifiction-format] Reading what a story's own iFiction
/// record says about it.
/// </summary>
public class IFictionRecordTests
{
    [Fact]
    public void TheBibliographicFieldsOfTheStoryAreRead()
    {
        var record = IFictionRecord.Read(Record(
            """
            <identification><ifid>ZCODE-1-000000</ifid><format>zcode</format></identification>
            <bibliographic>
                <title>Lakeside Living</title>
                <author>Emily Short</author>
                <language>en-US</language>
                <headline>An Interactive Example</headline>
                <firstpublished>2006</firstpublished>
                <genre>Fiction</genre>
                <description>This is example 194, for what it's worth.</description>
            </bibliographic>
            """))!;

        Assert.Equal("ZCODE-1-000000", record.Ifid);
        Assert.Equal("Lakeside Living", record.Title);
        Assert.Equal("Emily Short", record.Author);
        Assert.Equal("An Interactive Example", record.Headline);
        Assert.Equal("2006", record.FirstPublished);
        Assert.Equal("Fiction", record.Genre);
        Assert.Equal("This is example 194, for what it's worth.", record.Description);
    }

    [Fact]
    public void TheThreeEscapesTheTreatyAllowsAreUndone()
    {
        // [babel #bibliographic] The ampersand and the two angle brackets
        // are the only characters a value has to escape.
        var record = IFictionRecord.Read(Record("<bibliographic><title>Tom &amp; Jerry &lt;3&gt;</title></bibliographic>"))!;

        Assert.Equal("Tom & Jerry <3>", record.Title);
    }

    [Fact]
    public void TheOtherNamedEscapesAndNumberedOnesAreReadToo()
    {
        // Not the treaty's, but a record written by hand may use them,
        // and reading them costs nothing.
        var record = IFictionRecord.Read(Record(
            "<bibliographic><title>&quot;Caf&#233;&quot; &#xC5; &apos;Night&apos;</title></bibliographic>"))!;

        Assert.Equal("\"Café\" Å 'Night'", record.Title);
    }

    [Fact]
    public void AnAmpersandThatIsNoEscapeIsKept()
    {
        var record = IFictionRecord.Read(Record(
            "<bibliographic><title>R&D</title><author>Smith &bogus; Jones</author></bibliographic>"))!;

        Assert.Equal("R&D", record.Title);
        Assert.Equal("Smith &bogus; Jones", record.Author);
    }

    [Fact]
    public void ADescriptionKeepsItsParagraphsAndSinglesItsSpaces()
    {
        // [babel #bibliographic] A paragraph break is a br tag, and any run
        // of white space within a paragraph is one space.
        var record = IFictionRecord.Read(Record(
            "<bibliographic><description>\n\t\tOne\n   two.<br/>\n\tThree\tfour.<br />Five.</description></bibliographic>"))!;

        Assert.Equal("One two.\nThree four.\nFive.", record.Description);
    }

    [Fact]
    public void AFieldThatIsMissingOrEmptyIsNull()
    {
        var record = IFictionRecord.Read(Record("<bibliographic><title>   </title><author>A</author></bibliographic>"))!;

        Assert.Null(record.Title);
        Assert.Equal("A", record.Author);
        Assert.Null(record.Headline);
        Assert.Null(record.Description);
        Assert.Null(record.Ifid);
    }

    [Fact]
    public void ATagWhoseNameOnlyBeginsWithAnotherIsNotIt()
    {
        // The treaty's own series and seriesnumber are the real case of
        // one tag's name beginning another's.
        var record = IFictionRecord.Read(Record(
            "<bibliographic><titles>Not this</titles><title>This</title></bibliographic>"))!;

        Assert.Equal("This", record.Title);
    }

    [Fact]
    public void AnOpeningTagMayCarryAttributes()
    {
        var record = IFictionRecord.Read(Record("<bibliographic><title lang=\"en\">Dressed</title></bibliographic>"))!;

        Assert.Equal("Dressed", record.Title);
    }

    [Fact]
    public void ARecordWithNoStoryIsNoRecord()
    {
        Assert.Null(IFictionRecord.Read("<ifindex version=\"1.0\"></ifindex>"));
    }

    [Fact]
    public void OnlyTheFirstStoryIsRead()
    {
        var record = IFictionRecord.Read(
            "<ifindex><story><bibliographic><title>First</title></bibliographic></story>"
            + "<story><bibliographic><title>Second</title></bibliographic></story></ifindex>")!;

        Assert.Equal("First", record.Title);
    }

    [Theory]
    [InlineData("zork1-r88-s840726.zblorb", "Zork I", "Dave Lebling and Marc Blank", "1980", ">Throw the sack at the troll.")]
    [InlineData("anchorhead-r1-s171017.gblorb", "Anchorhead", "Michael Gentry", "2017", null)]
    [InlineData("pas-de-deux-r2-s191125.zblorb", "Pas De Deux", "Linus Åkesson", null, null)]
    public void AStorysOwnRecordReadsAsItsAuthorWroteIt(
        string name, string title, string author, string? published, string? opening)
    {
        // Zork I's description begins with an escaped prompt, indented
        // with tabs, and broken off into a paragraph of its own, which is
        // every rule for a description at once. Pas De Deux's author has
        // a letter outside ASCII, which the record carries as UTF-8.
        var path = Packages().FirstOrDefault(f => Path.GetFileName(f) == name);
        Assert.SkipUnless(path is not null, "The entharion submodule is not populated.");

        var record = IFictionRecord.Read(BlorbFile.Read(File.ReadAllBytes(path!)).Metadata!)!;

        Assert.Equal(title, record.Title);
        Assert.Equal(author, record.Author);
        Assert.Equal(published, record.FirstPublished);

        if (opening is not null)
        {
            Assert.Equal(opening, record.Description!.Split('\n')[0]);
        }
    }

    [Fact]
    public void EveryRecordInTheCollectionNamesItsTitleAndAuthor()
    {
        // [babel #bibliographic] The two fields every record must have,
        // with every escape undone and no tag left in any value.
        var records = Packages()
            .Select(path => (Name: Path.GetFileName(path), Blorb: BlorbFile.Read(File.ReadAllBytes(path))))
            .Where(package => package.Blorb.Metadata is not null)
            .ToList();

        Assert.SkipUnless(records.Count > 0, "The entharion submodule is not populated.");

        foreach (var (name, blorb) in records)
        {
            var record = IFictionRecord.Read(blorb.Metadata!);

            Assert.True(record is not null, $"{name} has no story in its record.");
            Assert.False(string.IsNullOrEmpty(record!.Title), $"{name} has no title.");
            Assert.False(string.IsNullOrEmpty(record.Author), $"{name} has no author.");

            foreach (var value in new[] { record.Title, record.Author, record.Headline, record.Genre, record.Description })
            {
                Assert.DoesNotContain("&amp;", value ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain("&gt;", value ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain("<br", value ?? string.Empty, StringComparison.Ordinal);
            }
        }
    }

    private static string Record(string story) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
        + "<ifindex version=\"1.0\" xmlns=\"http://babel.ifarchive.org/protocol/iFiction/\">"
        + "<story>" + story + "</story></ifindex>";

    /// <summary>
    /// Every Blorb package in the reference collection, whichever
    /// language's folder it is kept in, or none without the submodule.
    /// </summary>
    private static List<string> Packages()
    {
        var root = Corpus.FindRepositoryRoot();
        var collection = root is null ? null : Path.Combine(root, "entharion");

        if (collection is null || !Directory.Exists(collection))
        {
            return [];
        }

        string[] suffixes = [".zblorb", ".gblorb", ".blorb", ".blb"];
        var vendor = Path.Combine(collection, "vendor") + Path.DirectorySeparatorChar;

        return [.. Directory.EnumerateFiles(collection, "*", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(vendor, StringComparison.Ordinal))
            .Where(path => suffixes.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)];
    }
}

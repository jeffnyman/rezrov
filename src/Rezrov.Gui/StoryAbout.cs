using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Rezrov.AaMachine;
using Rezrov.Core;
using Rezrov.Core.Blorb;
using Rezrov.Glulx;
using Rezrov.ZMachine;
using ZMemory = Rezrov.ZMachine.ZMemory;

namespace Rezrov.Gui;

/// <summary>
/// About This Game: what a story says about itself, gathered from
/// wherever it says it, and shown with its cover.
/// </summary>
/// <remarks>
/// A story can describe itself in several places, and they are asked in
/// order of how much each one knows. An iFiction record in its Blorb file
/// is written for exactly this and comes first. An Å-machine story keeps
/// a title, an author and a blurb of its own. Infocom's games say nothing
/// about themselves at all, so their titles come from the catalog this
/// program keeps of them. And a story that says nothing, and is nobody's
/// in the catalog, is at least called what its file is called.
///
/// Nothing is made up to fill a gap. A field no source has is left out
/// of the window rather than shown empty.
/// </remarks>
internal static class StoryAbout
{
    /// <summary>
    /// [babel #bibliographic] How much of a value is shown, which is where
    /// the treaty suggests a display cut each field, and its description.
    /// </summary>
    private const int Shown = 240;

    private const int DescriptionShown = 2400;

    /// <summary>
    /// Everything the window shows, whichever source gave it.
    /// </summary>
    internal sealed record Facts(
        string Title,
        string? Author,
        string? Headline,
        string? Published,
        string? Genre,
        string? Description,
        string Machine,
        string? Release,
        string? Ifid,
        byte[]? Cover);

    /// <summary>
    /// What is known about the story being played, from all the places a
    /// story says anything.
    /// </summary>
    public static Facts Gather(StoryFormat format, byte[] story, BlorbFile? resources, string path)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(path);

        var record = resources?.Metadata is { } metadata ? IFictionRecord.Read(metadata) : null;
        var (machine, release, ifid, own) = Identity(format, story);

        return new Facts(
            Title: record?.Title ?? own?.Title ?? Catalogued(format, story) ?? Path.GetFileNameWithoutExtension(path),
            Author: record?.Author ?? own?.Author ?? resources?.Author,
            Headline: record?.Headline ?? own?.Noun,
            Published: record?.FirstPublished ?? own?.ReleaseDate,
            Genre: record?.Genre,

            // [aam story] A blurb parts its paragraphs with two line feeds
            // where a record uses one.
            Description: record?.Description ?? own?.Blurb?.Replace("\n\n", "\n", StringComparison.Ordinal),
            Machine: machine,
            Release: release,
            Ifid: record?.Ifid ?? ifid,
            Cover: Cover(resources));
    }

    /// <summary>
    /// The window itself, for whatever shows it to close again.
    /// </summary>
    public static Window Window(Facts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var text = new StackPanel { Spacing = 6, MaxWidth = 460 };

        text.Children.Add(new TextBlock
        {
            Text = Cut(facts.Title, Shown),
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        if (facts.Headline is { } headline)
        {
            text.Children.Add(new TextBlock
            {
                Text = Cut(headline, Shown),
                FontStyle = FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (facts.Author is { } author)
        {
            text.Children.Add(new TextBlock { Text = "by " + Cut(author, Shown), TextWrapping = TextWrapping.Wrap });
        }

        if (string.Join(", ", new[] { facts.Published, facts.Genre }.OfType<string>().Select(v => Cut(v, Shown)))
            is { Length: > 0 } dated)
        {
            text.Children.Add(new TextBlock { Text = dated, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
        }

        if (facts.Description is { } description)
        {
            var paragraphs = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };

            foreach (var paragraph in Cut(description, DescriptionShown).Split('\n'))
            {
                paragraphs.Children.Add(new TextBlock { Text = paragraph, TextWrapping = TextWrapping.Wrap });
            }

            text.Children.Add(paragraphs);
        }

        // How the story is identified, quieter than what it says about
        // itself, since it is there to be looked up rather than read.
        var technical = new StackPanel { Spacing = 2, Margin = new Thickness(0, 14, 0, 0), Opacity = 0.65 };
        technical.Children.Add(new TextBlock
        {
            Text = facts.Release is { } release ? $"{facts.Machine}, {release}" : facts.Machine,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });

        if (facts.Ifid is { } ifid)
        {
            technical.Children.Add(new TextBlock { Text = "IFID " + ifid, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        }

        text.Children.Add(technical);

        var close = new Button
        {
            Content = "Close",
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        text.Children.Add(close);

        var body = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Margin = new Thickness(24) };

        if (Picture(facts.Cover) is { } cover)
        {
            body.Children.Add(new Image
            {
                Source = cover,
                Width = 220,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Top,
            });
        }

        body.Children.Add(text);

        var window = new Window
        {
            Title = "About This Game",
            Content = new ScrollViewer { Content = body },
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxHeight = 720,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        close.Click += (_, _) => window.Close();
        return window;
    }

    /// <summary>
    /// Which machine the story runs on, its release where it says, its
    /// identifier where one can be worked out, and what an Å-machine story
    /// says of itself.
    /// </summary>
    private static (string Machine, string? Release, string? Ifid, AaMetadata? Own) Identity(StoryFormat format, byte[] story)
    {
        try
        {
            switch (format)
            {
                case StoryFormat.ZMachine:
                    var header = new StoryHeader(new ZMemory(story));
                    return (
                        $"Z-machine, Version {(int)header.Version}",
                        $"release {header.Release}, serial {header.SerialCode}",
                        Ifid.Of(story),
                        null);

                case StoryFormat.Glulx:
                    var glulx = new GlulxMemory(story).Header;
                    return (
                        glulx.InformVersion is { } inform ? $"Glulx {glulx.VersionText}, Inform {inform}" : $"Glulx {glulx.VersionText}",
                        glulx.InformRelease is { } number ? $"release {number}, serial {glulx.InformSerial}" : null,
                        null,
                        null);

                case StoryFormat.AaMachine:
                    var aa = AaStory.Read(story);
                    return ("Å-machine", $"release {aa.Release}, serial {aa.Serial}", null, aa.Metadata);
            }
        }
        catch (InvalidDataException)
        {
            // A story that played but will not describe itself still has
            // a machine to name.
        }

        return (format.ToString(), null, null, null);
    }

    /// <summary>
    /// [babel legacy Z-code IFID] The title of one of Infocom's games, from
    /// the catalog, since their files never say.
    /// </summary>
    private static string? Catalogued(StoryFormat format, byte[] story) =>
        format == StoryFormat.ZMachine ? InfocomCatalog.TitleOf(story) : null;

    /// <summary>
    /// [blorb 8] The cover, which the Fspc chunk names among the pictures,
    /// as long as it is one of the two kinds a picture may be.
    /// </summary>
    private static byte[]? Cover(BlorbFile? resources) =>
        resources?.Frontispiece is { } number
            && resources.Find(ResourceUsage.Picture, number) is { ChunkType: "PNG " or "JPEG" } picture
            ? picture.Data.ToArray()
            : null;

    private static Bitmap? Picture(byte[]? bytes)
    {
        if (bytes is null)
        {
            return null;
        }

        try
        {
            return new Bitmap(new MemoryStream(bytes));
        }
        catch (Exception e) when (e is ArgumentException or InvalidDataException or NotSupportedException)
        {
            // A cover that will not decode is left out, as a missing
            // one is.
            return null;
        }
    }

    /// <summary>
    /// A value cut where the treaty suggests, marked as cut.
    /// </summary>
    private static string Cut(string value, int most) =>
        value.Length <= most ? value : value[..(most - 1)].TrimEnd() + "…";
}

using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Rezrov.Gui;

/// <summary>
/// Opening another story: asking the player which, and starting a window
/// of its own to play it in.
/// </summary>
/// <remarks>
/// A window here plays one story from start to end, so another story is
/// another window, and whatever was being played goes on untouched
/// beside it. Nothing the player had not saved can be lost that way, and
/// there is nothing to ask them first.
///
/// The new window is this same program started again, at the path it
/// was started from. On a Mac that path is the executable inside
/// rezrov.app, so the new window is the bundled program too, under its
/// own name and icon.
/// </remarks>
internal static class StoryOpener
{
    // How the program looks and which machine it says it is: the player's
    // choices, which the new window keeps. Each is given with a value.
    private static readonly HashSet<string> CarriedWithValue =
        ["--font", "--sans", "--fixed", "--size", "--smoothing", "--padding", "--interpreter"];

    // The same, given on their own.
    private static readonly HashSet<string> CarriedAlone = ["--map", "--tandy"];

    // Choices about the story that was being played rather than about the
    // player, which mean nothing to another one. Each has a value, which
    // goes with it.
    private static readonly HashSet<string> LeftWithValue = ["--blorb", "--commands", "--pictures", "--seed"];

    /// <summary>
    /// What the stories this program plays are called: the Z-machine's
    /// versions, Glulx, the Å-machine, and Blorb packages. Anything called
    /// something else is still a choice away, under all files.
    /// </summary>
    /// <remarks>
    /// [blorb #file-suffixes] A Blorb file may always end in .blorb, should
    /// end in .zblorb or .gblorb by the machine its game is for, and may
    /// end in .blb, .zlb or .glb where a system allows only three letters.
    /// </remarks>
    private static readonly FilePickerFileType Stories = new("Stories")
    {
        Patterns =
        [
            "*.z1", "*.z2", "*.z3", "*.z4", "*.z5", "*.z6", "*.z7", "*.z8",
            "*.ulx", "*.aastory",
            "*.zblorb", "*.gblorb", "*.blorb", "*.blb", "*.zlb", "*.glb",
        ],
    };

    /// <summary>
    /// The options from a command line that a window opened from this one
    /// should be given too, in the words they were given in.
    /// </summary>
    /// <remarks>
    /// Copied rather than written out again from what they came to, so a
    /// font list or a size reaches the new window exactly as the player
    /// typed it. The first argument is the story, which is not carried.
    /// </remarks>
    public static IReadOnlyList<string> Carried(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var carried = new List<string>();

        for (var i = 1; i < args.Count; i++)
        {
            var option = args[i];

            if (CarriedWithValue.Contains(option) && i + 1 < args.Count)
            {
                carried.Add(option);
                carried.Add(args[++i]);
            }
            else if (CarriedAlone.Contains(option))
            {
                carried.Add(option);
            }
            else if (LeftWithValue.Contains(option))
            {
                i++;
            }
        }

        return carried;
    }

    /// <summary>
    /// Asks which story to play, and opens a window playing it.
    /// </summary>
    /// <param name="owner">The window asking.</param>
    /// <param name="near">
    /// Where to start looking, which is where the story being played
    /// came from, or null to let the system choose.
    /// </param>
    /// <param name="carried">The options the new window is given.</param>
    /// <returns>Whether a window was opened.</returns>
    public static async Task<bool> Ask(Window owner, string? near, IReadOnlyList<string> carried)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var start = near is null ? null : await owner.StorageProvider.TryGetFolderFromPathAsync(near);
        var chosen = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a story",
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = [Stories, FilePickerFileTypes.All],
        });

        if (chosen.Count == 0 || chosen[0].TryGetLocalPath() is not { } story)
        {
            return false;
        }

        return Open(owner, story, carried);
    }

    private static bool Open(Window owner, string story, IReadOnlyList<string> carried)
    {
        if (Environment.ProcessPath is not { } program)
        {
            Tell(owner, "This program cannot tell where it was started from, so it has no way to start again.");
            return false;
        }

        var start = new ProcessStartInfo(program) { UseShellExecute = false };
        start.ArgumentList.Add(story);

        foreach (var option in carried)
        {
            start.ArgumentList.Add(option);
        }

        try
        {
            using var started = Process.Start(start);
            return started is not null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            Tell(owner, $"The story could not be opened: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Says what went wrong in a small window of its own, since a window
    /// program has no console to say it on.
    /// </summary>
    private static void Tell(Window owner, string what)
    {
        var close = new Button
        {
            Content = "Close",
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = what, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
        panel.Children.Add(close);

        var told = new Window
        {
            Title = "rezrov",
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        close.Click += (_, _) => told.Close();
        _ = told.ShowDialog(owner);
    }
}

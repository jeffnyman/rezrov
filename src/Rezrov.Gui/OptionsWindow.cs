using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Rezrov.ZMachine;

namespace Rezrov.Gui;

/// <summary>
/// The Options window: the choices a player keeps between sessions, set
/// by hand instead of typed at a command line.
/// </summary>
/// <remarks>
/// What it shows is what is kept, not what this window happens to be
/// using, since a window started with options of its own at a command
/// line is not what the next one will be given. The type, its size, the
/// smoothing, the margin and the colors change in this window as soon
/// as they are kept, replacing any it was started with. The machine a
/// game is told it runs on, the Tandy bit, and the map at the start are
/// read as a game starts, and so wait for the next one.
///
/// Only a choice that differs from the program's own is written down,
/// so the file says what the player changed, and putting everything back
/// leaves it saying nothing.
/// </remarks>
internal static class OptionsWindow
{
    private const string OwnMachine = "Whichever suits the game";

    /// <param name="kept">What happens once the options are kept.</param>
    public static Window Make(Action kept)
    {
        ArgumentNullException.ThrowIfNull(kept);

        var now = KeptOptions.Load().ToDictionary(o => o[0], o => o.Length > 1 ? o[1] : string.Empty);

        var prose = Text(Value(now, "--font", Glyphs.ProseFamily));
        var sans = Text(Value(now, "--sans", GuiAaGlyphs.SansFamily));
        var fixedFace = Text(Value(now, "--fixed", Glyphs.FixedFamily));
        var size = Number(Value(now, "--size", Glyphs.OrdinarySize), 6, 72);
        var padding = Number(Value(now, "--padding", Board.OrdinaryPadding), 0, 64);
        var smoothing = Choice(["subpixel", "grayscale", "none"], Value(now, "--smoothing", "subpixel"));
        var machine = Choice([OwnMachine, .. InterpreterNumbers.AllNames], Value(now, "--interpreter", OwnMachine));
        var map = new CheckBox { Content = "Open the map beside the game at the start", IsChecked = now.ContainsKey("--map") };
        var tandy = new CheckBox { Content = "Set the Tandy bit for a Version 1 to 3 game", IsChecked = now.ContainsKey("--tandy") };

        var keptInk = ColorSchemes.Parse(now.GetValueOrDefault("--foreground"));
        var keptPaper = ColorSchemes.Parse(now.GetValueOrDefault("--background"));
        var scheme = Choice(
            [ColorSchemes.GameOwn, .. ColorSchemes.Named.Select(s => s.Name), ColorSchemes.Custom],
            ColorSchemes.NameOf(keptInk, keptPaper));
        var (ink, inkSwatch) = Colored(keptInk);
        var (paper, paperSwatch) = Colored(keptPaper);

        // A named scheme fills the two colors in and holds them, so what
        // is shown is what will be kept; only Custom lets them be typed.
        void Schemed()
        {
            var chosen = scheme.SelectedItem as string;

            if (chosen == ColorSchemes.GameOwn)
            {
                ink.Text = string.Empty;
                paper.Text = string.Empty;
            }
            else if (ColorSchemes.Named.FirstOrDefault(s => s.Name == chosen) is { } named)
            {
                ink.Text = ColorSchemes.Written(named.Ink);
                paper.Text = ColorSchemes.Written(named.Paper);
            }

            ink.IsEnabled = chosen == ColorSchemes.Custom;
            paper.IsEnabled = chosen == ColorSchemes.Custom;
        }

        scheme.SelectionChanged += (_, _) => Schemed();
        Schemed();

        var rows = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,16,*"),
            RowSpacing = 10,
        };

        var row = 0;
        void Add(string label, Control control)
        {
            rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(name, row);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 2);
            rows.Children.Add(name);
            rows.Children.Add(control);
            row++;
        }

        void Heading(string text)
        {
            rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var heading = new TextBlock
            {
                Text = text,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, row == 0 ? 0 : 10, 0, 0),
            };
            Grid.SetRow(heading, row);
            Grid.SetColumnSpan(heading, 3);
            rows.Children.Add(heading);
            row++;
        }

        void Across(Control control)
        {
            rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(control, row);
            Grid.SetColumnSpan(control, 3);
            rows.Children.Add(control);
            row++;
        }

        Heading("Text");
        Add("Prose", prose);
        Add("Sans-serif", sans);
        Add("Fixed width", fixedFace);
        Add("Size in pixels", size);
        Add("Smoothing", smoothing);
        Add("Margin in pixels", padding);
        Heading("Colors");
        Add("Scheme", scheme);
        Add("Text", Paired(ink, inkSwatch));
        Add("Page", Paired(paper, paperSwatch));
        Heading("Window");
        Across(map);
        Heading("Machine");
        Add("Running on", machine);
        Across(tandy);

        var trouble = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.OrangeRed, IsVisible = false };

        var defaults = new Button { Content = "Defaults" };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var save = new Button { Content = "Keep", IsDefault = true };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        buttons.Children.Add(defaults);
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        var body = new StackPanel { Margin = new Thickness(24), Width = 520 };
        body.Children.Add(rows);
        body.Children.Add(new TextBlock
        {
            Text = "The type, size, smoothing, margin, and colors change here as soon as they are kept, taking the place of any typed at a command line. The rest apply to games started from now on. A game that chooses its own colors keeps them.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            Margin = new Thickness(0, 16, 0, 0),
        });
        body.Children.Add(trouble);
        body.Children.Add(buttons);

        var window = new Window
        {
            Title = "Options",
            Content = body,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        defaults.Click += (_, _) =>
        {
            prose.Text = Glyphs.ProseFamily;
            sans.Text = GuiAaGlyphs.SansFamily;
            fixedFace.Text = Glyphs.FixedFamily;
            size.Value = (decimal)Glyphs.OrdinarySize;
            padding.Value = (decimal)Board.OrdinaryPadding;
            smoothing.SelectedItem = "subpixel";
            machine.SelectedItem = OwnMachine;
            scheme.SelectedItem = ColorSchemes.GameOwn;
            map.IsChecked = false;
            tandy.IsChecked = false;
        };

        cancel.Click += (_, _) => window.Close();

        save.Click += (_, _) =>
        {
            var chosen = new List<string[]>();

            void Differs(string option, string? value, string standard)
            {
                if (value is { Length: > 0 } given && given.Trim() != standard)
                {
                    chosen.Add([option, given.Trim()]);
                }
            }

            Differs("--font", prose.Text, Glyphs.ProseFamily);
            Differs("--sans", sans.Text, GuiAaGlyphs.SansFamily);
            Differs("--fixed", fixedFace.Text, Glyphs.FixedFamily);
            Differs("--size", Whole(size.Value), Whole(Glyphs.OrdinarySize));
            Differs("--smoothing", smoothing.SelectedItem as string, "subpixel");
            Differs("--padding", Whole(padding.Value), Whole(Board.OrdinaryPadding));
            Differs("--interpreter", machine.SelectedItem as string, OwnMachine);

            // A color left empty is left to the game, and one that cannot
            // be read is said rather than quietly dropped.
            foreach (var (option, field, name) in new[] { ("--foreground", ink, "text"), ("--background", paper, "page") })
            {
                if (string.IsNullOrWhiteSpace(field.Text))
                {
                    continue;
                }

                if (ColorSchemes.Parse(field.Text) is not { } color)
                {
                    trouble.Text = $"The {name} color should be a # and six hex digits, like #1A1A1A.";
                    trouble.IsVisible = true;
                    return;
                }

                chosen.Add([option, ColorSchemes.Written(color)]);
            }

            if (map.IsChecked == true)
            {
                chosen.Add(["--map"]);
            }

            if (tandy.IsChecked == true)
            {
                chosen.Add(["--tandy"]);
            }

            if (KeptOptions.Save(chosen) is { } failed)
            {
                trouble.Text = $"The options could not be kept: {failed}";
                trouble.IsVisible = true;
                return;
            }

            kept();
            window.Close();
        };

        return window;
    }

    private static string Value(Dictionary<string, string> kept, string option, string standard) =>
        kept.TryGetValue(option, out var value) && value.Length > 0 ? value : standard;

    private static string Value(Dictionary<string, string> kept, string option, double standard) =>
        Value(kept, option, standard.ToString(CultureInfo.InvariantCulture));

    private static TextBox Text(string value) => new() { Text = value };

    /// <summary>
    /// A field for a color, and a patch beside it showing the color typed,
    /// or nothing while what is typed is not yet a color.
    /// </summary>
    private static (TextBox Field, Border Swatch) Colored(uint? color)
    {
        var field = new TextBox
        {
            Text = color is { } given ? ColorSchemes.Written(given) : string.Empty,
            Width = 140,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var swatch = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
        };

        void Shown() => swatch.Background = ColorSchemes.Parse(field.Text) is { } typed
            ? new SolidColorBrush(Color.FromRgb((byte)(typed >> 16), (byte)(typed >> 8), (byte)typed))
            : null;

        field.TextChanged += (_, _) => Shown();
        Shown();

        return (field, swatch);
    }

    private static StackPanel Paired(Control field, Control swatch)
    {
        var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        pair.Children.Add(field);
        pair.Children.Add(swatch);
        return pair;
    }

    private static NumericUpDown Number(string value, int least, int most) => new()
    {
        Minimum = least,
        Maximum = most,
        Increment = 1,
        FormatString = "0",
        Value = decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? Math.Clamp(number, least, most)
            : least,
        HorizontalAlignment = HorizontalAlignment.Left,
        Width = 140,
    };

    private static ComboBox Choice(string[] items, string chosen) => new()
    {
        ItemsSource = items,
        SelectedItem = items.Contains(chosen) ? chosen : items[0],
        HorizontalAlignment = HorizontalAlignment.Left,
        MinWidth = 220,
    };

    private static string Whole(decimal? value) =>
        ((int)Math.Round(value ?? 0)).ToString(CultureInfo.InvariantCulture);

    private static string Whole(double value) =>
        ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
}

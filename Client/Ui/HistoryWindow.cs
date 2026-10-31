using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>
/// The history: every dungeon run, boss fight, training dummy parse and arena stay saved on this PC as it ended (see <see cref="FightLog"/>), one entry
/// each, on tabs and by day, with the time, killed or not, and your DPS; a click opens the fight report. Personal bests: your best DPS per boss and per
/// dummy parse length.
/// </summary>
public sealed class HistoryWindow : Window
{
    private static readonly string[] Tabs = { "Dungeon runs", "Boss fights", "Training dummy", "PvP", "Personal bests" };
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly Brush GoodBack = Frozen(Color.FromArgb(0x2E, 0x5C, 0xC2, 0x7E)), GoodText = Frozen(Color.FromRgb(0x7F, 0xD8, 0x9B));
    private static readonly Brush BadBack = Frozen(Color.FromArgb(0x2E, 0xE0, 0x6A, 0x5E)), BadText = Frozen(Color.FromRgb(0xF0, 0x8E, 0x84));
    private static readonly Brush HoverBack = Frozen(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF));

    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _list = new();
    private readonly StackPanel _page = new() { Margin = new Thickness(30, 22, 30, 30), MaxWidth = 1100 };
    private string _tab = Tabs[0];

    public HistoryWindow()
    {
        Title = "History · " + Brand.AppName;
        Width = 1040;
        Height = 820;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");

        StackPanel page = _page;
        var title = new TextBlock { Text = "History", FontSize = 26, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Title");
        page.Children.Add(title);
        page.Children.Add(Muted("Dungeon runs, boss fights, training dummy parses and PvP are saved on this PC as they end, each on its own, with every number the meter showed. Click one for its report.", 13.5, new Thickness(0, 4, 0, 0)));
        var tabLine = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 18, 0, 0), Child = _tabs };
        tabLine.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        page.Children.Add(tabLine);
        _list.Margin = new Thickness(0, 10, 0, 0);
        page.Children.Add(_list);
        var folder = new TextBlock { Text = "Open the history folder", FontSize = 12.5, Margin = new Thickness(0, 14, 0, 0), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
        folder.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        folder.MouseLeftButtonUp += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(FightLog.Folder);
                Process.Start(new ProcessStartInfo(FightLog.Folder) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                // nothing to open
            }
        };
        page.Children.Add(folder);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
        ThemedChrome.Apply(this);

        Action onChanged = () => Dispatcher.BeginInvoke(new Action(Draw));
        FightLog.Changed += onChanged;
        Closed += (_, _) => FightLog.Changed -= onChanged;
        Draw();
    }

    /// <summary>The page itself, inside the window's scroll viewer and chrome (the picture mode takes it out and draws it).</summary>
    internal StackPanel PageContent => _page;

    public void ShowTab(string tab)
    {
        _tab = Tabs.Contains(tab) ? tab : Tabs[0];
        Draw();
    }

    private void Draw()
    {
        _tabs.Children.Clear();
        foreach (string name in Tabs)
        {
            bool on = name == _tab;
            var label = new TextBlock { Text = name, FontSize = 14, Margin = new Thickness(0, 0, 0, 9), FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal };
            label.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.Text" : "Brush.TextMuted");
            var stack = new StackPanel { Margin = new Thickness(0, 0, 26, -1), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            stack.Children.Add(label);
            stack.Children.Add(new Border { Height = 2, Background = on ? Frozen(Color.FromRgb(0xDC, 0xB8, 0x77)) : Brushes.Transparent });
            string target = name;
            stack.MouseLeftButtonUp += (_, _) => ShowTab(target);
            _tabs.Children.Add(stack);
        }

        _list.Children.Clear();
        IReadOnlyList<FightLogEntry> all = FightLog.Entries();
        if (_tab == "Personal bests")
        {
            DrawBests(all);
            return;
        }

        FightLogKind kind = _tab switch { "Boss fights" => FightLogKind.Boss, "Training dummy" => FightLogKind.Dummy, "PvP" => FightLogKind.Pvp, _ => FightLogKind.Run };
        var entries = all.Where(e => e.Kind == kind).ToList();
        if (entries.Count == 0)
        {
            _list.Children.Add(Card(Muted(kind switch
            {
                FightLogKind.Boss => "No boss fights yet. Every boss you fight, in a dungeon or in the open world, is saved here when the fight ends.",
                FightLogKind.Dummy => "No dummy parses yet. Hit a Training Scarecrow or a Punching Bag: the parse starts by itself and is saved here when its time is up.",
                FightLogKind.Pvp => "No arena fights yet.",
                _ => "No dungeon runs yet. Every run is saved here when you leave the dungeon.",
            }, 13.5, new Thickness(18, 16, 18, 16))));
            return;
        }

        foreach (var day in entries.GroupBy(e => e.Start.Date))
        {
            _list.Children.Add(Muted(day.Key.ToString("MMM d, yyyy", En).ToUpperInvariant(), 11.5, new Thickness(2, 16, 0, 8), semibold: true));
            var card = new StackPanel();
            bool first = true;
            foreach (FightLogEntry entry in day)
            {
                card.Children.Add(Row(entry, first, best: false));
                first = false;
            }

            _list.Children.Add(Card(card));
        }
    }

    /// <summary>Your best DPS per boss (killed fights) and per dummy parse length.</summary>
    private void DrawBests(IReadOnlyList<FightLogEntry> all)
    {
        var bosses = all.Where(e => e.Kind == FightLogKind.Boss && e.Killed == true && e.YourDps > 0).GroupBy(e => e.Title).Select(g => g.MaxBy(e => e.YourDps)!).OrderBy(e => e.Title).ToList();
        var dummies = all.Where(e => e.Kind == FightLogKind.Dummy && e.YourDps > 0).GroupBy(e => e.Detail ?? "").Select(g => g.MaxBy(e => e.YourDps)!).OrderBy(e => e.Seconds).ToList();
        if (bosses.Count == 0 && dummies.Count == 0)
        {
            _list.Children.Add(Card(Muted("No bests yet: kill a boss or finish a dummy parse.", 13.5, new Thickness(18, 16, 18, 16))));
            return;
        }

        foreach ((string heading, List<FightLogEntry> list) in new[] { ("BOSSES", bosses), ("TRAINING DUMMY", dummies) })
        {
            if (list.Count == 0)
            {
                continue;
            }

            _list.Children.Add(Muted(heading, 11.5, new Thickness(2, 16, 0, 8), semibold: true));
            var card = new StackPanel();
            bool first = true;
            foreach (FightLogEntry entry in list)
            {
                card.Children.Add(Row(entry, first, best: true));
                first = false;
            }

            _list.Children.Add(Card(card));
        }
    }

    private FrameworkElement Row(FightLogEntry entry, bool first, bool best)
    {
        var grid = new Grid { Margin = new Thickness(14, 9, 18, 9) };
        foreach (GridLength w in new[] { new GridLength(46), new GridLength(1, GridUnitType.Star), new GridLength(90), new GridLength(110) })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        }

        ImageSource? icon = entry.BossNpcId is int npc ? Picture(Aion2Artwork.BossThumbPath(npc)) : null;
        icon ??= entry.YourClass is { Length: > 0 } cls ? Picture(Path.Combine(AppContext.BaseDirectory, "assets", "classes", "icons", cls + ".png")) : null;
        var box = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(5), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        box.Background = icon is not null ? new ImageBrush(icon) { Stretch = Stretch.UniformToFill } : Frozen(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
        Grid.SetColumn(box, 0);
        grid.Children.Add(box);

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
        title.Inlines.Add(new System.Windows.Documents.Run(entry.Title) { FontWeight = FontWeights.SemiBold });
        if (entry.Detail is { Length: > 0 } detail)
        {
            var d = new System.Windows.Documents.Run(" · " + detail);
            d.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.TextMuted");
            title.Inlines.Add(d);
        }

        names.Children.Add(title);
        string when = best ? entry.Start.ToString("MMM d, h:mm tt", En) : entry.Start.ToString("h:mm tt", En);
        string what = entry.Kind switch
        {
            FightLogKind.Run => entry.BossFights switch { 0 => "no boss fights", 1 => "1 boss fight", int n => $"{n} boss fights" },
            FightLogKind.Boss => entry.Killed == true ? "Killed" : "Wipe",
            _ => entry.You is { } you ? you : "",
        };
        names.Children.Add(Muted(what.Length > 0 ? $"{when} · {what}" : when, 12, new Thickness(0, 2, 0, 0)));
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);

        bool bad = entry.Kind == FightLogKind.Boss && entry.Killed != true;
        var chip = new Border
        {
            CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Background = bad ? BadBack : GoodBack,
            Child = new TextBlock { Text = ReportPalette.Clock(entry.Seconds), FontSize = 12.5, Foreground = bad ? BadText : GoodText },
        };
        Grid.SetColumn(chip, 2);
        grid.Children.Add(chip);

        var dps = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        dps.Children.Add(new TextBlock { Text = Short(entry.YourDps > 0 ? entry.YourDps : entry.PartyDps), FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });
        if (entry.YourDps > 0 && entry.Kind != FightLogKind.Dummy)
        {
            dps.Children.Add(Muted("party " + Short(entry.PartyDps), 11, new Thickness(0, 1, 0, 0), right: true));
        }

        Grid.SetColumn(dps, 3);
        grid.Children.Add(dps);

        var row = new Border { Child = grid, Background = Brushes.Transparent, Cursor = Cursors.Hand, BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0) };
        row.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        row.MouseEnter += (_, _) => row.Background = HoverBack;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) => _ = OpenAsync(entry);
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open the report" };
        open.Click += (_, _) => _ = OpenAsync(entry);
        var delete = new MenuItem { Header = "Delete" };
        delete.Click += (_, _) => FightLog.Delete(entry);
        menu.Items.Add(open);
        menu.Items.Add(delete);
        row.ContextMenu = menu;
        return row;
    }

    private async Task OpenAsync(FightLogEntry entry)
    {
        Cursor = Cursors.Wait;
        try
        {
            FightReport? report = await Task.Run(() => FightLog.Open(entry) is { } input ? FightReport.Build(input) : null);
            if (report is null)
            {
                ThemedMessageBox.Show(this, "This fight's file is gone or could not be read.", "History", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            new FightReportWindow(report) { Owner = this }.Show();
        }
        finally
        {
            Cursor = null;
        }
    }

    private static Border Card(UIElement child)
    {
        var card = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = child, ClipToBounds = true };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    private static TextBlock Muted(string text, double size, Thickness margin, bool semibold = false, bool right = false)
    {
        var t = new TextBlock
        {
            Text = text, FontSize = size, Margin = margin, TextWrapping = TextWrapping.Wrap, FontWeight = semibold ? FontWeights.SemiBold : FontWeights.Normal,
            HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        return t;
    }

    private static string Short(double v) => v >= 1_000_000 ? (v / 1_000_000).ToString("0.0#", En) + "M" : v >= 1000 ? (v / 1000).ToString("0.0", En) + "K" : v.ToString("0", En);

    private static ImageSource? Picture(string? path) => path is not null && File.Exists(path) ? OverlayPictures.Load(path) : null;

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

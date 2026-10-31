using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// The page of the loot tool: on the left the sessions (a session ends after half an hour without a pickup, or when the meter starts anew), on the right what was picked up in the chosen
/// one, map by map, the items in the colour of their grade. A button deletes one session or the whole log. The log lives only on this computer.
/// What it is worth: the market prices of the own server (<see cref="MarketPrices"/>, the site's price service), in all and per hour of the session.
/// </summary>
public sealed class LootPage : Grid
{
    private readonly StackPanel _sessions = new();
    private readonly StackPanel _items = new();
    private readonly TextBlock _summary = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) };
    private readonly TextBlock _worth = new() { Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
    private readonly Button _deleteOne = new() { Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 8, 0) };
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private List<LootSession> _list = new();
    private DateTime? _selectedStart;
    private string _shown = "";
    private bool _consent;
    private long _consentAt = long.MinValue;

    /// <summary>The privacy policy accepted (read from the settings at most every ten seconds: the page refreshes several times a second).</summary>
    private bool Consent()
    {
        long tick = Environment.TickCount64;
        if (tick - _consentAt > 10_000)
        {
            _consentAt = tick;
            _consent = PrivacyConsent.CanUpload;
        }

        return _consent;
    }

    public LootPage()
    {
        var loc = LocalizationManager.Instance;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        MinHeight = 380;

        var left = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        var leftHead = new TextBlock { Text = loc["Loot.Sessions"], FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        leftHead.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        DockPanel.SetDock(leftHead, Dock.Top);
        left.Children.Add(leftHead);
        var sessionScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _sessions };
        left.Children.Add(sessionScroll);
        SetColumn(left, 0);
        Children.Add(left);

        var right = new DockPanel();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(bar, Dock.Bottom);
        _deleteOne.Content = loc["Loot.DeleteSession"];
        _deleteOne.Click += (_, _) =>
        {
            if (Selected() is { } session && Ask(loc["Loot.DeleteSession.Confirm"]))
            {
                Aion2Loot.Clear(session);
            }
        };
        var deleteAll = new Button { Content = loc["Loot.DeleteAll"], Padding = new Thickness(10, 2, 10, 2) };
        deleteAll.Click += (_, _) =>
        {
            if (Ask(loc["Loot.DeleteAll.Confirm"]))
            {
                Aion2Loot.Clear();
            }
        };
        bar.Children.Add(_deleteOne);
        bar.Children.Add(deleteAll);
        right.Children.Add(bar);
        _summary.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        DockPanel.SetDock(_summary, Dock.Top);
        right.Children.Add(_summary);
        _worth.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        DockPanel.SetDock(_worth, Dock.Top);
        right.Children.Add(_worth);
        right.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _items });
        SetColumn(right, 1);
        Children.Add(right);

        _refresh.Tick += (_, _) => Refresh();
        Loaded += (_, _) => _refresh.Start();
        Unloaded += (_, _) => _refresh.Stop();
        Refresh();
    }

    public void Stop() => _refresh.Stop();

    private bool Ask(string text) =>
        ThemedMessageBox.Show(Window.GetWindow(this), text, LocalizationManager.Instance["Main.MenuTools.Loot"], MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private LootSession? Selected() => _list.FirstOrDefault(s => s.Start == _selectedStart);

    private void Refresh()
    {
        var loc = LocalizationManager.Instance;
        _list = Aion2Loot.Sessions.ToList();
        int server = OwnServer();
        bool ask = Consent() && MarketPrices.SignedIn;
        MarketBook? book = MarketPrices.For(server, ask);
        string key = string.Join("|", _list.Select(s => $"{s.Start.Ticks}:{s.End.Ticks}:{s.Maps.Sum(m => m.Items.Sum(i => i.Picks))}")) + "/" + _selectedStart?.Ticks + "/" + loc.Language
            + "/" + server + ":" + MarketPrices.Version + ":" + ask;
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        if (_selectedStart is null || _list.All(s => s.Start != _selectedStart))
        {
            _selectedStart = _list.FirstOrDefault()?.Start;
        }

        _sessions.Children.Clear();
        foreach (LootSession s in _list)
        {
            long picked = s.Maps.Sum(m => m.Items.Sum(it => it.Count));
            var line = new StackPanel();
            var title = new TextBlock { Text = s.Start.ToString("g"), FontWeight = FontWeights.SemiBold };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            line.Children.Add(title);
            var detail = new TextBlock { Text = string.Format(loc["Loot.SessionLine"], picked, (s.End - s.Start).TotalMinutes < 1 ? "<1" : ((int)(s.End - s.Start).TotalMinutes).ToString()), FontSize = 11.5 };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            line.Children.Add(detail);
            var entry = new Border { Child = line, Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 4, 3), Cursor = System.Windows.Input.Cursors.Hand };
            bool selected = s.Start == _selectedStart;
            entry.Background = selected ? (Brush)new BrushConverter().ConvertFromString("#26405a")! : Brushes.Transparent;
            DateTime start = s.Start;
            entry.MouseEnter += (_, _) => { if (start != _selectedStart) { entry.Background = (Brush)new BrushConverter().ConvertFromString("#1a2b38")!; } };
            entry.MouseLeave += (_, _) => { if (start != _selectedStart) { entry.Background = Brushes.Transparent; } };
            entry.MouseLeftButtonUp += (_, _) =>
            {
                _selectedStart = start;
                _shown = "";
                Refresh();
            };
            _sessions.Children.Add(entry);
        }

        _items.Children.Clear();
        _deleteOne.IsEnabled = Selected() is not null;
        if (Selected() is not { } session)
        {
            _summary.Text = "";
            _worth.Text = "";
            var empty = new TextBlock { Text = loc["Loot.Empty"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            _items.Children.Add(empty);
            return;
        }

        long total = session.Maps.Sum(m => m.Items.Sum(i => i.Count));
        _summary.Text = string.Format(loc["Loot.Summary"], total, LootText.Merge(session.Maps).Count);
        ShowWorth(session, book, server, ask);
        foreach (LootMap map in session.Maps.OrderByDescending(m => m.Items.Max(i => i.Last)))
        {
            var head = new TextBlock { Text = LootText.MapName(map.Zone), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 3) };
            head.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
            _items.Children.Add(head);
            foreach (LootItem item in map.Items.OrderByDescending(i => i.Last))
            {
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var count = new TextBlock { Text = "×" + item.Count.ToString("N0"), Margin = new Thickness(12, 0, 0, 0), MinWidth = 44, TextAlignment = TextAlignment.Right };
                count.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                DockPanel.SetDock(count, Dock.Right);
                row.Children.Add(count);
                if (book is not null && book.Items.TryGetValue(item.ItemId, out MarketPrice? price) && price.Value is long each)
                {
                    var value = new TextBlock { Text = Short(each * item.Count), Margin = new Thickness(12, 0, 0, 0), MinWidth = 56, TextAlignment = TextAlignment.Right,
                        ToolTip = string.Format(loc["Loot.ItemWorth"], each.ToString("N0"), (each * item.Count).ToString("N0")) };
                    value.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
                    DockPanel.SetDock(value, Dock.Right);
                    row.Children.Add(value);
                }

                row.Children.Add(new TextBlock
                {
                    Text = LootText.ItemName(item.ItemId), Foreground = new SolidColorBrush(LootText.GradeColor(item.ItemId)), TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = string.Format(loc["Loot.ItemTip"], item.Picks, item.First.ToString("t"), item.Last.ToString("t")),
                });
                _items.Children.Add(row);
            }
        }
    }

    /// <summary>The server whose prices count: the character logged in now, else the one played last.</summary>
    private static int OwnServer()
    {
        var characters = Aion2Tickets.Snapshot();
        string? now = Aion2Tickets.CurrentName;
        return (characters.FirstOrDefault(c => c.Name == now && c.ServerId > 0) ?? characters.Where(c => c.ServerId > 0).OrderByDescending(c => c.SavedAt).FirstOrDefault())?.ServerId ?? 0;
    }

    private void ShowWorth(LootSession session, MarketBook? book, int server, bool ask)
    {
        var loc = LocalizationManager.Instance;
        _worth.Inlines.Clear();
        _worth.ToolTip = null;
        if (server <= 0)
        {
            return;
        }

        if (!ask && book is null)
        {
            _worth.Text = loc["Loot.WorthSignIn"];
            return;
        }

        if (book is null)
        {
            _worth.Text = MarketPrices.IsLoading(server) ? loc["Loot.WorthLoading"] : loc["Loot.WorthNone"];
            return;
        }

        var (worth, priced, unpriced) = MarketPrices.Worth(LootText.Merge(session.Maps).Select(i => (i.ItemId, i.Count)), book);
        var main = new System.Windows.Documents.Run(string.Format(loc["Loot.Worth"], worth.ToString("N0"), book.Name)) { FontWeight = FontWeights.SemiBold };
        main.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.Accent");
        _worth.Inlines.Add(main);
        TimeSpan span = session.End - session.Start;
        if (span >= TimeSpan.FromMinutes(5) && worth > 0)
        {
            _worth.Inlines.Add(new System.Windows.Documents.Run("  ·  " + string.Format(loc["Loot.WorthHour"], ((long)(worth / span.TotalHours)).ToString("N0"))));
        }

        if (unpriced > 0)
        {
            _worth.Inlines.Add(new System.Windows.Documents.Run("  ·  " + string.Format(loc["Loot.WorthUnpriced"], unpriced)));
        }

        _worth.ToolTip = string.Format(loc["Loot.WorthTip"], book.Name) + (book.Captured is DateTime at ? " " + at.ToLocalTime().ToString("g") : "");
    }

    /// <summary>1,234 / 12.3k / 1.23M: short enough for the item's line.</summary>
    internal static string Short(long kinah) => kinah switch
    {
        >= 1_000_000_000 => (kinah / 1e9).ToString("0.##") + "B",
        >= 1_000_000 => (kinah / 1e6).ToString("0.##") + "M",
        >= 10_000 => (kinah / 1e3).ToString("0.#") + "k",
        _ => kinah.ToString("N0"),
    };
}

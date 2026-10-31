using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Combat;
using AionDPS.History;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// The meter as an app (found by the user: the small meter window with its menus and a window per tool was hard to find one's way in): a column of sections
/// on the left (Home, Meter, History, Characters, Training dummy, Overlays, then the tools, then the account and Settings) and the chosen one on the right.
/// "Meter" is the live meter (MeterPage, as it always was); every other section is a page in AppPageHost: Home and Overlays are built here, History and the
/// tools are the pages of their old windows (the window is never shown, its content is taken in and it is closed when the page goes).
/// </summary>
public partial class MainWindow
{
    private sealed record ShellItem(string Id, string Label, string Glyph);

    private string _shellPage = "home";
    private ToolPage? _shellTool;
    private Window? _shellWindow;
    private readonly Dictionary<string, Border> _shellButtons = new();
    private readonly DispatcherTimer _shellTick = new() { Interval = TimeSpan.FromSeconds(30) };
    private StackPanel? _shellNavList;
    private bool _shellBuilt;
    private bool _shellCollapsed;

    /// <summary>The sections: the main ones, the tools (their names are the Tools menu's), then the account and Settings at the bottom.</summary>
    private List<(string? Group, List<ShellItem> Items)> ShellSections()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var main = new List<ShellItem>
        {
            new("home", loc["Shell.Home"], ""), new("meter", loc["Shell.Meter"], ""), new("history", loc["Shell.History"], ""),
            new("characters", loc["Shell.Characters"], ""), new("dummy", loc["Shell.Dummy"], ""), new("overlays", loc["Shell.Overlays"], ""),
        };
        var glyphs = new Dictionary<string, string>
        {
            ["assist"] = "", ["combat"] = "", ["petmap"] = "", ["petmap.pets"] = "", ["petmap.gather"] = "", ["loot"] = "",
            ["craft"] = "", ["timetable"] = "", ["boss"] = "", ["attributes"] = "", ["statcalc"] = "", ["analyzer"] = "",
            ["stream"] = "", ["network"] = "", ["theme"] = "",
        };
        var tools = ToolsWindow.Groups().SelectMany(g => g).Where(t => t.Id is not ("checklist" or "dummy"))
            .Where(t => !PrivacyConsent.Restricted || PrivacyConsent.RestrictedTools.Contains(t.Id))
            .Select(t => new ShellItem(t.Id, t.Title, glyphs.GetValueOrDefault(t.Id, ""))).ToList();
        if (PrivacyConsent.Restricted)
        {
            main.RemoveAll(i => i.Id is "dummy" && !PrivacyConsent.RestrictedTools.Contains("dummy"));
        }

        return new()
        {
            (null, main), (loc["Shell.Tools"], tools),
            ("", new List<ShellItem> { new("account", loc["Shell.Account"], ""), new("settings", loc["Shell.Settings"], "") }),
        };
    }

    /// <summary>Builds the column of sections and opens Home (once, at the start; again when the language changes).</summary>
    internal void BuildShell()
    {
        _shellCollapsed = MeterSettings.Load().AppNavCollapsed;
        _shellButtons.Clear();
        var list = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        foreach (var (group, items) in ShellSections())
        {
            if (group is { Length: > 0 } && !_shellCollapsed)
            {
                TextBlock head = ShellText(group.ToUpperInvariant(), "Brush.TextSubtle", 10.5, bold: true);
                head.Margin = new Thickness(18, 14, 0, 4);
                list.Children.Add(head);
            }
            else if (group is not null)
            {
                var line = new Border { Height = 1, Margin = new Thickness(12, 10, 12, 6) };
                line.SetResourceReference(Border.BackgroundProperty, "Brush.Border");
                list.Children.Add(line);
            }

            foreach (ShellItem item in items)
            {
                list.Children.Add(ShellButton(item));
            }
        }

        // the column folds to its pictures (the choice is kept)
        var fold = new Border { Padding = new Thickness(0, 9, 0, 9), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = LocalizationManager.Instance[_shellCollapsed ? "Shell.Unfold" : "Shell.Fold"] };
        TextBlock arrow = ShellText(_shellCollapsed ? "" : "", "Brush.TextMuted", 12);
        arrow.FontFamily = new FontFamily("Segoe MDL2 Assets");
        arrow.HorizontalAlignment = HorizontalAlignment.Center;
        fold.Child = arrow;
        fold.MouseLeftButtonUp += (_, _) =>
        {
            var settings = MeterSettings.Load();
            settings.AppNavCollapsed = !_shellCollapsed;
            settings.Save();
            BuildShell();
        };
        var dock = new DockPanel();
        DockPanel.SetDock(fold, Dock.Bottom);
        dock.Children.Add(fold);
        dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list });
        AppNav.Child = dock;
        AppNav.Width = _shellCollapsed ? 54 : 196;
        _shellNavList = list;
        if (!_shellBuilt)
        {
            _shellBuilt = true;
            _shellTick.Tick += (_, _) =>
            {
                if (_shellPage is "home" or "overlays" && IsVisible && WindowState != WindowState.Minimized)
                {
                    ShowShellPage(_shellPage);
                }
            };
            _shellTick.Start();
        }

        ShowShellPage(_shellPage);
    }

    private Border ShellButton(ShellItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_shellCollapsed ? 30 : 26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        TextBlock glyph = ShellText(item.Glyph, "Brush.TextMuted", 14);
        glyph.FontFamily = new FontFamily("Segoe MDL2 Assets");
        glyph.HorizontalAlignment = _shellCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        grid.Children.Add(glyph);
        if (!_shellCollapsed)
        {
            TextBlock label = ShellText(item.Label, "Brush.TextMuted", 13);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
        }
        else
        {
            Grid.SetColumnSpan(glyph, 2);
        }

        var button = new Border
        {
            Child = grid, Padding = new Thickness(_shellCollapsed ? 4 : 12, 7, 8, 7), Margin = new Thickness(6, 1, 6, 1), CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent, Cursor = Cursors.Hand, BorderThickness = new Thickness(3, 0, 0, 0), BorderBrush = Brushes.Transparent, Tag = item.Id,
            ToolTip = _shellCollapsed ? item.Label : null,
        };
        button.MouseEnter += (_, _) =>
        {
            if (_shellPage != item.Id)
            {
                button.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
            }
        };
        button.MouseLeave += (_, _) =>
        {
            if (_shellPage != item.Id)
            {
                button.Background = Brushes.Transparent;
            }
        };
        button.MouseLeftButtonUp += (_, _) => ShowShellPage(item.Id);
        _shellButtons[item.Id] = button;
        return button;
    }

    /// <summary>The chosen section lit (a gold edge and a raised background), the others quiet.</summary>
    private void MarkShell()
    {
        foreach (var (id, button) in _shellButtons)
        {
            bool on = id == _shellPage;
            if (on)
            {
                button.SetResourceReference(Border.BackgroundProperty, "Brush.ControlRaised");
                button.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
            }
            else
            {
                button.Background = Brushes.Transparent;
                button.BorderBrush = Brushes.Transparent;
            }

            foreach (TextBlock text in ((Grid)button.Child).Children.OfType<TextBlock>())
            {
                text.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.Text" : "Brush.TextMuted");
            }
        }
    }

    /// <summary>Opens a section. The account and Settings open their windows; the combat analyzer is a large window of its own.</summary>
    internal void ShowShellPage(string id)
    {
        if (!_shellBuilt)
        {
            return;
        }

        switch (id)
        {
            case "account":
                OnAccountClicked(this, new RoutedEventArgs());
                return;
            case "settings":
                OnSettingsClicked(this, new RoutedEventArgs());
                return;
            case "analyzer":
                OpenSessionManager();
                return;
            case "checklist":
                id = "characters";
                break;
        }

        bool same = id == _shellPage && AppPageHost.Children.Count > 0;
        if (!same || id is not ("home" or "overlays"))
        {
            LeaveShellPage();
        }

        _shellPage = id;
        MarkShell();
        bool meter = id == "meter";
        MeterPage.Visibility = meter ? Visibility.Visible : Visibility.Collapsed;
        AppPageHost.Visibility = meter ? Visibility.Collapsed : Visibility.Visible;
        if (meter)
        {
            AppPageHost.Children.Clear();
            return;
        }

        UIElement content = id switch
        {
            "home" => BuildHomePage(),
            "overlays" => BuildOverlaysPage(),
            "history" => HostWindow(new HistoryWindow()),
            "characters" => HostTool("checklist"),
            _ => HostTool(id),
        };
        AppPageHost.Children.Clear();
        AppPageHost.Children.Add(content);
    }

    /// <summary>What the page that goes away leaves behind: its timers stop, its old window closes.</summary>
    private void LeaveShellPage()
    {
        _shellTool?.Cleanup?.Invoke();
        _shellTool = null;
        _shellWindow?.Close();
        _shellWindow = null;
        AppPageHost.Children.Clear();
    }

    private UIElement HostWindow(Window window)
    {
        _shellWindow = window;
        return ThemedChrome.TakeContent(window) ?? new Grid();
    }

    private UIElement HostTool(string id)
    {
        _shellTool = BuildToolPage(id);
        if (_shellTool?.Content is { } content)
        {
            if (content is FrameworkElement element && element.Parent is Panel parent)
            {
                parent.Children.Remove(element);
            }

            return content;
        }

        TextBlock none = ShellText(LocalizationManager.Instance["Shell.NotYet"], "Brush.TextMuted", 13);
        none.Margin = new Thickness(24);
        none.TextWrapping = TextWrapping.Wrap;
        return none;
    }

    // ---- Home ----

    /// <summary>
    /// Home: everything at a glance. The resets and the characters' Odyle Energy (who is full, who is soon), the recent fights, the training dummy, the overlays
    /// with a switch each, and the account. Every card opens its section.
    /// </summary>
    private UIElement BuildHomePage()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        DateTime now = DateTime.Now, utcNow = DateTime.UtcNow;
        var page = new StackPanel { Margin = new Thickness(24, 18, 24, 24) };
        page.Children.Add(PageTitle(loc["Shell.Home"], Account.SignedIn && Account.Name is { } name ? string.Format(CultureInfo.CurrentCulture, loc["Shell.HomeHello"], name) : loc["Shell.HomeLede"]));

        var cards = new WrapPanel();
        page.Children.Add(cards);

        // the resets
        var resets = new StackPanel();
        if (Aion2Tickets.Find(4) is { } daily && Aion2Tickets.NextReset(daily, now) is { } nextDaily)
        {
            resets.Children.Add(BigLine(loc["Checklist.DailyReset"], ShortSpan(nextDaily - now), nextDaily.ToString("t", CultureInfo.CurrentCulture)));
        }

        if (Aion2Tickets.Find(101) is { } weekly && Aion2Tickets.NextReset(weekly, now) is { } nextWeekly)
        {
            resets.Children.Add(BigLine(loc["Checklist.WeeklyReset"], ShortSpan(nextWeekly - now), nextWeekly.ToString("ddd", CultureInfo.CurrentCulture) + " " + nextWeekly.ToString("t", CultureInfo.CurrentCulture)));
        }

        cards.Children.Add(HomeCard(loc["Shell.Resets"], resets, null, 300));

        // the characters' Odyle Energy: who is full, who is next
        string? online = Aion2Tickets.CurrentName;
        var odyle = Aion2Tickets.Snapshot().Where(c => c.Tickets.Count > 0)
            .Select(c => (Character: c, State: Aion2Tickets.OdyleNow(c, utcNow, c.Name == online))).Where(x => x.State is not null)
            .OrderBy(x => x.State!.Full ? DateTime.MinValue : x.State.FullAt ?? DateTime.MaxValue).Take(6).ToList();
        var energy = new StackPanel();
        if (odyle.Count == 0)
        {
            energy.Children.Add(Wrapped(loc["Shell.NoCharacters"], "Brush.TextMuted", 12.5));
        }

        foreach (var (character, state) in odyle)
        {
            energy.Children.Add(OdyleRow(character, state!, utcNow));
        }

        cards.Children.Add(HomeCard(loc["Shell.Odyle"], energy, "characters", 420));

        // the recent fights
        var fights = new StackPanel();
        IReadOnlyList<FightLogEntry> entries = FightLog.Entries();
        if (entries.Count == 0)
        {
            fights.Children.Add(Wrapped(loc["Shell.NoFights"], "Brush.TextMuted", 12.5));
        }

        foreach (FightLogEntry entry in entries.Take(6))
        {
            fights.Children.Add(FightRow(entry, now));
        }

        cards.Children.Add(HomeCard(loc["Shell.RecentFights"], fights, "history", 420));

        // the training dummy
        var settings = MeterSettings.Load();
        var dummy = new StackPanel();
        int seconds = Math.Clamp(settings.DummyTestSeconds, 10, 3600);
        dummy.Children.Add(Wrapped(settings.DummyAutoStart
            ? string.Format(CultureInfo.CurrentCulture, loc["Shell.DummyOn"], seconds % 60 == 0 ? $"{seconds / 60} min" : $"{seconds} s")
            : loc["Shell.DummyOff"], "Brush.Text", 13));
        if (entries.FirstOrDefault(e => e.Kind == FightLogKind.Dummy) is { } lastDummy)
        {
            TextBlock last = Wrapped(string.Format(CultureInfo.CurrentCulture, loc["Shell.DummyLast"], lastDummy.YourDps.ToString("N0", CultureInfo.CurrentCulture), lastDummy.Detail ?? "", Ago(now - lastDummy.Start)), "Brush.TextMuted", 12);
            last.Margin = new Thickness(0, 6, 0, 0);
            dummy.Children.Add(last);
        }

        cards.Children.Add(HomeCard(loc["Shell.Dummy"], dummy, "dummy", 300));

        // the overlays: a switch each for the main ones
        var overlays = new StackPanel();
        foreach (OverlayDef def in OverlayDefs().Take(5))
        {
            overlays.Children.Add(OverlaySwitch(def, compact: true));
        }

        cards.Children.Add(HomeCard(loc["Shell.Overlays"], overlays, "overlays", 300));

        // the account
        var account = new StackPanel();
        account.Children.Add(Wrapped(Account.SignedIn ? string.Format(CultureInfo.CurrentCulture, loc["Shell.SignedIn"], Account.Name ?? "Discord") : loc["Shell.SignedOut"], "Brush.Text", 13));
        TextBlock site = ShellLink(Account.SignedIn ? loc["Shell.SiteRoster"] : loc["Shell.SignIn"], () =>
        {
            if (Account.SignedIn)
            {
                OpenUrl(Brand.Site + "/meter/roster");
            }
            else
            {
                OnAccountClicked(this, new RoutedEventArgs());
            }
        });
        site.Margin = new Thickness(0, 8, 0, 0);
        account.Children.Add(site);
        cards.Children.Add(HomeCard(loc["Shell.Account"], account, null, 420));
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
    }

    private UIElement OdyleRow(TicketCharacter c, Aion2Tickets.OdyleState o, DateTime utcNow)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        string? cls = c.ClassCode > 0 ? Aion2.Protocol.Aion2SkillNames.ClassFromCode(c.ClassCode) : null;
        TextBlock name = ShellText(c.Name, "Brush.Text", 13, bold: true);
        if (cls is not null)
        {
            name.Foreground = ClassBrushes.Solid(cls);
        }

        name.TextTrimming = TextTrimming.CharacterEllipsis;
        grid.Children.Add(name);
        var energy = new StackPanel { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        TextBlock numbers = ShellText((o.Estimated ? "~" : "") + o.Count.ToString("N0", CultureInfo.CurrentCulture) + " / " + o.Cap.ToString("N0", CultureInfo.CurrentCulture), o.Full ? "Brush.Warning" : "Brush.Accent", 12, bold: true);
        energy.Children.Add(numbers);
        var bar = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, o.Cap), Value = Math.Min(o.Count, o.Cap), Height = 3, Margin = new Thickness(0, 2, 0, 0) };
        bar.SetResourceReference(Control.ForegroundProperty, o.Full ? "Brush.Warning" : "Brush.Accent");
        bar.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
        bar.BorderThickness = new Thickness(0);
        energy.Children.Add(bar);
        Grid.SetColumn(energy, 1);
        grid.Children.Add(energy);
        bool soon = !o.Full && o.FullAt is { } f && f - utcNow < TimeSpan.FromHours(6);
        TextBlock full = ShellText(o.Full ? loc["Checklist.RosterFull"] : o.FullAt is { } at ? string.Format(CultureInfo.CurrentCulture, loc["Checklist.In"], ShortSpan(at - utcNow)) : "–",
            o.Full || soon ? "Brush.Warning" : "Brush.TextMuted", 12, bold: o.Full || soon);
        full.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(full, 2);
        grid.Children.Add(full);
        return grid;
    }

    private UIElement FightRow(FightLogEntry entry, DateTime now)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        TextBlock title = ShellText(entry.Title + (entry.Detail is { Length: > 0 } d ? " · " + d : ""), "Brush.Text", 13);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        left.Children.Add(title);
        string state = entry.Kind == FightLogKind.Boss ? (entry.Killed == true ? "Killed" : "Wipe") : entry.Kind.ToString();
        left.Children.Add(ShellText($"{Ago(now - entry.Start)} · {TimeSpan.FromSeconds(entry.Seconds):m\\:ss} · {state}", "Brush.TextSubtle", 11));
        grid.Children.Add(left);
        if (entry.YourDps > 0)
        {
            TextBlock dps = ShellText(PlayerRow.Compact((long)entry.YourDps) + " DPS", "Brush.Accent", 13, bold: true);
            dps.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(dps, 1);
            grid.Children.Add(dps);
        }

        grid.MouseLeftButtonUp += async (_, e) =>
        {
            e.Handled = true;
            FightReport? report = await System.Threading.Tasks.Task.Run(() => FightLog.Open(entry) is { } input ? FightReport.Build(input) : null);
            if (report is not null)
            {
                new FightReportWindow(report) { Owner = this }.Show();
            }
        };
        return grid;
    }

    // ---- Overlays ----

    private sealed record OverlayDef(string Name, string What, Func<MeterSettings, bool> Get, Action<MeterSettings, bool> Set, string? Page, Action? Reset = null);

    /// <summary>Every overlay over the game, the main ones first: its switch, what it shows and where its settings are.</summary>
    private List<OverlayDef> OverlayDefs()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        return new()
        {
            new(loc["Shell.OvMeter"], loc["Shell.OvMeterWhat"], s => s.MeterOverlayOn, (s, v) => s.MeterOverlayOn = v, "meter", ResetMeterOverlayPlace),
            new(loc["Settings.Nav.Combat"], loc["Shell.OvCombatWhat"], s => s.ShowCombatOverlay, (s, v) => s.ShowCombatOverlay = v, "combat"),
            new(loc["Main.MenuTools.Assist"], loc["Shell.OvAssistWhat"], s => s.ShowAssistOverlay, (s, v) => s.ShowAssistOverlay = v, "assist"),
            new(loc["Settings.Nav.Timetable"], loc["Shell.OvTimetableWhat"], s => s.ShowTimetable, (s, v) => s.ShowTimetable = v, "timetable"),
            new(loc["Settings.Nav.Boss"], loc["Shell.OvBossWhat"], s => s.ShowBossOverlay, (s, v) => s.ShowBossOverlay = v, "boss"),
            new(loc["Settings.Nav.PetMap"], loc["Shell.OvPetMapWhat"], s => s.ShowPetMap, (s, v) => s.ShowPetMap = v, "petmap"),
            new(loc["Shell.OvPetFarm"], loc["Shell.OvPetFarmWhat"], s => s.ShowPetFarm, (s, v) => s.ShowPetFarm = v, "petmap.pets"),
            new(loc["Main.MenuTools.Loot"], loc["Shell.OvLootWhat"], s => s.ShowLootOverlay, (s, v) => s.ShowLootOverlay = v, "loot"),
            new(loc["Main.MenuTools.Craft"], loc["Shell.OvCraftWhat"], s => s.ShowCraftOverlay, (s, v) => s.ShowCraftOverlay = v, "craft"),
            new(loc["Settings.Nav.Network"], loc["Shell.OvNetworkWhat"], s => s.ShowNetworkOverlay, (s, v) => s.ShowNetworkOverlay = v, "network"),
            new(loc["Shell.OvHotkeys"], loc["Shell.OvHotkeysWhat"], s => s.ShowHotkeyOverlay, (s, v) => s.ShowHotkeyOverlay = v, null),
            new(loc["Main.MenuTools.Stream"], loc["Shell.OvStreamWhat"], s => s.StreamOverlayOn, (s, v) => s.StreamOverlayOn = v, "stream"),
        };
    }

    /// <summary>Overlays: every overlay over the game in one list, each with its switch, what it shows and a way to its settings; the meter can be put back in place.</summary>
    private UIElement BuildOverlaysPage()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var page = new StackPanel { Margin = new Thickness(24, 18, 24, 24), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        page.Children.Add(PageTitle(loc["Shell.Overlays"], loc["Shell.OverlaysLede"]));
        var list = new StackPanel();
        foreach (OverlayDef def in OverlayDefs())
        {
            list.Children.Add(OverlaySwitch(def, compact: false));
        }

        page.Children.Add(HomeCard(null, list, null, 0));
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
    }

    private UIElement OverlaySwitch(OverlayDef def, bool compact)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var grid = new Grid { Margin = new Thickness(0, compact ? 1 : 6, 0, compact ? 1 : 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(ShellText(def.Name, "Brush.Text", 13, bold: !compact));
        if (!compact)
        {
            TextBlock what = Wrapped(def.What, "Brush.TextMuted", 12);
            what.Margin = new Thickness(0, 2, 12, 0);
            text.Children.Add(what);
            var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            if (def.Page is { } pageId)
            {
                links.Children.Add(ShellLink(loc["Shell.OvSettings"], () => ShowShellPage(pageId)));
            }

            if (def.Reset is { } reset)
            {
                TextBlock back = ShellLink(loc["Shell.OvReset"], reset);
                back.Margin = new Thickness(def.Page is null ? 0 : 16, 0, 0, 0);
                links.Children.Add(back);
            }

            if (links.Children.Count > 0)
            {
                text.Children.Add(links);
            }
        }

        grid.Children.Add(text);
        var toggle = new CheckBox { IsChecked = def.Get(MeterSettings.Load()), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0) };
        if (Application.Current?.TryFindResource("SettingsSwitch") is Style style)
        {
            toggle.Style = style;
        }

        toggle.Click += (_, _) =>
        {
            var settings = MeterSettings.Load();
            def.Set(settings, toggle.IsChecked == true);
            settings.Save();
            ApplySettingsEverywhere(settings);
        };
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        if (!compact)
        {
            var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = grid, Padding = new Thickness(0, 0, 0, 8) };
            line.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            return line;
        }

        return grid;
    }

    /// <summary>The meter overlay back at its first place (right of the middle of the main screen).</summary>
    private void ResetMeterOverlayPlace()
    {
        var settings = MeterSettings.Load();
        settings.MeterOverlayLeft = null;
        settings.MeterOverlayTop = null;
        settings.Save();
        _meterOverlay?.PlaceFromSettings();
        _overlayPeekUntil = DateTime.Now + _meterOverlayHideAfter; // shown for a moment, so it can be seen where it is now
        UpdateMeterOverlay();
    }

    // ---- small parts ----

    private static UIElement PageTitle(string title, string lede)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        TextBlock head = ShellText(title, "Brush.Text", 24);
        head.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Title");
        stack.Children.Add(head);
        TextBlock sub = Wrapped(lede, "Brush.TextMuted", 13);
        sub.Margin = new Thickness(0, 4, 0, 0);
        sub.MaxWidth = 760;
        sub.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(sub);
        return stack;
    }

    /// <summary>A card of Home: a small title with an arrow to its section, the content under it.</summary>
    private Border HomeCard(string? title, UIElement content, string? opens, double width)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        if (title is not null)
        {
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            if (opens is not null)
            {
                TextBlock more = ShellLink(loc["Shell.Open"], () => ShowShellPage(opens));
                DockPanel.SetDock(more, Dock.Right);
                head.Children.Add(more);
            }

            head.Children.Add(ShellText(title.ToUpperInvariant(), "Brush.TextMuted", 11, bold: true));
            stack.Children.Add(head);
        }

        stack.Children.Add(content);
        var card = new Border
        {
            Child = stack, Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
        };
        if (width > 0)
        {
            card.Width = width;
        }

        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    private static UIElement BigLine(string label, string value, string sub)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(ShellText(label, "Brush.TextMuted", 12));
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(ShellText(value, "Brush.Text", 20, bold: true));
        TextBlock small = ShellText("  " + sub, "Brush.TextSubtle", 12);
        small.VerticalAlignment = VerticalAlignment.Bottom;
        small.Margin = new Thickness(0, 0, 0, 3);
        line.Children.Add(small);
        stack.Children.Add(line);
        return stack;
    }

    private static TextBlock ShellText(string text, string brush, double size, bool bold = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private static TextBlock Wrapped(string text, string brush, double size)
    {
        TextBlock block = ShellText(text, brush, size);
        block.TextWrapping = TextWrapping.Wrap;
        return block;
    }

    private static TextBlock ShellLink(string text, Action act)
    {
        TextBlock link = ShellText(text, "Brush.Accent", 12.5, bold: true);
        link.Cursor = Cursors.Hand;
        link.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            act();
        };
        return link;
    }

    private static string ShortSpan(TimeSpan left)
    {
        left = left < TimeSpan.Zero ? TimeSpan.Zero : left;
        return left.TotalDays >= 1 ? $"{(int)left.TotalDays} d {left.Hours} h" : left.TotalHours >= 1 ? $"{(int)left.TotalHours} h {left.Minutes} m" : $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} m";
    }

    private static string Ago(TimeSpan ago) => string.Format(CultureInfo.CurrentCulture, LocalizationManager.Instance["Checklist.Ago"], ShortSpan(ago));

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // no browser to open it with
        }
    }
}

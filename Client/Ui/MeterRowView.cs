using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>The titles of the meter's own three columns, which follow the mode (DPS, rDPS, HPS …; Damage, Heal, Taken in All in One): set by the main window.</summary>
public sealed class MeterTitles : INotifyPropertyChanged
{
    public static MeterTitles Instance { get; } = new();

    private string _rate = "DPS";
    private string _share = "%";
    private string _total = "Total";

    public string Rate => _rate;

    public string Share => _share;

    public string Total => _total;

    public void Set(string rate, string share, string total)
    {
        Change(ref _rate, rate, nameof(Rate));
        Change(ref _share, share, nameof(Share));
        Change(ref _total, total, nameof(Total));
    }

    private void Change(ref string field, string value, string name)
    {
        if (field != value)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// One line of the meter over the game, built from a <see cref="MeterLook"/> (its bar, size, font and columns) and bound to a
/// <see cref="PlayerRow"/>; or, with <see cref="IsHeader"/>, the line of column titles over them. The look page's preview uses the same lines.
/// </summary>
public sealed class MeterRowView : Grid
{
    public static readonly DependencyProperty LookProperty = DependencyProperty.Register(nameof(Look), typeof(MeterLook), typeof(MeterRowView),
        new PropertyMetadata(null, (d, _) => ((MeterRowView)d).Build()));

    public static readonly DependencyProperty IsHeaderProperty = DependencyProperty.Register(nameof(IsHeader), typeof(bool), typeof(MeterRowView),
        new PropertyMetadata(false, (d, _) => ((MeterRowView)d).Build()));

    public static readonly DependencyProperty ClickableProperty = DependencyProperty.Register(nameof(Clickable), typeof(bool), typeof(MeterRowView),
        new PropertyMetadata(false, (d, _) => ((MeterRowView)d).Build()));

    public MeterLook? Look
    {
        get => (MeterLook?)GetValue(LookProperty);
        set => SetValue(LookProperty, value);
    }

    public bool IsHeader
    {
        get => (bool)GetValue(IsHeaderProperty);
        set => SetValue(IsHeaderProperty, value);
    }

    public bool Clickable
    {
        get => (bool)GetValue(ClickableProperty);
        set => SetValue(ClickableProperty, value);
    }

    /// <summary>A line of the meter was clicked (the main window opens the player's details).</summary>
    public static event Action<PlayerRow>? RowClicked;

    private static readonly ClassIconConverter Icons = new();
    private static readonly BooleanToVisibilityConverter Shown = new();
    private static readonly Dictionary<string, ControlTemplate> BarTemplates = new();
    private static readonly Dictionary<string, Style> BarStyles = new();

    /// <summary>The template of the meter's lines in this look.</summary>
    public static DataTemplate TemplateFor(MeterLook look, bool clickable)
    {
        var factory = new FrameworkElementFactory(typeof(MeterRowView));
        factory.SetValue(ClickableProperty, clickable);
        factory.SetValue(LookProperty, look);
        var template = new DataTemplate { VisualTree = factory };
        template.Seal();
        return template;
    }

    public MeterRowView()
    {
        MouseLeftButtonUp += (_, e) =>
        {
            if (Clickable && !IsHeader && DataContext is PlayerRow row)
            {
                e.Handled = true;
                RowClicked?.Invoke(row);
            }
        };
    }

    private void Build()
    {
        Children.Clear();
        ColumnDefinitions.Clear();
        if (Look is not { } look)
        {
            return;
        }

        LocalizationManager loc = LocalizationManager.Instance;
        TextElement.SetFontFamily(this, look.FontFamily);
        Height = IsHeader ? Math.Round(look.TinySize + 9) : look.RowHeight;
        Margin = new Thickness(0, 0, 0, IsHeader ? 1 : look.Gap);
        Background = Brushes.Transparent;
        Cursor = Clickable && !IsHeader ? System.Windows.Input.Cursors.Hand : null;
        if (!IsHeader)
        {
            var bar = new ProgressBar
            {
                Template = BarTemplate(look.Bar), Style = BarStyle(look.Bar), Minimum = 0, Maximum = 100, IsHitTestVisible = false,
                Background = new SolidColorBrush(look.Bar switch { "glow" => Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF), "flat" => Color.FromArgb(0x06, 0xFF, 0xFF, 0xFF),
                    "capsule" => Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF), "thread" => Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), _ => Color.FromArgb(0x05, 0xFF, 0xFF, 0xFF) }),
            };
            bar.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new Binding(nameof(PlayerRow.FillPercent)) { Mode = BindingMode.OneWay });
            Children.Add(bar);
            if (look.Bar is "glow" or "flat")
            {
                var edge = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
                edge.SetBinding(Border.BackgroundProperty, new Binding(nameof(PlayerRow.ClassEdgeBrush)));
                Children.Add(edge);
            }
        }

        var line = new Grid { Margin = new Thickness(look.Bar == "capsule" ? 13 : 10, 0, look.Bar == "capsule" ? 12 : 10, 0) };
        Children.Add(line);
        void Add(UIElement element, GridLength width, string? shared = null)
        {
            var column = new ColumnDefinition { Width = width };
            if (shared is not null)
            {
                column.SharedSizeGroup = shared;
            }

            line.ColumnDefinitions.Add(column);
            SetColumn(element, line.ColumnDefinitions.Count - 1);
            line.Children.Add(element);
        }

        TextBlock Text(double size, string brush, bool bold = false, HorizontalAlignment align = HorizontalAlignment.Right)
        {
            var text = new TextBlock { FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = align, TextTrimming = TextTrimming.CharacterEllipsis };
            Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
            if (bold)
            {
                text.FontWeight = FontWeights.SemiBold;
            }

            text.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return text;
        }

        TextBlock Title(string text, string? tip, HorizontalAlignment align = HorizontalAlignment.Right)
        {
            TextBlock title = Text(look.TinySize, "Brush.TextMuted", true, align);
            title.Text = text;
            title.ToolTip = tip;
            return title;
        }

        // rank, class icon and the name side
        if (look.Has("rank"))
        {
            TextBlock rank = IsHeader ? Title("#", null) : Text(look.SmallSize, "Brush.TextSubtle", true);
            if (!IsHeader)
            {
                rank.SetBinding(TextBlock.TextProperty, new Binding(nameof(PlayerRow.RankDisplay)));
            }

            rank.Margin = new Thickness(0, 0, 6, 0);
            rank.MinWidth = Math.Round(14 * look.TextScale);
            Add(rank, GridLength.Auto);
        }

        if (look.Has("icon"))
        {
            var icon = new Image { Width = look.IconSize, Height = look.IconSize, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            if (!IsHeader)
            {
                icon.SetBinding(Image.SourceProperty, new Binding(nameof(PlayerRow.ClassName)) { Converter = Icons });
            }

            Add(icon, GridLength.Auto);
        }

        if (IsHeader)
        {
            Add(Title(loc["Look.ColName"], null, HorizontalAlignment.Left), new GridLength(1, GridUnitType.Star));
        }
        else
        {
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
            TextBlock who = Text(look.NameSize, "Brush.OverlayText", true, HorizontalAlignment.Left);
            who.SetBinding(TextBlock.TextProperty, new Binding(nameof(PlayerRow.NameDisplay)));
            if (look.Has("classcolor"))
            {
                who.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(PlayerRow.ClassTextBrush)));
            }

            name.Children.Add(who);
            if (look.Has("cp"))
            {
                TextBlock cp = Text(look.TinySize, "Brush.TextMuted", align: HorizontalAlignment.Left);
                cp.Margin = new Thickness(7, 1, 0, 0);
                cp.SetBinding(TextBlock.TextProperty, new Binding(nameof(PlayerRow.CombatPowerText)));
                name.Children.Add(cp);
            }

            var buffs = new TextBlock { FontSize = Math.Round(look.TinySize - 0.5, 1), Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xC8, 0x8F)), Opacity = 0.85,
                Margin = new Thickness(7, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            buffs.SetBinding(TextBlock.TextProperty, new Binding(nameof(PlayerRow.BuffLine)));
            buffs.SetBinding(VisibilityProperty, new Binding(nameof(PlayerRow.HasBuffLine)) { Converter = Shown });
            name.Children.Add(buffs);
            Add(name, new GridLength(1, GridUnitType.Star));
        }

        // the columns, in their fixed order
        foreach (MeterColumn column in MeterLook.Columns.Where(c => look.Has(c.Id)))
        {
            string? tip = column.TipKey.Length > 0 ? loc[column.TipKey] : null;
            switch (column.Id)
            {
                case "support":
                {
                    UIElement cell = IsHeader ? Title(loc["Look.ColSupport"], tip, HorizontalAlignment.Left) : new SupportBadgesView(look);
                    if (cell is FrameworkElement badges)
                    {
                        badges.Margin = new Thickness(8, 0, 0, 0);
                        if (!IsHeader)
                        {
                            badges.SetBinding(SupportBadgesView.BadgesProperty, new Binding(nameof(PlayerRow.SupportBadges)));
                        }
                    }

                    Add(cell, new GridLength(look.SupportWidth));
                    break;
                }

                case "dps":
                {
                    TextBlock rate = IsHeader ? Title("", tip) : Text(look.RateSize, "Brush.OverlayText", true);
                    rate.Margin = new Thickness(10, 0, 0, 0);
                    rate.FontWeight = IsHeader ? FontWeights.SemiBold : FontWeights.Bold;
                    rate.SetBinding(TextBlock.TextProperty, IsHeader ? new Binding(nameof(MeterTitles.Rate)) { Source = MeterTitles.Instance } : new Binding(column.Path));
                    Add(rate, GridLength.Auto, "Rate");
                    break;
                }

                case "share" or "total":
                {
                    TextBlock cell = IsHeader ? Title("", tip) : Text(column.Id == "share" ? look.SmallSize : Math.Round(12 * look.NameSize / 13, 1), "Brush.TextSubtle");
                    if (IsHeader)
                    {
                        cell.SetBinding(TextBlock.TextProperty, new Binding(column.Id == "share" ? nameof(MeterTitles.Share) : nameof(MeterTitles.Total)) { Source = MeterTitles.Instance });
                    }
                    else
                    {
                        cell.SetResourceReference(StyleProperty, column.Id == "share" ? "AllHealSubtle" : "AllTakenSubtle");
                        cell.SetBinding(TextBlock.TextProperty, new Binding(column.Path));
                    }

                    Add(cell, new GridLength(look.WidthOf(column)));
                    break;
                }

                default:
                {
                    TextBlock cell = IsHeader ? Title(loc[column.TitleKey], tip) : Text(look.SmallSize, "Brush.TextSubtle");
                    if (!IsHeader)
                    {
                        cell.SetBinding(TextBlock.TextProperty, new Binding(column.Path));
                    }

                    Add(cell, new GridLength(look.WidthOf(column)));
                    break;
                }
            }
        }
    }

    /// <summary>The bar's shape for each style: PART_Track is the whole line, PART_Indicator the filled part (the player's share of the best).</summary>
    private static ControlTemplate BarTemplate(string bar)
    {
        if (BarTemplates.TryGetValue(bar, out ControlTemplate? known))
        {
            return known;
        }

        string inner = bar switch
        {
            "flat" => "<Border x:Name='PART_Track' Background='{TemplateBinding Background}'/>" +
                      "<Border x:Name='PART_Indicator' HorizontalAlignment='Left' Background='{TemplateBinding Foreground}'/>",
            "thread" => "<Border x:Name='PART_Track' Height='2' VerticalAlignment='Bottom' Background='{TemplateBinding Background}'/>" +
                        "<Border x:Name='PART_Indicator' Height='2' VerticalAlignment='Bottom' HorizontalAlignment='Left' Background='{TemplateBinding BorderBrush}'/>",
            "capsule" => "<Border x:Name='PART_Track' Margin='3,2' CornerRadius='6' Background='{TemplateBinding Background}'/>" +
                         "<Border x:Name='PART_Indicator' Margin='3,2' CornerRadius='6' HorizontalAlignment='Left' Background='{TemplateBinding Foreground}'/>",
            "frame" => "<Border x:Name='PART_Track' Margin='2,1' CornerRadius='3' BorderThickness='1' BorderBrush='#18FFFFFF' Background='{TemplateBinding Background}'/>" +
                       "<Grid x:Name='PART_Indicator' Margin='2,1' HorizontalAlignment='Left'>" +
                       "<Border CornerRadius='3' Background='{TemplateBinding Foreground}' Opacity='0.55'/>" +
                       "<Border CornerRadius='3' BorderThickness='1' BorderBrush='{TemplateBinding BorderBrush}'/></Grid>",
            _ => "<Border x:Name='PART_Track' Background='{TemplateBinding Background}'/>" +
                 "<Grid x:Name='PART_Indicator' HorizontalAlignment='Left'><Border Background='{TemplateBinding Foreground}'/>" +
                 "<Border Height='1' VerticalAlignment='Bottom' Background='{TemplateBinding BorderBrush}'/></Grid>",
        };
        var template = (ControlTemplate)XamlReader.Parse(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ProgressBar'>" +
            "<Grid>" + inner + "</Grid></ControlTemplate>");
        template.Seal();
        BarTemplates[bar] = template;
        return template;
    }

    /// <summary>The bar's colours: the class's (its glow for the glow style), an enemy's red in PvP.</summary>
    private static Style BarStyle(string bar)
    {
        if (BarStyles.TryGetValue(bar, out Style? known))
        {
            return known;
        }

        var style = new Style(typeof(ProgressBar));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new Binding(bar == "glow" ? nameof(PlayerRow.ClassGlowBrush) : nameof(PlayerRow.ClassBrush))));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new Binding(nameof(PlayerRow.ClassLineBrush))));
        var enemy = new DataTrigger { Binding = new Binding(nameof(PlayerRow.IsEnemy)), Value = true };
        enemy.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Brush.OverlayEnemy")));
        enemy.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("Brush.DangerSoft")));
        style.Triggers.Add(enemy);
        style.Seal();
        BarStyles[bar] = style;
        return style;
    }
}

/// <summary>The support badges of a line: each party buff's icon and uptime, framed in the colour of the class that gave it.</summary>
public sealed class SupportBadgesView : StackPanel
{
    public static readonly DependencyProperty BadgesProperty = DependencyProperty.Register(nameof(Badges), typeof(IReadOnlyList<SupportBadge>), typeof(SupportBadgesView),
        new PropertyMetadata(null, (d, _) => ((SupportBadgesView)d).Rebuild()));

    private readonly MeterLook _look;

    public SupportBadgesView(MeterLook look)
    {
        _look = look;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        ClipToBounds = true;
    }

    public IReadOnlyList<SupportBadge>? Badges
    {
        get => (IReadOnlyList<SupportBadge>?)GetValue(BadgesProperty);
        set => SetValue(BadgesProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();
        foreach (SupportBadge badge in Badges ?? Array.Empty<SupportBadge>())
        {
            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            double size = Math.Max(11, _look.IconSize - 4);
            inner.Children.Add(badge.Icon is null
                ? new Border { Width = size, Height = size, CornerRadius = new CornerRadius(2), Background = badge.Edge }
                : (UIElement)new Image { Source = badge.Icon, Width = size, Height = size, Stretch = Stretch.UniformToFill });
            var percent = new TextBlock { Text = badge.PercentText, FontSize = _look.TinySize, Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Typography.SetNumeralAlignment(percent, FontNumeralAlignment.Tabular);
            percent.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
            inner.Children.Add(percent);
            Children.Add(new Border
            {
                Child = inner, BorderBrush = badge.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(1, 1, 4, 1),
                Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = badge.Tip, Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)),
            });
        }
    }
}

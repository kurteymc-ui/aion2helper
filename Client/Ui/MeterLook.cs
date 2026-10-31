using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>One of the meter's optional columns: its id (kept in the settings), the title over it, how wide it is at the normal text size,
/// the row property it shows and what it means.</summary>
public sealed record MeterColumn(string Id, string TitleKey, double Width, string Path, string TipKey, bool Number = true);

/// <summary>
/// How the meter over the game looks (Overlays → Meter look): the bars, the height of a line, the font and its size, the columns, the
/// parts around the list and which way it grows. Read from the settings; <see cref="MeterRowView"/> builds the lines from it.
/// </summary>
public sealed class MeterLook
{
    /// <summary>The bar styles, in the order the page offers them.</summary>
    public static readonly string[] Bars = { "glow", "flat", "thread", "capsule", "frame" };

    public static readonly string[] Sizes = { "compact", "normal", "roomy" };

    /// <summary>The fonts, all of them part of Windows (the next one in the list stands in where one is missing).</summary>
    public static readonly (string Id, string Name, string Families)[] Fonts =
    {
        ("segoe", "Segoe UI", "Segoe UI Variable Text, Segoe UI"),
        ("bahnschrift", "Bahnschrift", "Bahnschrift, Segoe UI"),
        ("condensed", "Bahnschrift Condensed", "Bahnschrift SemiCondensed, Bahnschrift, Segoe UI"),
        ("mono", "Cascadia Mono", "Cascadia Mono, Consolas, Segoe UI"),
        ("trebuchet", "Trebuchet MS", "Trebuchet MS, Segoe UI"),
    };

    /// <summary>
    /// The columns a line can have, in the order they stand (left to right after the name). DPS, share and total are the meter's own three
    /// (their titles follow the mode: Heal, Taken … in All in One); the others are worked out from the hits of the fight shown.
    /// </summary>
    public static readonly MeterColumn[] Columns =
    {
        new("support", "Look.ColSupport", 0, nameof(PlayerRow.SupportBadges), "Look.ColSupportTip", Number: false),
        new("crit", "Look.ColCrit", 40, nameof(PlayerRow.CritText), "Look.ColCritTip"),
        new("back", "Look.ColBack", 40, nameof(PlayerRow.BackText), "Look.ColBackTip"),
        new("front", "Look.ColFront", 42, nameof(PlayerRow.FrontText), "Look.ColFrontTip"),
        new("perfect", "Look.ColPerfect", 44, nameof(PlayerRow.PerfectText), "Look.ColPerfectTip"),
        new("double", "Look.ColDouble", 44, nameof(PlayerRow.DoubleText), "Look.ColDoubleTip"),
        new("parry", "Look.ColParry", 44, nameof(PlayerRow.ParryText), "Look.ColParryTip"),
        new("hits", "Look.ColHits", 44, nameof(PlayerRow.HitsText), "Look.ColHitsTip"),
        new("ndps", "Look.ColNdps", 52, nameof(PlayerRow.NdpsText), "Look.ColNdpsTip"),
        new("rdps", "Look.ColRdps", 52, nameof(PlayerRow.RdpsText), "Look.ColRdpsTip"),
        new("given", "Look.ColGiven", 52, nameof(PlayerRow.GivenText), "Look.ColGivenTip"),
        new("dps", "", 0, nameof(PlayerRow.RateOrDamageCompact), "Look.ColDpsTip"),
        new("share", "", 48, nameof(PlayerRow.ShareOrHealCompact), "Look.ColShareTip"),
        new("total", "", 52, nameof(PlayerRow.TotalOrTakenCompact), "Look.ColTotalTip"),
    };

    /// <summary>The parts of the name side that can be switched off.</summary>
    public static readonly string[] NameParts = { "rank", "icon", "cp", "classcolor" };

    /// <summary>What a meter shows that was never set up.</summary>
    public static readonly string[] DefaultColumns = { "icon", "cp", "dps", "share", "total" };

    public string Bar { get; init; } = "glow";
    public string Size { get; init; } = "normal";
    public string Font { get; init; } = "segoe";

    /// <summary>The text size, 0.85 to 1.3 of the normal one.</summary>
    public double TextScale { get; init; } = 1.0;

    public IReadOnlySet<string> Shown { get; init; } = new HashSet<string>(DefaultColumns);

    /// <summary>A line of titles over the columns.</summary>
    public bool Titles { get; init; }

    public static MeterLook From(MeterSettings s) => new()
    {
        Bar = Bars.Contains(s.MeterBarStyle) ? s.MeterBarStyle : "glow",
        Size = Sizes.Contains(s.MeterRowSize) ? s.MeterRowSize : "normal",
        Font = Fonts.Any(f => f.Id == s.MeterFont) ? s.MeterFont : "segoe",
        TextScale = Math.Clamp(s.MeterTextScale, 0.85, 1.3),
        Shown = (s.MeterColumns ?? DefaultColumns.ToList()).ToHashSet(),
        Titles = s.MeterColumnTitles,
    };

    public bool Has(string id) => Shown.Contains(id);

    /// <summary>The height of a line.</summary>
    public double RowHeight => Math.Round((Size switch { "compact" => 22, "roomy" => 33, _ => 27 }) * Math.Max(1.0, 0.55 + 0.45 * TextScale));

    /// <summary>The space between two lines.</summary>
    public double Gap => Bar is "capsule" or "frame" ? 2 : 1;

    public FontFamily FontFamily => new(Fonts.FirstOrDefault(f => f.Id == Font).Families ?? "Segoe UI");

    private double SizeFactor => (Size == "compact" ? 0.93 : Size == "roomy" ? 1.05 : 1.0) * TextScale;

    public double NameSize => Math.Round(13 * SizeFactor, 1);

    public double RateSize => Math.Round(13.5 * SizeFactor, 1);

    public double SmallSize => Math.Round(11.5 * SizeFactor, 1);

    public double TinySize => Math.Round(10.5 * SizeFactor, 1);

    public double IconSize => Size == "compact" ? 15 : Size == "roomy" ? 21 : 18;

    /// <summary>The width a column takes at this text size.</summary>
    public double WidthOf(MeterColumn column) => Math.Round(column.Width * Math.Max(1.0, TextScale) * (Font == "mono" ? 1.12 : 1.0));

    /// <summary>The support badges' room: four badges at most.</summary>
    public double SupportWidth => Math.Round(4 * (IconSize + 24) * Math.Max(1.0, TextScale));

    /// <summary>
    /// How wide the meter is: the 380 of the usual three columns, wider for every column added (so the names keep their room), narrower for
    /// one taken away; with the text size.
    /// </summary>
    public double PanelWidth
    {
        get
        {
            double width = 380;
            foreach (MeterColumn column in Columns)
            {
                bool own = column.Id is "dps" or "share" or "total";
                double room = column.Id == "support" ? SupportWidth : column.Id == "dps" ? 64 : WidthOf(column);
                if (own && !Has(column.Id))
                {
                    width -= room;
                }
                else if (!own && Has(column.Id))
                {
                    width += room;
                }
            }

            width += Has("rank") ? 22 : 0;
            width -= Has("icon") ? 0 : 25;
            width *= 0.6 + 0.4 * TextScale;
            return Math.Round(Math.Clamp(width, 240, 1100));
        }
    }

    /// <summary>Everything that changes the lines, as one text: the lines are built again only when it changes.</summary>
    public string Key => string.Join("|", Bar, Size, Font, TextScale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Titles,
        string.Join(",", Shown.OrderBy(c => c, StringComparer.Ordinal)));
}

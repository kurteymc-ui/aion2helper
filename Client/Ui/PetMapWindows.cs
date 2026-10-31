using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// A small transparent overlay that can be placed and scaled on its own: locked it is click-through, unlocked it takes clicks, can be dragged by
/// its body and scaled with the handles at its corners and edges. Place and size are kept per overlay (the pet map and the pet list are two of them).
/// </summary>
public abstract class MovableOverlay : Window, IOverlayPlace
{
    private const double MinScale = 0.5, MaxScale = 3.0;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Border _frame;
    private readonly Border _bodyHost = new();
    private readonly Border _shiftHost = new();
    private readonly TranslateTransform _shift = new();
    private readonly Func<(double? Left, double? Top, double Scale)> _load;
    private readonly Action<double, double, double> _save;
    private readonly Action<double?, double?> _saveSize;
    private readonly Func<(double? Width, double? Height)> _loadSizeFn;
    private bool _locked = true;
    private readonly OverlayEdgeResize _edges;

    protected MovableOverlay(string title, UIElement body, Func<(double? Left, double? Top, double Scale)> load, Action<double, double, double> save, double defaultLeft, double defaultTop,
        Func<(double? Width, double? Height)> loadSize, Action<double?, double?> saveSize, OverlayEdgeMode edgeMode = OverlayEdgeMode.Crop)
    {
        Title = title;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AltTabHidden.Apply(this);
        ShowActivated = false; // the game keeps the keyboard
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        _load = load;
        _save = save;
        _saveSize = saveSize;
        _loadSizeFn = loadSize;

        var saved = load();
        SetScale(saved.Scale);
        _shiftHost.RenderTransform = _shift;
        _shiftHost.Child = body;
        _bodyHost.Child = _shiftHost;
        _bodyHost.ClipToBounds = true;
        var stack = new DockPanel { LastChildFill = true };
        stack.Children.Add(_bodyHost);
        _frame = new Border { Child = stack, LayoutTransform = _scale, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(2), Background = Brushes.Transparent };
        _frame.MouseLeftButtonDown += (_, e) =>
        {
            if (!_locked && e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                SavePlace();
            }
        };
        var size = loadSize();
        if (size.Width is > 0) { _frame.Width = size.Width.Value; }
        if (size.Height is > 0) { _frame.Height = size.Height.Value; }
        if (edgeMode == OverlayEdgeMode.CropAnchored && MeterSettings.Load().OverlayShifts.TryGetValue(title, out double[]? shift) && shift.Length == 2)
        {
            _shift.X = shift[0];
            _shift.Y = shift[1];
        }

        _edges = new OverlayEdgeResize(this, _frame, () => _scale.ScaleX, () => { }, SavePlace, SetScale, edgeMode, (x, y) => { _shift.X = x; _shift.Y = y; });
        Content = _edges.Wrap(_frame);
        Left = saved.Left ?? defaultLeft;
        Top = saved.Top ?? defaultTop;
        ApplyLocked(true);
        _defaultLeft = defaultLeft;
        _defaultTop = defaultTop;
        Action<string> onReset = title => { if (title == Title) { Dispatcher.Invoke(ReloadPlace); } };
        OverlayPlaces.WasReset += onReset;
        Closed += (_, _) => OverlayPlaces.WasReset -= onReset;
        OverlayDocking.Attach(this, SavePlace);
    }

    private readonly double _defaultLeft, _defaultTop;
    private DateTime _opacityAt = DateTime.MinValue;

    /// <summary>The place, size and scale are read from the settings again (after "reset size and position").</summary>
    private void ReloadPlace()
    {
        var saved = _load();
        SetScale(saved.Scale);
        var size = _loadSizeFn();
        _frame.Width = size.Width is > 0 ? size.Width.Value : double.NaN;
        _frame.MaxHeight = double.PositiveInfinity;
        _frame.Height = size.Height is > 0 ? size.Height.Value : double.NaN;
        _shift.X = MeterSettings.Load().OverlayShifts.TryGetValue(Title, out double[]? shifted) && shifted.Length == 2 ? shifted[0] : 0;
        _shift.Y = shifted is { Length: 2 } ? shifted[1] : 0;
        Left = saved.Left ?? _defaultLeft;
        Top = saved.Top ?? _defaultTop;
        ApplyLocked(_locked);
    }

    public bool IsLocked => _locked;

    // ---- the layout editor (OverlayLayout.cs) ----
    private bool _isView;

    public bool PlaceIsView => _isView;

    public Rect PlaceBounds
    {
        get
        {
            Size size = OverlayMeasure.SizeOf(this);
            return new Rect(double.IsFinite(Left) ? Left : 0, double.IsFinite(Top) ? Top : 0, size.Width, size.Height);
        }
    }

    public double PlaceScale => _scale.ScaleX;

    public void PlaceAt(double left, double top, bool save)
    {
        Left = left;
        Top = top;
        if (save)
        {
            SavePlace();
        }
    }

    public void PlaceScaleTo(double scale, bool save)
    {
        SetScale(scale);
        if (save)
        {
            SavePlace();
        }
    }

    /// <summary>The height the frame may take in its own units (the scaling excluded): the height it was pulled to; without one, the screen's.</summary>
    protected double HeightLimit
    {
        get
        {
            double h = !double.IsNaN(_frame.Height) ? _frame.Height : !double.IsInfinity(_frame.MaxHeight) ? _frame.MaxHeight : SystemParameters.WorkArea.Height;
            return h / Math.Max(_scale.ScaleY, 0.1);
        }
    }

    /// <summary>The size the body really has (in its own units, the frame's scaling excluded).</summary>
    /// <remarks>A view's body is laid out in the tool page, not in this never shown window (whose own size stays 0): its own size counts there,
    /// the whole view until it has been laid out.</remarks>
    protected double BodyWidth => _detached is FrameworkElement shown ? (shown.ActualWidth > 0 ? shown.ActualWidth : double.PositiveInfinity) : _bodyHost.ActualWidth;

    protected double BodyHeight => _detached is FrameworkElement shown ? (shown.ActualHeight > 0 ? shown.ActualHeight : double.PositiveInfinity) : _bodyHost.ActualHeight;

    private UIElement? _detached;

    /// <summary>Takes the content out of a window that is never shown, to put it into a tool's own window (a live view): the window keeps rendering into it.</summary>
    public UIElement? DetachBody()
    {
        _isView = true;
        UIElement? body = _shiftHost.Child;
        _shiftHost.Child = null;
        _detached = body;
        return body;
    }

    /// <summary>The content of the overlay (set by the derived window once it has built it).</summary>
    protected UIElement? Body
    {
        set => _shiftHost.Child = value;
    }

    /// <summary>Applies the lock only when it changed (the controller asks every tick).</summary>
    public void ApplyLockedIfChanged(bool locked)
    {
        if (locked != _locked)
        {
            ApplyLocked(locked);
        }
    }

    private void SetScale(double scale)
    {
        scale = Math.Clamp(double.IsFinite(scale) ? scale : 1.0, MinScale, MaxScale);
        _scale.ScaleX = scale;
        _scale.ScaleY = scale;
    }

    private void SavePlace()
    {
        _save(Left, Top, _scale.ScaleX);
        double? height = !double.IsNaN(_frame.Height) ? _frame.Height : !double.IsInfinity(_frame.MaxHeight) ? _frame.MaxHeight : null;
        _saveSize(double.IsNaN(_frame.Width) ? null : _frame.Width, height);
        if (_shift.X != 0 || _shift.Y != 0)
        {
            var s = MeterSettings.Load();
            s.OverlayShifts[Title] = new[] { _shift.X, _shift.Y };
            s.Save();
        }
    }

    /// <summary>Locked: click-through, no outline, no grip. Unlocked: clickable and outlined, so it can be found, moved and scaled.</summary>
    public void ApplyLocked(bool locked)
    {
        _locked = locked;
        // Unlocked, the frame has the height it was pulled to (the sample rows fill it); locked, that height is only the most it may take,
        // so a short list ends at its last row without empty space below.
        if (locked && !double.IsNaN(_frame.Height))
        {
            _frame.MaxHeight = _frame.Height;
            _frame.Height = double.NaN;
        }
        else if (!locked && !double.IsInfinity(_frame.MaxHeight))
        {
            _frame.Height = _frame.MaxHeight;
            _frame.MaxHeight = double.PositiveInfinity;
        }

        _edges.Show(!locked);
        _frame.Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;
        _frame.BorderBrush = locked ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42));
        _frame.Background = locked ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0));
        ApplyClickThrough();
    }

    private void ApplyClickThrough()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        if (handle != IntPtr.Zero)
        {
            using var native = new NativeOverlay(this);
            native.SetClickThrough(_locked);
        }
    }

    /// <summary>Shows or hides the window; the click-through style is set again after the first show (WPF may recreate the handle).</summary>
    public void ShowOverlay(bool visible)
    {
        if (!visible)
        {
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        if (!IsVisible || (DateTime.UtcNow - _opacityAt).TotalSeconds >= 2)
        {
            _opacityAt = DateTime.UtcNow;
            ApplyStoredOpacity(); // also while shown: a changed opacity in the settings takes effect within a moment
        }

        if (!IsVisible)
        {
            Show();
            ApplyClickThrough();
        }
    }

    private byte? _baseAlpha;

    /// <summary>A least opacity (0 to 1) the overlay keeps while set, whatever its own setting says (the pet list over the shop, where it sits on the game's own window text).</summary>
    public double? OpacityFloor { get; set; }

    /// <summary>The overlay's own opacity (0 to 1, set per overlay in its module; the key is its title) replaces the alpha of the body's dark background; <see cref="OpacityFloor"/> raises it.</summary>
    public void ApplyStoredOpacity()
    {
        if (_shiftHost.Child is not Border { Background: SolidColorBrush bg } body)
        {
            return;
        }

        _baseAlpha ??= bg.Color.A;
        double alpha = MeterSettings.Load().OverlayOpacities.TryGetValue(Title, out double v) ? Math.Clamp(v, 0, 1) : _baseAlpha.Value / 255.0;
        if (OpacityFloor is { } floor)
        {
            alpha = Math.Max(alpha, floor);
        }

        body.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), bg.Color.R, bg.Color.G, bg.Color.B));
    }

    public void RaiseToFront()
    {
        if (IsVisible)
        {
            NativeOverlay.KeepOnTop(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        }
    }
}

/// <summary>The colours of the chosen pets (the same on the map and in the list).</summary>
public static class PetMapPalette
{
    private static readonly string[] Hex = { "#4D9BFF", "#FF8A3D", "#5BD07A", "#E05BD0", "#FFE04D", "#4DDCE0", "#FF5A6E", "#B58BFF" };

    public static Color Of(int index) => (Color)ColorConverter.ConvertFromString(Hex[index % Hex.Length]);

    private static readonly Dictionary<string, Color> GatherColors = new();
    private static readonly Dictionary<Color, SolidColorBrush> FrozenBrushes = new();

    /// <summary>The colour of a kind of collectible (shown as a diamond). Looked up for every point on every map tick, so each kind is parsed once.</summary>
    public static Color OfGather(string kind)
    {
        lock (GatherColors)
        {
            if (!GatherColors.TryGetValue(kind, out Color color))
            {
                color = (Color)ColorConverter.ConvertFromString(kind switch
                {
                    "Od" => "#7FE3FF",
                    "Herb" => "#3DDC84",
                    "Food" => "#FFC24D",
                    "Ore" => "#C9CED6",
                    "Wood" => "#B5835A",
                    "Gemstone" => "#E08CFF",
                    "Cotton" => "#F3EBD3",
                    "Fragment" => "#FF7B7B",
                    "Cube" => "#FFD84D",
                    _ => "#FFFFFF",
                });
                GatherColors[kind] = color;
            }

            return color;
        }
    }

    /// <summary>A frozen brush of a colour, shared: the map assigns one to every dot every 50 ms, and a fresh unfrozen brush each time is garbage and a re-render trigger.</summary>
    public static SolidColorBrush BrushOf(Color color)
    {
        lock (FrozenBrushes)
        {
            if (!FrozenBrushes.TryGetValue(color, out SolidColorBrush? brush))
            {
                brush = new SolidColorBrush(color);
                brush.Freeze();
                FrozenBrushes[color] = brush;
            }

            return brush;
        }
    }
}

/// <summary>
/// The pet map: the game's own world map (transparent sea), centred on the player, with a dot for every spawn point of the chosen pets' monsters,
/// each pet in its own colour. No text on it. The view glides to each new position the game reports.
/// </summary>
public sealed class PetMapWindow : MovableOverlay
{
    public const double ViewSize = 440;
    private readonly Canvas _canvas = new() { Width = ViewSize, Height = ViewSize, ClipToBounds = true, Background = Brushes.Transparent };
    private readonly Dictionary<string, BitmapSource?> _tiles = new();
    private readonly List<string> _tileOrder = new();
    private readonly List<Image> _tileImages = new();
    private readonly List<Ellipse> _dots = new();
    private readonly List<Ellipse> _liveDots = new();
    private readonly List<Polygon> _gatherMarks = new();
    private readonly List<Polygon> _edgeArrows = new();
    private readonly Polygon _me;

    /// <summary>The way the player's arrow points, in degrees clockwise from straight up (the direction he moves in).</summary>
    public double MeHeading { get; set; }
    private readonly TextBlock _meUnknown;

    /// <summary>Round instead of square (to lay over the game's minimap): the view is cut to a circle and the edge arrows sit on it.</summary>
    public bool Round { get; set; }

    /// <summary>The pets that may get an arrow on the edge (null: any); on the route, those of the stop being farmed.</summary>
    public IReadOnlySet<int>? ArrowPets { get; set; }

    /// <summary>At most this many pet arrows, the nearest pets first.</summary>
    public const int ArrowLimit = 8;

    private IReadOnlyList<(double X, double Y, double Radius, int Step)> _route = Array.Empty<(double, double, double, int)>();
    private static readonly Brush RouteGold = PetMapPalette.BrushOf(Color.FromRgb(0xE8, 0xC3, 0x5A));
    private static readonly Brush RouteGoldSoft = PetMapPalette.BrushOf(Color.FromArgb(0x14, 0xE8, 0xC3, 0x5A));
    private static readonly Brush RouteDark = PetMapPalette.BrushOf(Color.FromArgb(0xE6, 0x10, 0x14, 0x1A));

    /// <summary>The pet route to draw (world units): the stop being farmed first, then the next ones. Empty: no route. <paramref name="firstName"/>: where the
    /// stop being farmed is (its area), written beside the arrow and, on the full map, beside the stop.</summary>
    public void SetRoute(IReadOnlyList<(double X, double Y, double Radius, int Step)> stops, (double X, double Y)? next = null, string? firstName = null)
    {
        _route = stops;
        _next = next;
        _firstName = firstName;
    }

    private string? _firstName;

    /// <summary>The whole map instead of the part around the player (the full map overlay): the route's stops, all of them, and where the player is on it.</summary>
    public bool Whole { get; }

    /// <summary>As many stops as the route draws: the minimap the next six, the full map up to twelve.</summary>
    public const int RouteBadges = 12;

    /// <summary>At the stop: the nearest of its pets' monsters (or spawn points), which the line leads to instead of the stop.</summary>
    private (double X, double Y)? _next;

    /// <summary>Where a ray from the middle towards (dx, dy) leaves the view, a little inside it: on the square's edge, or on the circle when round.</summary>
    private (double X, double Y) EdgeOf(double cx, double cy, double dx, double dy)
    {
        if (Round)
        {
            double r = Math.Min(cx, cy) - 13, len = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-6);
            return (cx + dx / len * r, cy + dy / len * r);
        }

        double t = Math.Min((cx - 13) / Math.Max(Math.Abs(dx), 1e-6), (cy - 13) / Math.Max(Math.Abs(dy), 1e-6));
        return (cx + dx * t, cy + dy * t);
    }

    /// <summary>
    /// The route: a solid gold line from the player to the stop being farmed (at the stop: to the nearest of its pets), a dashed one on from there through
    /// the next stops, the stop being farmed with its reach and its number in gold, the next ones numbered, and, when the stop is out of view, a gold arrow on
    /// the edge towards it with its number, its area and the distance. <paramref name="at"/> puts a world place on the canvas; <paramref name="me"/> is the player there.
    /// </summary>
    private void DrawRoute(Func<double, double, Point> at, Point me, double worldX, double worldY, double ppu, double cx, double cy, double vw, double vh)
    {
        // the parts are made once and moved on every frame (20 a second): new shapes with shadows every frame made the map stutter
        RouteParts parts = _routeShapes ??= NewRouteParts();
        foreach (UIElement part in parts.All)
        {
            part.Visibility = Visibility.Collapsed;
        }

        if (_route.Count == 0)
        {
            return;
        }

        // at the stop: a solid line to the nearest of its pets
        if (_next is { } next)
        {
            Point n = at(next.X, next.Y);
            parts.NextLine.X1 = me.X;
            parts.NextLine.Y1 = me.Y;
            parts.NextLine.X2 = n.X;
            parts.NextLine.Y2 = n.Y;
            parts.NextLine.Visibility = Visibility.Visible;
            Canvas.SetLeft(parts.NextTarget, n.X - 13);
            Canvas.SetTop(parts.NextTarget, n.Y - 13);
            parts.NextTarget.Visibility = Visibility.Visible;
        }

        bool round = Round && !Whole;
        bool Inside(double x, double y, double margin) => round
            ? (x - cx) * (x - cx) + (y - cy) * (y - cy) <= (Math.Min(cx, cy) - margin) * (Math.Min(cx, cy) - margin)
            : x >= margin && y >= margin && x <= vw - margin && y <= vh - margin;
        var shown = _route.Take(parts.Badges.Count).Select(st => (Stop: st, P: at(st.X, st.Y))).ToList();
        if (shown.Count > 1)
        {
            // the way on from the stop being farmed: dashed
            var points = new PointCollection(shown.Count);
            foreach (var (_, pt) in shown)
            {
                points.Add(pt);
            }

            parts.Path.Points = points;
            parts.Path.Visibility = Visibility.Visible;
        }

        if (_next is null)
        {
            // the way to the stop being farmed: solid, from the player
            parts.Leg.X1 = me.X;
            parts.Leg.Y1 = me.Y;
            parts.Leg.X2 = shown[0].P.X;
            parts.Leg.Y2 = shown[0].P.Y;
            parts.Leg.Visibility = Visibility.Visible;
        }

        for (int i = 0; i < shown.Count; i++)
        {
            var (stop, pt) = shown[i];
            if (!Inside(pt.X, pt.Y, 6))
            {
                continue;
            }

            double reach = Math.Max(12, stop.Radius * 100 * ppu);
            if (i == 0 && reach < Math.Min(cx, cy) * 0.8)
            {
                // the stop's reach, while it fits in the view (zoomed out: the number alone)
                parts.Ring.Width = parts.Ring.Height = 2 * reach;
                Canvas.SetLeft(parts.Ring, pt.X - reach);
                Canvas.SetTop(parts.Ring, pt.Y - reach);
                parts.Ring.Visibility = Visibility.Visible;
            }

            Border badge = parts.Badges[i];
            ((TextBlock)badge.Child).Text = stop.Step.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Canvas.SetLeft(badge, pt.X - badge.Width / 2);
            Canvas.SetTop(badge, pt.Y - badge.Height / 2);
            badge.Visibility = Visibility.Visible;
        }

        var (first, fp) = shown[0];
        double metres = Math.Sqrt((first.X - worldX) * (first.X - worldX) + (first.Y - worldY) * (first.Y - worldY)) / 100.0;
        string name = _firstName is { Length: > 0 } n0 ? (n0.Length > 22 ? n0[..21] + "\u2026" : n0) : "";
        if (!Inside(fp.X, fp.Y, 6))
        {
            // the stop being farmed out of view: a gold arrow on the edge, its number, where it is and how far
            double dx = fp.X - me.X, dy = fp.Y - me.Y, angle = Math.Atan2(dy, dx);
            var (tipX, tipY) = EdgeOf(cx, cy, dx, dy);
            parts.Arrow.RenderTransform = new RotateTransform(angle * 180 / Math.PI);
            Canvas.SetLeft(parts.Arrow, tipX);
            Canvas.SetTop(parts.Arrow, tipY);
            parts.Arrow.Visibility = Visibility.Visible;
            parts.LabelText.Text = name.Length > 0
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} · {1} · {2:0} m", first.Step, name, metres)
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} · {1:0} m", first.Step, metres);
            parts.Label.Visibility = Visibility.Visible;
            parts.Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size sz = parts.Label.DesiredSize;
            Canvas.SetLeft(parts.Label, Math.Clamp(tipX - 30 * Math.Cos(angle) - sz.Width / 2, 3, Math.Max(3, vw - sz.Width - 3)));
            Canvas.SetTop(parts.Label, Math.Clamp(tipY - 30 * Math.Sin(angle) - sz.Height / 2, 3, Math.Max(3, vh - sz.Height - 3)));
        }
        else if (Whole && _next is null)
        {
            // on the full map: the stop's area and how far, beside it
            parts.LabelText.Text = name.Length > 0
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} · {1:0} m", name, metres)
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:0} m", metres);
            parts.Label.Visibility = Visibility.Visible;
            parts.Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size sz = parts.Label.DesiredSize;
            bool left = fp.X + 16 + sz.Width > vw - 3;
            Canvas.SetLeft(parts.Label, Math.Clamp(left ? fp.X - 16 - sz.Width : fp.X + 16, 3, Math.Max(3, vw - sz.Width - 3)));
            Canvas.SetTop(parts.Label, Math.Clamp(fp.Y - sz.Height / 2, 3, Math.Max(3, vh - sz.Height - 3)));
        }
    }

    private sealed class RouteParts
    {
        public required Polyline Path { get; init; }
        public required Ellipse Ring { get; init; }
        public required List<Border> Badges { get; init; }
        public required Polygon Arrow { get; init; }
        public required Border Label { get; init; }
        public required TextBlock LabelText { get; init; }
        public required Line NextLine { get; init; }
        public required Ellipse NextTarget { get; init; }
        public required Line Leg { get; init; }

        public IEnumerable<UIElement> All => new UIElement[] { Path, Ring, Arrow, Label, NextLine, NextTarget, Leg }.Concat(Badges);
    }

    private RouteParts? _routeShapes;

    private RouteParts NewRouteParts()
    {
        var shadow = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.9 };
        shadow.Freeze();
        var labelText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = RouteGold };
        var parts = new RouteParts
        {
            Path = new Polyline { Stroke = RouteGold, StrokeThickness = 2.2, Opacity = 0.75, StrokeDashArray = new DoubleCollection { 3, 2.5 }, IsHitTestVisible = false },
            Ring = new Ellipse { Stroke = RouteGold, StrokeThickness = 1.6, StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = RouteGoldSoft, IsHitTestVisible = false },
            Badges = Enumerable.Range(0, RouteBadges).Select(i =>
            {
                double size = i == 0 ? 22 : 17;
                return new Border
                {
                    Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = i == 0 ? RouteGold : RouteDark, BorderBrush = RouteGold, BorderThickness = new Thickness(1.6),
                    IsHitTestVisible = false, Effect = shadow,
                    Child = new TextBlock { FontSize = i == 0 ? 12 : 10.5, FontWeight = FontWeights.Bold, Foreground = i == 0 ? RouteDark : RouteGold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
            }).ToList(),
            Arrow = new Polygon
            {
                Points = new PointCollection { new Point(13, 0), new Point(-9, -9), new Point(-4, 0), new Point(-9, 9) }, Fill = RouteGold, Stroke = RouteDark, StrokeThickness = 1.4,
                IsHitTestVisible = false, Effect = shadow,
            },
            Label = new Border { Background = RouteDark, BorderBrush = RouteGold, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1), IsHitTestVisible = false, Child = labelText },
            LabelText = labelText,
            NextLine = new Line { Stroke = RouteGold, StrokeThickness = 2.6, IsHitTestVisible = false, Effect = shadow },
            NextTarget = new Ellipse { Width = 26, Height = 26, Stroke = RouteGold, StrokeThickness = 2.4, IsHitTestVisible = false },
            Leg = new Line { Stroke = RouteGold, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false, Effect = shadow },
        };
        void Add(UIElement e, int z)
        {
            Panel.SetZIndex(e, z);
            e.Visibility = Visibility.Collapsed;
            _canvas.Children.Add(e);
        }

        Add(parts.Ring, 2);
        Add(parts.Path, 3);
        Add(parts.Leg, 4);
        Add(parts.NextLine, 4);
        foreach (Border badge in Enumerable.Reverse(parts.Badges))
        {
            Add(badge, 7);
        }

        Add(parts.Arrow, 8);
        Add(parts.Label, 8);
        Add(parts.NextTarget, 9);
        return parts;
    }

    public PetMapWindow(bool whole = false) : base(whole ? "Aion DPS Full Map" : "Aion DPS Pet Map", new Border { Child = null }, whole ? LoadWhole : Load, whole ? SaveWhole : Save,
        whole ? Math.Max(0, SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - ViewSize * 1.4) / 2) : SystemParameters.WorkArea.Right - ViewSize - 40,
        whole ? Math.Max(0, SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - ViewSize * 1.4) / 2) : 120,
        whole ? LoadWholeSize : LoadSize, whole ? SaveWholeSize : SaveSize, OverlayEdgeMode.CropAnchored)
    {
        Whole = whole;
        _me = new Polygon
        {
            Points = new PointCollection { new Point(0, -11), new Point(8, 9), new Point(0, 5), new Point(-8, 9) },
            Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42)), Stroke = Brushes.Black, StrokeThickness = 1,
        };
        Canvas.SetLeft(_me, ViewSize / 2);
        Canvas.SetTop(_me, ViewSize / 2);
        _canvas.Children.Add(_me);
        _meUnknown = new TextBlock
        {
            Text = "?", Width = 28, TextAlignment = TextAlignment.Center, FontSize = 26, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xBA, 0x42)),
            IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Color = Colors.Black, Opacity = 1 },
        };
        Canvas.SetLeft(_meUnknown, ViewSize / 2 - 14);
        Canvas.SetTop(_meUnknown, ViewSize / 2 - 20);
        _canvas.Children.Add(_meUnknown);
        Body = _canvas;
    }

    private static (double?, double?) LoadWholeSize()
    {
        var s = MeterSettings.Load();
        return (s.PetWholeWidth, s.PetWholeHeight);
    }

    private static void SaveWholeSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.PetWholeWidth = width;
        s.PetWholeHeight = height;
        s.Save();
    }

    private static (double?, double?, double) LoadWhole()
    {
        var s = MeterSettings.Load();
        return (s.PetWholeLeft, s.PetWholeTop, s.PetWholeScale);
    }

    private static void SaveWhole(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.PetWholeLeft = left;
        s.PetWholeTop = top;
        s.PetWholeScale = scale;
        s.Save();
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.PetMapWidth, s.PetMapHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.PetMapWidth = width;
        s.PetMapHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.PetMapLeft, s.PetMapTop, s.PetMapScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.PetMapLeft = left;
        s.PetMapTop = top;
        s.PetMapScale = scale;
        s.Save();
    }

    /// <summary>How many tile pictures the last render put on the map (for tests).</summary>
    public int TilesShown => _tileImages.Count(i => i.Visibility == Visibility.Visible);

    public string TileDebug => string.Join(" ", _tiles.Select(kv => kv.Key + (kv.Value is null ? "=null" : "=ok")));

    private BitmapSource? Tile(Aion2MapInfo map, int col, int row)
    {
        string key = $"{map.Key}/{col}_{row}";
        if (_tiles.TryGetValue(key, out var cached))
        {
            return cached;
        }

        BitmapSource? bitmap = MapTiles.Load(map, col, row, Math.Min(map.Tile, 1536));
        _tiles[key] = bitmap;
        _tileOrder.Add(key);
        while (_tileOrder.Count > 8)
        {
            _tiles.Remove(_tileOrder[0]);
            _tileOrder.RemoveAt(0);
        }

        return bitmap;
    }

    /// <summary>
    /// Draws the map around (<paramref name="worldX"/>, <paramref name="worldY"/>): <paramref name="radiusMetres"/> to each side, the dots of
    /// <paramref name="points"/> in their pets' colours (<paramref name="colors"/> by pet id).
    /// </summary>
    public void Render(Aion2MapInfo map, double worldX, double worldY, double radiusMetres, double opacity,
        IReadOnlyList<(int PetId, float X, float Y)> points, IReadOnlyList<(int PetId, float X, float Y)> live, IReadOnlyDictionary<int, Color> colors,
        IReadOnlyList<(string Kind, float X, float Y)>? gather = null, bool positionKnown = true, bool petArrows = true, bool gatherArrows = true,
        IReadOnlyList<(float X, float Y)>? guards = null)
    {
        // the visible part of the canvas (the frame may be pulled smaller than the view): the player stays in the middle of it
        double vw = Math.Clamp(BodyWidth, 40, ViewSize), vh = Math.Clamp(BodyHeight, 40, ViewSize);
        double cx = vw / 2, cy = vh / 2;
        double ppu = ViewSize / 2 / (radiusMetres * 100.0);       // view pixels per world unit
        double k = ppu / map.Scale;                                // view pixels per native map pixel
        var (nx, ny) = Aion2Maps.ToNative(map, worldX, worldY);
        double halfNative = ViewSize / 2 / k; // generous: the tiles beyond the visible part are clipped
        int c0 = Math.Max(0, (int)Math.Floor((nx - halfNative) / map.Tile)), c1 = Math.Min(map.Grid - 1, (int)Math.Floor((nx + halfNative) / map.Tile));
        int r0 = Math.Max(0, (int)Math.Floor((ny - halfNative) / map.Tile)), r1 = Math.Min(map.Grid - 1, (int)Math.Floor((ny + halfNative) / map.Tile));

        int used = 0;
        for (int col = c0; col <= c1; col++)
        {
            for (int row = r0; row <= r1; row++)
            {
                var source = Tile(map, col, row);
                if (source is null)
                {
                    continue;
                }

                if (used >= _tileImages.Count)
                {
                    var image = new Image { Stretch = Stretch.Fill, SnapsToDevicePixels = false };
                    Panel.SetZIndex(image, -2); // under the danger zones of the guards and everything else
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                    _tileImages.Add(image);
                    _canvas.Children.Insert(used, image);
                }

                var img = _tileImages[used++];
                img.Source = source;
                img.Opacity = opacity;
                img.Width = map.Tile * k;
                img.Height = map.Tile * k;
                Canvas.SetLeft(img, cx + (col * map.Tile - nx) * k);
                Canvas.SetTop(img, cy + (row * map.Tile - ny) * k);
                img.Visibility = Visibility.Visible;
            }
        }

        for (int i = used; i < _tileImages.Count; i++)
        {
            _tileImages[i].Visibility = Visibility.Collapsed;
        }

        int dot = 0;
        foreach ((int pet, float x, float y) in points)
        {
            double px = cx + (x - worldX) * ppu, py = cy + (y - worldY) * ppu;
            if (px < 5 || py < 5 || px > vw - 5 || py > vh - 5)
            {
                continue;
            }

            if (dot >= _dots.Count)
            {
                var e = new Ellipse { Width = 9, Height = 9, Stroke = Brushes.White, StrokeThickness = 1.2, Opacity = 0.6, IsHitTestVisible = false };
                _dots.Add(e);
                _canvas.Children.Add(e);
            }

            var d = _dots[dot++];
            d.Fill = PetMapPalette.BrushOf(colors.GetValueOrDefault(pet, Colors.White));
            Canvas.SetLeft(d, px - 4.5);
            Canvas.SetTop(d, py - 4.5);
            d.Visibility = Visibility.Visible;
        }

        for (int i = dot; i < _dots.Count; i++)
        {
            _dots[i].Visibility = Visibility.Collapsed;
        }

        // collectibles: diamonds in the colour of their kind (the pets are round)
        int mark = 0;
        foreach ((string kind, float x, float y) in gather ?? Array.Empty<(string, float, float)>())
        {
            double px = cx + (x - worldX) * ppu, py = cy + (y - worldY) * ppu;
            if (px < 6 || py < 6 || px > vw - 6 || py > vh - 6)
            {
                continue;
            }

            if (mark >= _gatherMarks.Count)
            {
                var g = new Polygon { Points = new PointCollection { new Point(0, -8), new Point(7, 0), new Point(0, 8), new Point(-7, 0) }, Stroke = Brushes.Black, StrokeThickness = 1.4, IsHitTestVisible = false };
                _gatherMarks.Add(g);
                _canvas.Children.Add(g);
            }

            var m = _gatherMarks[mark++];
            m.Fill = PetMapPalette.BrushOf(PetMapPalette.OfGather(kind));
            Canvas.SetLeft(m, px);
            Canvas.SetTop(m, py);
            m.Visibility = Visibility.Visible;
        }

        for (int i = mark; i < _gatherMarks.Count; i++)
        {
            _gatherMarks[i].Visibility = Visibility.Collapsed;
        }

        // the monsters really around the player (announced by the game): large, bright, over the general spawn points
        int liveDot = 0;
        foreach ((int pet, float x, float y) in live)
        {
            double px = cx + (x - worldX) * ppu, py = cy + (y - worldY) * ppu;
            if (px < 8 || py < 8 || px > vw - 8 || py > vh - 8)
            {
                continue;
            }

            if (liveDot >= _liveDots.Count)
            {
                var e = new Ellipse { Width = 17, Height = 17, Stroke = Brushes.White, StrokeThickness = 2.5, IsHitTestVisible = false, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 5, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.9 } };
                _liveDots.Add(e);
                _canvas.Children.Add(e);
            }

            var d = _liveDots[liveDot++];
            d.Fill = PetMapPalette.BrushOf(colors.GetValueOrDefault(pet, Colors.White));
            Canvas.SetLeft(d, px - 8.5);
            Canvas.SetTop(d, py - 8.5);
            d.Visibility = Visibility.Visible;
        }

        for (int i = liveDot; i < _liveDots.Count; i++)
        {
            _liveDots[i].Visibility = Visibility.Collapsed;
        }

        // a chosen pet with nothing in view: an arrow on the edge, in its colour, towards its nearest spawn point
        int arrow = 0;
        double halfX = cx, halfY = cy;
        // one pass over all points (not one pass per pet): per pet whether one is in view, and the nearest one out of view
        var seen = new Dictionary<int, (bool Visible, double BestSq, double Bx, double By)>();
        void Consider(int id, float x, float y)
        {
            if (!petArrows || !colors.ContainsKey(id))
            {
                return;
            }

            if (!seen.TryGetValue(id, out var st))
            {
                st = (false, double.MaxValue, 0, 0);
            }
            else if (st.Visible)
            {
                return; // one in view is enough: no arrow for this pet
            }

            double dx = (x - worldX) * ppu, dy = (y - worldY) * ppu;
            if (Math.Abs(dx) < halfX - 5 && Math.Abs(dy) < halfY - 5)
            {
                st.Visible = true;
            }
            else
            {
                double sq = dx * dx + dy * dy;
                if (sq < st.BestSq)
                {
                    st.BestSq = sq;
                    st.Bx = dx;
                    st.By = dy;
                }
            }

            seen[id] = st;
        }

        foreach (var (id, x, y) in points)
        {
            Consider(id, x, y);
        }

        foreach (var (id, x, y) in live)
        {
            Consider(id, x, y);
        }

        void PlaceArrow(Color color, double bx, double by)
        {
            var (edgeX, edgeY) = EdgeOf(halfX, halfY, bx, by); // where the ray to the point meets the edge (the square, or the circle when round)
            if (arrow >= _edgeArrows.Count)
            {
                var a = new Polygon { Points = new PointCollection { new Point(11, 0), new Point(-8, -8), new Point(-4, 0), new Point(-8, 8) }, Stroke = Brushes.White, StrokeThickness = 1.6, IsHitTestVisible = false, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.9 } };
                _edgeArrows.Add(a);
                _canvas.Children.Add(a);
            }

            var ar = _edgeArrows[arrow++];
            ar.Fill = PetMapPalette.BrushOf(color);
            ar.RenderTransform = new RotateTransform(Math.Atan2(by, bx) * 180 / Math.PI);
            Canvas.SetLeft(ar, edgeX);
            Canvas.SetTop(ar, edgeY);
            ar.Visibility = Visibility.Visible;
        }

        // only the nearest few pets get an arrow (with every pet still needed on the map there would be dozens), on the route those of the stop being farmed
        var arrows = new List<(Color Color, double Sq, double Bx, double By)>();
        foreach (var (pet, color) in colors)
        {
            if (!seen.TryGetValue(pet, out var st) || (ArrowPets is { } only && !only.Contains(pet)))
            {
                continue;
            }

            if (st.Visible || st.BestSq == double.MaxValue)
            {
                continue;
            }

            arrows.Add((color, st.BestSq, st.Bx, st.By));
        }

        foreach (var (color, _, bx, by) in arrows.OrderBy(a => a.Sq).Take(ArrowLimit))
        {
            PlaceArrow(color, bx, by);
        }

        // gatherables: one arrow per kind with none in view, towards its nearest point
        if (gatherArrows && gather is not null)
        {
            var kinds = new Dictionary<string, (bool Visible, double BestSq, double Bx, double By)>();
            foreach ((string kind, float x, float y) in gather)
            {
                kinds.TryGetValue(kind, out var st);
                if (st == default)
                {
                    st = (false, double.MaxValue, 0, 0);
                }

                if (st.Visible)
                {
                    continue;
                }

                double dx = (x - worldX) * ppu, dy = (y - worldY) * ppu;
                if (Math.Abs(dx) < halfX - 5 && Math.Abs(dy) < halfY - 5)
                {
                    st.Visible = true;
                }
                else if (dx * dx + dy * dy < st.BestSq)
                {
                    st = (false, dx * dx + dy * dy, dx, dy);
                }

                kinds[kind] = st;
            }

            foreach (var (kind, st) in kinds)
            {
                if (!st.Visible && st.BestSq != double.MaxValue)
                {
                    PlaceArrow(PetMapPalette.OfGather(kind), st.Bx, st.By);
                }
            }
        }

        for (int i = arrow; i < _edgeArrows.Count; i++)
        {
            _edgeArrows[i].Visibility = Visibility.Collapsed;
        }

        DrawGuards(guards, worldX, worldY, ppu, cx, cy, vw, vh);
        DrawRoute((x, y) => new Point(cx + (x - worldX) * ppu, cy + (y - worldY) * ppu), new Point(cx, cy), worldX, worldY, ppu, cx, cy, vw, vh);
        if (_wholeImage is not null)
        {
            _wholeImage.Visibility = Visibility.Collapsed;
        }

        if (_halo is not null)
        {
            _halo.Visibility = Visibility.Collapsed;
        }
        // round: the view is a circle (the minimap's shape)
        _canvas.Clip = Round ? new EllipseGeometry(new Point(cx, cy), cx, cy) : null;

        Canvas.SetLeft(_me, cx);
        Canvas.SetTop(_me, cy);
        _me.RenderTransform = new RotateTransform(MeHeading);
        Canvas.SetLeft(_meUnknown, cx - 14);
        Canvas.SetTop(_meUnknown, cy - 20);
        // the player on top; a "?" instead of the arrow when the position is not known (the view then shows the last one)
        _me.Visibility = positionKnown ? Visibility.Visible : Visibility.Collapsed;
        _meUnknown.Visibility = positionKnown ? Visibility.Collapsed : Visibility.Visible;
        _canvas.Children.Remove(_me);
        _canvas.Children.Add(_me);
        _canvas.Children.Remove(_meUnknown);
        _canvas.Children.Add(_meUnknown);
    }

    private Image? _wholeImage;
    private Ellipse? _halo;
    private Border? _wholeTitle;

    /// <summary>The land picture of each map and where it lies (world units), made once per map (every tile at 192 pixels).</summary>
    private static readonly Dictionary<string, (BitmapSource Image, double X0, double Y0, double W, double H)?> Lands = new();

    private static (BitmapSource Image, double X0, double Y0, double W, double H)? LandOf(Aion2MapInfo map)
    {
        if (!Lands.TryGetValue(map.Key, out var land))
        {
            land = MapTiles.Land(map, 192) is { } l
                ? (l.Image, (l.Col0 * map.Tile - map.OffsetX) / map.Scale, (l.Row0 * map.Tile - map.OffsetY) / map.Scale, l.Cols * map.Tile / map.Scale, l.Rows * map.Tile / map.Scale)
                : null;
            Lands[map.Key] = land;
        }

        return land;
    }

    /// <summary>
    /// The full map: the whole land of <paramref name="map"/> in the view, the player's arrow where he is (<paramref name="worldX"/>, <paramref name="worldY"/>),
    /// the spawn points of the chosen pets small, the monsters and collectibles around him, and the whole route with every stop numbered.
    /// </summary>
    public void RenderWhole(Aion2MapInfo map, double worldX, double worldY, IReadOnlyList<(int PetId, float X, float Y)> points,
        IReadOnlyList<(int PetId, float X, float Y)> live, IReadOnlyDictionary<int, Color> colors, IReadOnlyList<(string Kind, float X, float Y)>? gather, bool positionKnown)
    {
        double vw = Math.Clamp(BodyWidth, 40, ViewSize), vh = Math.Clamp(BodyHeight, 40, ViewSize);
        var land = LandOf(map);
        // without tiles the map's spawn points still give its extent
        (double X0, double Y0, double W, double H) r = land is { } l0 ? (l0.X0, l0.Y0, l0.W, l0.H)
            : points.Count > 0 ? (points.Min(p => p.X), points.Min(p => p.Y), Math.Max(1000, points.Max(p => p.X) - points.Min(p => p.X)), Math.Max(1000, points.Max(p => p.Y) - points.Min(p => p.Y)))
            : (worldX - 50000, worldY - 50000, 100000, 100000);
        double ppu = Math.Min((vw - 10) / r.W, (vh - 10) / r.H);
        double ox = vw / 2 - (r.X0 + r.W / 2) * ppu, oy = vh / 2 - (r.Y0 + r.H / 2) * ppu;
        Point At(double x, double y) => new(ox + x * ppu, oy + y * ppu);

        foreach (Image tile in _tileImages)
        {
            tile.Visibility = Visibility.Collapsed;
        }

        if (_wholeImage is null)
        {
            _wholeImage = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
            Panel.SetZIndex(_wholeImage, -2);
            RenderOptions.SetBitmapScalingMode(_wholeImage, BitmapScalingMode.HighQuality);
            _canvas.Children.Insert(0, _wholeImage);
        }

        if (land is { } l)
        {
            _wholeImage.Source = l.Image;
            _wholeImage.Width = r.W * ppu;
            _wholeImage.Height = r.H * ppu;
            Canvas.SetLeft(_wholeImage, ox + r.X0 * ppu);
            Canvas.SetTop(_wholeImage, oy + r.Y0 * ppu);
            _wholeImage.Visibility = Visibility.Visible;
        }
        else
        {
            _wholeImage.Visibility = Visibility.Collapsed;
        }

        // the spawn points of the chosen pets: small
        int dot = 0;
        foreach ((int pet, float x, float y) in points)
        {
            Point pt = At(x, y);
            if (pt.X < 3 || pt.Y < 3 || pt.X > vw - 3 || pt.Y > vh - 3)
            {
                continue;
            }

            if (dot >= _dots.Count)
            {
                var e = new Ellipse { Stroke = Brushes.White, StrokeThickness = 0.8, Opacity = 0.75, IsHitTestVisible = false };
                _dots.Add(e);
                _canvas.Children.Add(e);
            }

            var d = _dots[dot++];
            d.Width = d.Height = 5;
            d.Fill = PetMapPalette.BrushOf(colors.GetValueOrDefault(pet, Colors.White));
            Canvas.SetLeft(d, pt.X - 2.5);
            Canvas.SetTop(d, pt.Y - 2.5);
            d.Visibility = Visibility.Visible;
        }

        for (int i = dot; i < _dots.Count; i++)
        {
            _dots[i].Visibility = Visibility.Collapsed;
        }

        int mark = 0;
        foreach ((string kind, float x, float y) in gather ?? Array.Empty<(string, float, float)>())
        {
            Point pt = At(x, y);
            if (pt.X < 4 || pt.Y < 4 || pt.X > vw - 4 || pt.Y > vh - 4)
            {
                continue;
            }

            if (mark >= _gatherMarks.Count)
            {
                var g = new Polygon { Points = new PointCollection { new Point(0, -5), new Point(4.5, 0), new Point(0, 5), new Point(-4.5, 0) }, Stroke = Brushes.Black, StrokeThickness = 1, IsHitTestVisible = false };
                _gatherMarks.Add(g);
                _canvas.Children.Add(g);
            }

            var m = _gatherMarks[mark++];
            m.Fill = PetMapPalette.BrushOf(PetMapPalette.OfGather(kind));
            Canvas.SetLeft(m, pt.X);
            Canvas.SetTop(m, pt.Y);
            m.Visibility = Visibility.Visible;
        }

        for (int i = mark; i < _gatherMarks.Count; i++)
        {
            _gatherMarks[i].Visibility = Visibility.Collapsed;
        }

        int liveDot = 0;
        foreach ((int pet, float x, float y) in live)
        {
            Point pt = At(x, y);
            if (liveDot >= _liveDots.Count)
            {
                var e = new Ellipse { Width = 9, Height = 9, Stroke = Brushes.White, StrokeThickness = 1.6, IsHitTestVisible = false };
                _liveDots.Add(e);
                _canvas.Children.Add(e);
            }

            var d = _liveDots[liveDot++];
            d.Fill = PetMapPalette.BrushOf(colors.GetValueOrDefault(pet, Colors.White));
            Canvas.SetLeft(d, pt.X - 4.5);
            Canvas.SetTop(d, pt.Y - 4.5);
            d.Visibility = Visibility.Visible;
        }

        for (int i = liveDot; i < _liveDots.Count; i++)
        {
            _liveDots[i].Visibility = Visibility.Collapsed;
        }

        // everything is in view: no arrows on the edge, no guards (they are only near the player)
        foreach (Polygon a in _edgeArrows)
        {
            a.Visibility = Visibility.Collapsed;
        }

        DrawGuards(null, worldX, worldY, ppu, vw / 2, vh / 2, vw, vh);
        Point me = At(worldX, worldY);
        DrawRoute((x, y) => At(x, y), me, worldX, worldY, ppu, vw / 2, vh / 2, vw, vh);
        _canvas.Clip = null;

        // the map's name in the corner
        if (_wholeTitle is null)
        {
            _wholeTitle = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x10, 0x14, 0x1A)), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), IsHitTestVisible = false,
                Child = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = RouteGold },
            };
            Panel.SetZIndex(_wholeTitle, 9);
            _canvas.Children.Add(_wholeTitle);
            Canvas.SetLeft(_wholeTitle, 6);
            Canvas.SetTop(_wholeTitle, 6);
        }

        ((TextBlock)_wholeTitle.Child).Text = map.Title;
        _wholeTitle.Visibility = Visibility.Visible;

        // the player: his arrow in a white ring, so he is found at a glance on the whole map
        if (_halo is null)
        {
            _halo = new Ellipse { Width = 30, Height = 30, Stroke = Brushes.White, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xBA, 0x42)), IsHitTestVisible = false };
            _canvas.Children.Add(_halo);
        }

        Canvas.SetLeft(_halo, me.X - 15);
        Canvas.SetTop(_halo, me.Y - 15);
        _halo.Visibility = positionKnown ? Visibility.Visible : Visibility.Collapsed;
        _canvas.Children.Remove(_halo);
        _canvas.Children.Add(_halo);
        Canvas.SetLeft(_me, me.X);
        Canvas.SetTop(_me, me.Y);
        _me.RenderTransform = new RotateTransform(MeHeading);
        Canvas.SetLeft(_meUnknown, me.X - 14);
        Canvas.SetTop(_meUnknown, me.Y - 20);
        _me.Visibility = positionKnown ? Visibility.Visible : Visibility.Collapsed;
        _meUnknown.Visibility = positionKnown ? Visibility.Collapsed : Visibility.Visible;
        _canvas.Children.Remove(_me);
        _canvas.Children.Add(_me);
        _canvas.Children.Remove(_meUnknown);
        _canvas.Children.Add(_meUnknown);
    }

    private sealed class GuardMark
    {
        public Ellipse Zone = null!, Badge = null!;
        public TextBlock Skull = null!, Label = null!;
        public Polygon Arrow = null!;
        public Border LabelBack = null!;

        public void Hide()
        {
            Zone.Visibility = Badge.Visibility = Skull.Visibility = Arrow.Visibility = LabelBack.Visibility = Visibility.Collapsed;
        }
    }

    private readonly List<GuardMark> _guardMarks = new();
    private static readonly Brush GuardRed = new SolidColorBrush(Color.FromRgb(0xE5, 0x1F, 0x1F));
    private static readonly Brush GuardZoneFill = new SolidColorBrush(Color.FromArgb(0x48, 0xFF, 0x26, 0x26));
    private static readonly Brush GuardZoneEdge = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0x3B, 0x3B));
    private static readonly Brush GuardLabelBack = new SolidColorBrush(Color.FromArgb(0xB8, 0x10, 0x10, 0x14));
    private static readonly FontFamily SymbolFont = new("Segoe UI Symbol");

    private GuardMark NewGuardMark()
    {
        var effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.9 };
        var mark = new GuardMark
        {
            Zone = new Ellipse { Fill = GuardZoneFill, Stroke = GuardZoneEdge, StrokeThickness = 1.2, IsHitTestVisible = false },
            Badge = new Ellipse { Width = 19, Height = 19, Fill = GuardRed, Stroke = Brushes.White, StrokeThickness = 1.8, IsHitTestVisible = false, Effect = effect },
            Skull = new TextBlock { Text = "\u2620", FontFamily = SymbolFont, FontWeight = FontWeights.Bold, FontSize = 12, Foreground = Brushes.White, Width = 19, TextAlignment = TextAlignment.Center, IsHitTestVisible = false },
            Arrow = new Polygon { Points = new PointCollection { new Point(11, 0), new Point(-8, -8), new Point(-4, 0), new Point(-8, 8) }, Fill = GuardRed, Stroke = Brushes.White, StrokeThickness = 1.6, IsHitTestVisible = false, Effect = effect },
            Label = new TextBlock { FontFamily = SymbolFont, FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = GuardRed, IsHitTestVisible = false },
        };
        mark.LabelBack = new Border { Background = GuardLabelBack, CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 1, 4, 1), Child = mark.Label, IsHitTestVisible = false };
        Panel.SetZIndex(mark.Zone, -1);
        foreach (UIElement e in new UIElement[] { mark.Badge, mark.Skull, mark.Arrow, mark.LabelBack })
        {
            Panel.SetZIndex(e, 6);
        }

        foreach (UIElement e in new UIElement[] { mark.Zone, mark.Badge, mark.Skull, mark.Arrow, mark.LabelBack })
        {
            _canvas.Children.Add(e);
        }

        return mark;
    }

    /// <summary>
    /// The militia guards the game announced around the player (Stacy's idea, her fork 17 October 2026): in view a see-through red danger zone of 20 m under a red badge with a white skull,
    /// out of view a red arrow on the edge towards it with the distance beside it.
    /// </summary>
    private void DrawGuards(IReadOnlyList<(float X, float Y)>? guards, double worldX, double worldY, double ppu, double cx, double cy, double vw, double vh)
    {
        int used = 0;
        foreach ((float gx, float gy) in guards ?? Array.Empty<(float, float)>())
        {
            GuardMark mark = used < _guardMarks.Count ? _guardMarks[used] : NewGuardMark();
            if (used >= _guardMarks.Count)
            {
                _guardMarks.Add(mark);
            }

            used++;
            double dx = (gx - worldX) * ppu, dy = (gy - worldY) * ppu;
            double px = cx + dx, py = cy + dy;
            if (px >= 8 && py >= 8 && px <= vw - 8 && py <= vh - 8)
            {
                double zone = Math.Max(14, Aion2Guards.ZoneMetres * 100 * ppu);
                mark.Zone.Width = mark.Zone.Height = zone * 2;
                Canvas.SetLeft(mark.Zone, px - zone);
                Canvas.SetTop(mark.Zone, py - zone);
                Canvas.SetLeft(mark.Badge, px - 9.5);
                Canvas.SetTop(mark.Badge, py - 9.5);
                Canvas.SetLeft(mark.Skull, px - 9.5);
                Canvas.SetTop(mark.Skull, py - 9);
                mark.Zone.Visibility = mark.Badge.Visibility = mark.Skull.Visibility = Visibility.Visible;
                mark.Arrow.Visibility = mark.LabelBack.Visibility = Visibility.Collapsed;
                continue;
            }

            // out of view: the arrow on the edge, the distance beside it (towards the middle)
            double angle = Math.Atan2(dy, dx);
            var (tipX, tipY) = EdgeOf(cx, cy, dx, dy);
            mark.Arrow.RenderTransform = new RotateTransform(angle * 180 / Math.PI);
            Canvas.SetLeft(mark.Arrow, tipX);
            Canvas.SetTop(mark.Arrow, tipY);
            double metres = Math.Sqrt((gx - worldX) * (double)(gx - worldX) + (gy - worldY) * (double)(gy - worldY)) / 100.0;
            mark.Label.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, "\u2620 {0:0} m", metres);
            mark.LabelBack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size size = mark.LabelBack.DesiredSize;
            double lx = Math.Clamp(tipX - 28 * Math.Cos(angle) - size.Width / 2, 3, Math.Max(3, vw - size.Width - 3));
            double ly = Math.Clamp(tipY - 28 * Math.Sin(angle) - size.Height / 2, 3, Math.Max(3, vh - size.Height - 3));
            Canvas.SetLeft(mark.LabelBack, lx);
            Canvas.SetTop(mark.LabelBack, ly);
            mark.Zone.Visibility = mark.Badge.Visibility = mark.Skull.Visibility = Visibility.Collapsed;
            mark.Arrow.Visibility = mark.LabelBack.Visibility = Visibility.Visible;
        }

        for (int i = used; i < _guardMarks.Count; i++)
        {
            _guardMarks[i].Hide();
        }
    }
}

/// <summary>The list that belongs to the pet map: the chosen pets with their colour, name, level (or "new") and the distance to the nearest spawn point.</summary>
public sealed class PetListWindow : MovableOverlay
{
    private readonly StackPanel _rows = new() { MinWidth = 250, MinHeight = 60 };
    private string _shown = "";

    /// <summary>How many rows the player can see at most (a row is about 19 units high), plus a reserve: more are not worth building.</summary>
    public int RowCapacity => (int)Math.Ceiling(HeightLimit / 19.0) + 10;

    private readonly TextBlock _headTitle = new() { FontWeight = FontWeights.Bold, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC3, 0x5A)), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _headDetail = new() { FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0x9D, 0xB3, 0xC2)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
    private readonly Border _head = new() { Visibility = Visibility.Collapsed, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 5), Margin = new Thickness(0, 0, 0, 4), MaxWidth = 380 };

    public PetListWindow() : base("Aion DPS Pet List", new Border { Child = null }, Load, Save, SystemParameters.WorkArea.Right - 300, 580, LoadSize, SaveSize)
    {
        _head.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xE8, 0xC3, 0x5A));
        var head = new StackPanel();
        head.Children.Add(_headTitle);
        head.Children.Add(_headDetail);
        _head.Child = head;
        var stack = new StackPanel();
        stack.Children.Add(_head);
        stack.Children.Add(_rows);
        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x0E, 0x18, 0x20)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x29, 0x45, 0x57)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Child = stack,
        };
        Body = panel;
    }

    /// <summary>The route's head over the rows: the stop being farmed and how far (gold), what comes after; null: no head.</summary>
    public void RenderHead(string? title, string? detail)
    {
        _head.Visibility = title is null ? Visibility.Collapsed : Visibility.Visible;
        _headTitle.Text = title ?? "";
        _headDetail.Text = detail ?? "";
        _headDetail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static (double?, double?) LoadSize()
    {
        var s = MeterSettings.Load();
        return (s.PetListWidth, s.PetListHeight);
    }

    private static void SaveSize(double? width, double? height)
    {
        var s = MeterSettings.Load();
        s.PetListWidth = width;
        s.PetListHeight = height;
        s.Save();
    }

    private static (double?, double?, double) Load()
    {
        var s = MeterSettings.Load();
        return (s.PetListLeft, s.PetListTop, s.PetListScale);
    }

    private static void Save(double left, double top, double scale)
    {
        var s = MeterSettings.Load();
        s.PetListLeft = left;
        s.PetListTop = top;
        s.PetListScale = scale;
        s.Save();
    }

    /// <summary>Rows: colour, name, level text, distance text (empty when unknown).</summary>
    public void Render(IReadOnlyList<(Color Color, string Name, string Level, string Distance, bool Diamond)> rows)
    {
        string key = string.Join("\n", rows.Select(r => $"{r.Color}|{r.Name}|{r.Level}|{r.Distance}|{r.Diamond}"));
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        _rows.Children.Clear();
        foreach (var (color, name, level, distance, diamond) in rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Shape dot = diamond
                ? new Polygon { Points = new PointCollection { new Point(6, 0), new Point(12, 6), new Point(6, 12), new Point(0, 6) }, Fill = new SolidColorBrush(color), Stroke = Brushes.Black, StrokeThickness = 1.2, Width = 12, Height = 12, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }
                : new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(color), Stroke = Brushes.White, StrokeThickness = 1.5, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var n = new TextBlock { Text = name, FontWeight = FontWeights.Bold, FontSize = 12.5, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            var l = new TextBlock { Text = level, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x7A)), VerticalAlignment = VerticalAlignment.Center };
            var d = new TextBlock { Text = distance, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0x9D, 0xB3, 0xC2)), VerticalAlignment = VerticalAlignment.Center, MinWidth = 52, TextAlignment = TextAlignment.Right, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(n, 1);
            Grid.SetColumn(l, 2);
            Grid.SetColumn(d, 3);
            grid.Children.Add(dot);
            grid.Children.Add(n);
            grid.Children.Add(l);
            grid.Children.Add(d);
            _rows.Children.Add(grid);
        }
    }
}

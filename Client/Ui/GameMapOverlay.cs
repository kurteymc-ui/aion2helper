using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

public enum GameMapMarkKind
{
    Spawn,
    Live,
    Gather,
    Cube,
}

/// <summary>Something to draw on the game's map, at its native map position.</summary>
public sealed record GameMapMark(double X, double Y, Color Color, GameMapMarkKind Kind, string Name);

/// <summary>What the game's map gets drawn on it: the marks, where the player is (native, on this map), the mark the line leads to and what it says,
/// and the legend (colour, name, what the collection says).</summary>
public sealed record GameMapScene(string MapKey, IReadOnlyList<GameMapMark> Marks, (double X, double Y)? Player, GameMapMark? Target, string? TargetText,
    IReadOnlyList<(Color Color, string Name, string Note)> Legend);

/// <summary>
/// Draws a scene over the game's map: each spawn point of a pet still needed as a dot in its colour, the monsters there now as big dots, the
/// collectibles as diamonds, the Hidden Cubes in gold, a ring where the player is, a line to the nearest pet with its name and distance, and a
/// legend. Coordinates are pixels of the game window's client area.
/// </summary>
public sealed class GameMapMarksView : Canvas
{
    public GameMapMarksView()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    private static Brush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    private static readonly Brush Outline = Frozen(Color.FromArgb(0xE6, 0x08, 0x0A, 0x0E));
    private static readonly Brush Gold = Frozen(Color.FromRgb(0xE8, 0xC3, 0x5A));
    private static readonly Brush White = Frozen(Colors.White);
    private static readonly Brush Panel = Frozen(Color.FromArgb(0xD8, 0x10, 0x14, 0x1A));

    /// <summary>Draws <paramref name="scene"/> for the map shown as <paramref name="view"/> says, in an area of <paramref name="width"/> × <paramref name="height"/> pixels.</summary>
    public void Render(GameMapView view, GameMapScene scene, double width, double height, bool guide, bool legend)
    {
        Children.Clear();
        Width = width;
        Height = height;
        if (view.MapKey != scene.MapKey)
        {
            return;
        }

        bool Inside(double x, double y) => x >= -10 && y >= -10 && x <= width + 10 && y <= height + 10;
        double spot = Math.Clamp(2.2 + view.Scale * 6, 3, 6.5);
        foreach (GameMapMark mark in scene.Marks.OrderBy(m => m.Kind == GameMapMarkKind.Spawn ? 0 : 1))
        {
            var (x, y) = view.ToScreen(mark.X, mark.Y);
            if (!Inside(x, y))
            {
                continue;
            }

            Shape shape = mark.Kind switch
            {
                GameMapMarkKind.Gather => new Polygon
                {
                    Points = new PointCollection { new(0, -spot - 1.5), new(spot + 1.5, 0), new(0, spot + 1.5), new(-spot - 1.5, 0) },
                    Fill = Frozen(mark.Color), Stroke = Outline, StrokeThickness = 1.2,
                },
                _ => new Ellipse
                {
                    Width = 2 * (mark.Kind == GameMapMarkKind.Spawn ? spot : spot + 3.5), Height = 2 * (mark.Kind == GameMapMarkKind.Spawn ? spot : spot + 3.5),
                    Fill = mark.Kind == GameMapMarkKind.Cube ? Gold : Frozen(mark.Color),
                    Stroke = mark.Kind == GameMapMarkKind.Spawn ? Outline : White, StrokeThickness = mark.Kind == GameMapMarkKind.Spawn ? 1.2 : 2,
                },
            };
            if (shape is Ellipse dot)
            {
                SetLeft(dot, x - dot.Width / 2);
                SetTop(dot, y - dot.Height / 2);
            }
            else
            {
                SetLeft(shape, x);
                SetTop(shape, y);
            }

            shape.ToolTip = mark.Name;
            Children.Add(shape);
        }

        // where the player is: a gold ring (it should sit on the game's own arrow), and the line to the nearest pet
        if (scene.Player is { } me)
        {
            var (px, py) = view.ToScreen(me.X, me.Y);
            if (guide && scene.Target is { } target)
            {
                var (tx, ty) = view.ToScreen(target.X, target.Y);
                double dx = tx - px, dy = ty - py, length = Math.Sqrt(dx * dx + dy * dy);
                if (length > 14)
                {
                    double ux = dx / length, uy = dy / length;
                    var line = new Line
                    {
                        X1 = px + ux * 12, Y1 = py + uy * 12, X2 = tx - ux * (spot + 6), Y2 = ty - uy * (spot + 6),
                        Stroke = Gold, StrokeThickness = 2.4, StrokeDashArray = new DoubleCollection { 3, 2 }, StrokeDashCap = PenLineCap.Round,
                    };
                    Children.Add(line);
                    var head = new Polygon
                    {
                        Points = new PointCollection { new(line.X2, line.Y2), new(line.X2 - ux * 11 - uy * 6, line.Y2 - uy * 11 + ux * 6), new(line.X2 - ux * 11 + uy * 6, line.Y2 - uy * 11 - ux * 6) },
                        Fill = Gold,
                    };
                    Children.Add(head);
                }

                if (scene.TargetText is { Length: > 0 } text && Inside(tx, ty))
                {
                    var label = new Border
                    {
                        Background = Panel, BorderBrush = Gold, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 3),
                        Child = new TextBlock { Text = text, Foreground = White, FontSize = 13, FontWeight = FontWeights.SemiBold },
                    };
                    label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double lx = Math.Clamp(tx + 12, 4, Math.Max(4, width - label.DesiredSize.Width - 4)), ly = Math.Clamp(ty - label.DesiredSize.Height - 10, 4, Math.Max(4, height - label.DesiredSize.Height - 4));
                    SetLeft(label, lx);
                    SetTop(label, ly);
                    Children.Add(label);
                }
            }

            if (Inside(px, py))
            {
                var ring = new Ellipse { Width = 22, Height = 22, Stroke = Gold, StrokeThickness = 2.4 };
                SetLeft(ring, px - 11);
                SetTop(ring, py - 11);
                Children.Add(ring);
            }
        }

        // the legend: the pets on this map in their colours
        if (legend && scene.Legend.Count > 0)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = LocalizationManager.Instance["GameMap.Legend"], Foreground = Gold, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            foreach (var (color, name, note) in scene.Legend.Take(12))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                row.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = Frozen(color), Stroke = Outline, StrokeThickness = 1, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = name, Foreground = White, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
                if (note.Length > 0)
                {
                    row.Children.Add(new TextBlock { Text = "  " + note, Foreground = Frozen(Color.FromRgb(0x9D, 0xB3, 0xC2)), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
                }

                stack.Children.Add(row);
            }

            if (scene.Legend.Count > 12)
            {
                stack.Children.Add(new TextBlock { Text = string.Format(CultureInfo.CurrentCulture, LocalizationManager.Instance["GameMap.LegendMore"], scene.Legend.Count - 12), Foreground = Frozen(Color.FromRgb(0x9D, 0xB3, 0xC2)), FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0) });
            }

            var box = new Border { Child = stack, Background = Panel, BorderBrush = Frozen(Color.FromArgb(0x66, 0xE8, 0xC3, 0x5A)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7, 12, 8) };
            box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            SetLeft(box, 18);
            SetTop(box, Math.Max(10, height - box.DesiredSize.Height - 18));
            Children.Add(box);
        }
    }
}

/// <summary>The see-through window over the game window that carries the marks: never takes a click, never in a screenshot (so the next look at
/// the screen sees the game's map alone).</summary>
internal sealed class GameMapOverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;

    public GameMapMarksView View { get; } = new();

    private NativeOverlay? _native;
    private System.Drawing.Rectangle _area;

    public GameMapOverlayWindow()
    {
        Title = Brand.AppName + " game map";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        Content = View;
        AltTabHidden.Apply(this);
        SourceInitialized += (_, _) =>
        {
            _native = new NativeOverlay(this);
            _native.SetClickThrough(true);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE); // Windows 10 2004 and later; older ones see the marks in the picture, which costs a little accuracy
            if (_area.Width > 0)
            {
                Place(_area);
            }
        };
    }

    /// <summary>Over the game window's client area (screen pixels); the marks are drawn in those pixels whatever the display scaling.</summary>
    public void Place(System.Drawing.Rectangle area)
    {
        _area = area;
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(hwnd, HWND_TOPMOST, area.X, area.Y, area.Width, area.Height, SWP_NOACTIVATE);
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        View.LayoutTransform = new ScaleTransform(1 / dpi.DpiScaleX, 1 / dpi.DpiScaleY);
    }
}

/// <summary>
/// The game's map (M) with the pets on it: while the game is in front and a game menu is open, the game window is looked at a few times a second,
/// the game's map is found on it (GameMapMatcher), and the pets still needed, the collectibles picked and the Hidden Cubes are drawn on it where they
/// are, with a line from the player to the nearest pet. When the map closes (or is not found twice in a row), the marks go.
/// </summary>
public sealed class GameMapOverlay : IDisposable
{
    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly GameMapMatcher _matcher = new();
    private GameMapOverlayWindow? _window;
    private GameMapView? _view;
    private System.Drawing.Rectangle _area;
    private bool _busy;
    private int _misses;
    private long _lastLook;
    private int _otherMap;
    private MeterSettings _settings = MeterSettings.Load();
    private long _settingsAt;

    public GameMapOverlay(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// <summary>The last place the map was found at (for the tool page: "found at 37 %").</summary>
    public GameMapView? View => _view;

    private void Tick()
    {
        long now = Environment.TickCount64;
        if (now - _settingsAt > 1000)
        {
            _settings = MeterSettings.Load();
            _settingsAt = now;
        }

        if (!_settings.GameMapMarks || PrivacyConsent.Restricted || GameMapMatcher.Unavailable)
        {
            Lose();
            return;
        }

        System.Drawing.Rectangle? area = GameWindow.ForegroundClientArea(ignoreMenu: true);
        bool menu = MenuWatcher.Hidden;
        // with the cursor hidden while playing, the game's map comes with the cursor: no menu, no map
        if (area is null || (!menu && MenuWatcher.Mode == ControlMode.Hidden))
        {
            Lose();
            return;
        }

        _area = area.Value;
        if (_view is not null)
        {
            Draw();
        }

        long every = _view is not null ? 250 : menu ? 400 : 1500;
        if (_busy || now - _lastLook < every)
        {
            return;
        }

        _lastLook = now;
        Aion2EntityDirectory? directory = _directory();
        List<Aion2MapInfo> maps = Candidates(directory);
        if (maps.Count == 0)
        {
            return;
        }

        if (!GameMapMatcher.Available())
        {
            return;
        }

        _busy = true;
        GameMapView? near = _view;
        System.Drawing.Rectangle look = _area;
        _ = Task.Run(() =>
        {
            GameMapView? found = null;
            try
            {
                using OpenCvSharp.Mat? screen = GameMapMatcher.Capture(look);
                if (screen is not null)
                {
                    // where it was, then the zone's map, then (now and then) one of the others: the player may look at another region
                    foreach (Aion2MapInfo map in maps)
                    {
                        found = _matcher.Find(screen, map, near?.MapKey == map.Key ? near : null);
                        if (found is not null)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is OpenCvSharp.OpenCVException or InvalidOperationException or ArgumentException or ExternalException or DllNotFoundException or TypeInitializationException)
            {
                AutoUploadLog.Write("game map: " + ex.Message);
            }

            return found;
        }).ContinueWith(t => Application.Current?.Dispatcher.BeginInvoke(new Action(() => Done(t.Status == TaskStatus.RanToCompletion ? t.Result : null))));
    }

    /// <summary>The maps to look for: the one the map was found as last, the zone's own, and one other world map in turn.</summary>
    private List<Aion2MapInfo> Candidates(Aion2EntityDirectory? directory)
    {
        var list = new List<Aion2MapInfo>();
        void Add(Aion2MapInfo? map)
        {
            if (map is not null && list.All(m => m.Key != map.Key))
            {
                list.Add(map);
            }
        }

        Add(_view is { } v ? Aion2Maps.All.FirstOrDefault(m => m.Key == v.MapKey) : null);
        Add(ZoneMap(directory));
        if (_view is null && _misses >= 2 && Aion2Maps.All.Count > 0)
        {
            Add(Aion2Maps.All[_otherMap++ % Aion2Maps.All.Count]);
        }

        return list;
    }

    private static Aion2MapInfo? ZoneMap(Aion2EntityDirectory? directory)
    {
        if (directory is null)
        {
            return null;
        }

        Aion2MapInfo? map = Aion2Maps.OfZone(directory.ZoneId);
        if (map is null && !Aion2Zones.IsInstance(directory.ZoneId))
        {
            map = Aion2Maps.Detect(directory.RecentMobPositions());
        }

        return map ?? (MeterSettings.Load().PetMapLastMap is { } last ? Aion2Maps.All.FirstOrDefault(m => m.Key == last) : null);
    }

    private void Done(GameMapView? found)
    {
        _busy = false;
        if (found is not null)
        {
            _view = found;
            _misses = 0;
            Draw();
            return;
        }

        if (++_misses >= 2)
        {
            Hide();
            _view = null;
        }
    }

    private void Draw()
    {
        if (_view is not { } view)
        {
            return;
        }

        Aion2MapInfo? map = Aion2Maps.All.FirstOrDefault(m => m.Key == view.MapKey);
        if (map is null)
        {
            return;
        }

        if (_window is null)
        {
            _window = new GameMapOverlayWindow();
        }

        if (!_window.IsVisible)
        {
            _window.Show();
        }

        _window.Place(_area);
        GameMapScene scene = SceneOf(map, _directory(), _settings, LocalizationManager.Instance.Language);
        _window.View.Render(view, scene, _area.Width, _area.Height, _settings.GameMapGuide, _settings.GameMapLegend);
    }

    private void Lose()
    {
        _view = null;
        _misses = 0;
        Hide();
    }

    private void Hide()
    {
        if (_window is { IsVisible: true })
        {
            _window.Hide();
        }
    }

    /// <summary>How far around the player the game announces every monster (about 55 m): a spawn point inside it without one is empty now.</summary>
    private const double MobRange = 5500;

    /// <summary>
    /// What goes on a map: the spawn points of the pets still needed (every one the collection says is not at the top level, or the ones picked),
    /// the monsters of those pets the game announced, the collectibles picked, the Hidden Cubes in range, where the player is, and the nearest pet to
    /// head for (a monster that is there now first, else the nearest spawn point). The colours are those of the interactive map and its list.
    /// </summary>
    public static GameMapScene SceneOf(Aion2MapInfo map, Aion2EntityDirectory? directory, MeterSettings settings, string language)
    {
        var states = directory?.LocalPetStates is { Count: > 0 } live ? live
            : (Aion2CharacterStore.Load(Aion2CharacterStore.DefaultPath)?.Pets ?? new()).Select(p => new Aion2PetState(p.Id, p.Level, p.Progress)).ToList();
        var levels = states.ToDictionary(s => s.PetId);
        var chosen = (settings.PetMapAuto ? Aion2Pets.AllPetIds : settings.PetMapPets)
            .Where(id => !(levels.TryGetValue(id, out Aion2PetState? s) && s.Level >= Aion2Pets.TopLevel)).ToHashSet();
        string? faction = Aion2Gather.OwnFaction();
        var gatherKinds = Aion2Gather.Items().Where(i => settings.PetMapGatherItems.Contains(i.Key) && i.IsFor(faction)).Select(i => i.Key).ToHashSet();

        bool here = directory is not null && ZoneMap(directory)?.Key == map.Key;
        (double X, double Y)? me = null;
        if (here && directory!.BestPosition is { } p && DateTime.UtcNow - p.At < TimeSpan.FromSeconds(30))
        {
            me = (p.X, p.Y); // world units for now
        }

        var points = Aion2Maps.PointsOf(map, chosen);
        var pets = points.Select(x => x.PetId).Distinct().OrderBy(id => id).ToList();
        var colors = pets.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => PetMapPalette.Of(t.i));
        var liveMobs = here ? directory!.LiveMobs().SelectMany(m => Aion2Pets.PetsOfNpc(m.NpcId).Where(chosen.Contains).Select(pet => (PetId: pet, m.X, m.Y))).ToList()
            : new List<(int PetId, float X, float Y)>();
        var marks = new List<GameMapMark>();
        string Name(int pet) => Aion2Pets.PetName(pet, language) ?? $"#{pet}";
        foreach (var (pet, x, y) in points)
        {
            // within the range the game announces monsters in, an empty spawn point is left out (the monster is dead or not back yet)
            if (me is { } at && (x - at.X) * (double)(x - at.X) + (y - at.Y) * (double)(y - at.Y) < MobRange * MobRange
                && !liveMobs.Any(l => l.PetId == pet && Math.Abs(l.X - x) < 800 && Math.Abs(l.Y - y) < 800))
            {
                continue;
            }

            var (nx, ny) = Aion2Maps.ToNative(map, x, y);
            marks.Add(new GameMapMark(nx, ny, colors[pet], GameMapMarkKind.Spawn, Name(pet)));
        }

        foreach (var (pet, x, y) in liveMobs)
        {
            var (nx, ny) = Aion2Maps.ToNative(map, x, y);
            marks.Add(new GameMapMark(nx, ny, colors.TryGetValue(pet, out Color c) ? c : PetMapPalette.Of(0), GameMapMarkKind.Live, Name(pet)));
        }

        foreach (var (item, x, y) in Aion2Gather.NamedPointsOf(map, gatherKinds))
        {
            var (nx, ny) = Aion2Maps.ToNative(map, x, y);
            marks.Add(new GameMapMark(nx, ny, PetMapPalette.OfGather(item.Kind), GameMapMarkKind.Gather, item.NameIn(language)));
        }

        if (here)
        {
            foreach (var (x, y) in directory!.LiveCubes())
            {
                var (nx, ny) = Aion2Maps.ToNative(map, x, y);
                marks.Add(new GameMapMark(nx, ny, Color.FromRgb(0xE8, 0xC3, 0x5A), GameMapMarkKind.Cube, LocalizationManager.Instance["Cubes.LineOne"]));
            }
        }

        // the nearest pet to head for: one there now first
        GameMapMark? target = null;
        string? text = null;
        if (me is { } from)
        {
            var (fx, fy) = Aion2Maps.ToNative(map, from.X, from.Y);
            double Distance(GameMapMark m) => Math.Sqrt((m.X - fx) * (m.X - fx) + (m.Y - fy) * (m.Y - fy)) / map.Scale / 100.0; // native pixels back to metres
            target = marks.Where(m => m.Kind == GameMapMarkKind.Live).MinBy(Distance) ?? marks.Where(m => m.Kind == GameMapMarkKind.Spawn).MinBy(Distance);
            if (target is not null)
            {
                text = $"{target.Name} · {Math.Max(1, (int)Math.Round(Distance(target)))} m";
            }

            me = (fx, fy);
        }

        LocalizationManager loc = LocalizationManager.Instance;
        var legend = pets.Select(id =>
        {
            string note = levels.TryGetValue(id, out Aion2PetState? s)
                ? string.Format(loc["PetFarm.Level"], s.Level) + $" · {s.Progress}/{Aion2Pets.ProgressNeeded(s.Level)}"
                : states.Count > 0 ? loc["PetShop.Missing"] : "";
            return (colors[id], Name(id), note);
        }).ToList();
        return new GameMapScene(map.Key, marks, me, target, text, legend);
    }

    public void Dispose()
    {
        _timer.Stop();
        _window?.Close();
        _window = null;
        _matcher.Dispose();
    }
}

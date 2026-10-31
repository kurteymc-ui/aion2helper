using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The cube finder: the game announces a Hidden Cube once the player comes within about 100 m of it (Aion2EntityDirectory.LiveCubes). Its parts:
/// a line on the meter while one is near (here), the parking-sensor sound and the map brought up for it (both in PetMapController), and the
/// Tools page "Cube finder" that holds their switches and says what is in range now.
/// </summary>
public partial class MainWindow
{
    private long _cubeCalloutAt;

    /// <summary>A few times a second: "Hidden Cube nearby · 42 m" on the meter while a cube is in range (Settings: the cube finder's line).</summary>
    private void UpdateOverlayCubeCallout()
    {
        if (Headless || (!_compactOverlay && !MeterOverlayActive))
        {
            return;
        }

        long tick = Environment.TickCount64;
        if (tick - _cubeCalloutAt < 400)
        {
            return;
        }

        _cubeCalloutAt = tick;
        if (!_gateSettings.CubeOverlayAlert || PrivacyConsent.Restricted || _source?.Entities is not Aion2EntityDirectory directory
            || directory.LiveCubes() is not { Count: > 0 } cubes)
        {
            OverlayCubeCallout.Visibility = Visibility.Collapsed;
            return;
        }

        ShowCubeCallout(OverlayCubeCalloutText, cubes.Count, NearestCubeMetres(directory, cubes));
        OverlayCubeCallout.Visibility = Visibility.Visible;
    }

    /// <summary>The distance to the nearest cube in metres (100 world units), when the own position is fresh (5 s); null while it is not known.</summary>
    private static double? NearestCubeMetres(Aion2EntityDirectory directory, IReadOnlyList<(float X, float Y)> cubes) =>
        cubes.Count > 0 && directory.BestPosition is { } me && DateTime.UtcNow - me.At < TimeSpan.FromSeconds(5)
            ? cubes.Min(c => Math.Sqrt((c.X - me.X) * (double)(c.X - me.X) + (c.Y - me.Y) * (double)(c.Y - me.Y))) / 100.0
            : null;

    /// <summary>The meter's cube line: how many are in range and how far the nearest is (also drawn by the overlay pictures).</summary>
    internal static void ShowCubeCallout(TextBlock text, int count, double? metres)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string what = count > 1 ? string.Format(CultureInfo.CurrentCulture, loc["Cubes.LineMany"], count) : loc["Cubes.LineOne"];
        text.Text = metres is double m ? what + " · " + string.Format(CultureInfo.CurrentCulture, loc["Cubes.Metres"], Math.Max(1, (int)Math.Round(m))) : what;
    }

    /// <summary>Tools → Cube finder: what the game announces now, and the switches of the line, the sound and the map.</summary>
    private ToolPage BuildCubesPage()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var column = new StackPanel { Margin = new Thickness(24, 20, 24, 24), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
        column.Children.Add(PageTitle(loc["Cubes.Title"], loc["Cubes.Lede"]));

        // what is in range now
        var nowLine = ShellText("", "Brush.Text", 20, bold: true);
        var nowSub = Wrapped("", "Brush.TextSubtle", 12);
        nowSub.Margin = new Thickness(0, 4, 0, 0);
        var nowStack = new StackPanel();
        var nowHead = new StackPanel { Orientation = Orientation.Horizontal };
        var cube = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M9,1 L16.5,5 L16.5,13 L9,17 L1.5,13 L1.5,5 Z M1.5,5 L9,9 L16.5,5 M9,9 L9,17"),
            Stroke = new SolidColorBrush(Color.FromRgb(0xE8, 0xC3, 0x5A)), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round,
            Width = 18, Height = 18, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        nowHead.Children.Add(cube);
        nowHead.Children.Add(nowLine);
        nowStack.Children.Add(nowHead);
        nowStack.Children.Add(nowSub);
        column.Children.Add(HomeCard(loc["Cubes.Now"], nowStack, null, 0));

        void ShowNow()
        {
            if (PrivacyConsent.Restricted || _source?.Entities is not Aion2EntityDirectory directory)
            {
                nowLine.Text = loc["Cubes.NoGame"];
                nowSub.Text = loc["Cubes.NoGameHow"];
                return;
            }

            var cubes = directory.LiveCubes();
            double? metres = NearestCubeMetres(directory, cubes);
            nowLine.Text = cubes.Count == 0 ? loc["Cubes.None"]
                : (cubes.Count > 1 ? string.Format(CultureInfo.CurrentCulture, loc["Cubes.LineMany"], cubes.Count) : loc["Cubes.LineOne"])
                  + (metres is double m ? " · " + string.Format(CultureInfo.CurrentCulture, loc["Cubes.Metres"], Math.Max(1, (int)Math.Round(m))) : "");
            int seen = Math.Max(0, directory.CubeArrivals);
            nowSub.Text = string.Format(CultureInfo.CurrentCulture, loc["Cubes.Seen"], seen);
        }

        if (Headless)
        {
            nowLine.Text = loc["Cubes.LineOne"] + " · " + string.Format(CultureInfo.CurrentCulture, loc["Cubes.Metres"], 42);
            nowSub.Text = string.Format(CultureInfo.CurrentCulture, loc["Cubes.Seen"], 3);
        }
        else
        {
            ShowNow();
        }

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => ShowNow();
        if (!Headless)
        {
            timer.Start();
        }

        // the switches: each change is saved and applied at once
        MeterSettings s0 = MeterSettings.Load();
        var switches = new StackPanel();
        void Save(Action<MeterSettings> change)
        {
            MeterSettings s = MeterSettings.Load();
            change(s);
            s.Save();
            ApplySettingsEverywhere(s);
            _gateSettingsAt = DateTime.MinValue; // the meter's line follows at once
        }

        CheckBox Switch(string title, string tip, bool on, Action<MeterSettings, bool> set)
        {
            var text = new StackPanel();
            text.Children.Add(ShellText(title, "Brush.Text", 13.5, bold: true));
            TextBlock small = Wrapped(tip, "Brush.TextMuted", 12);
            small.Margin = new Thickness(0, 2, 0, 0);
            small.MaxWidth = 700; // the check box gives its content all the room it asks for: the text wraps by itself only below this
            text.Children.Add(small);
            var box = new CheckBox { IsChecked = on, Content = text, Margin = new Thickness(0, 0, 0, 12), VerticalContentAlignment = VerticalAlignment.Top };
            box.Click += (_, _) => Save(s => set(s, box.IsChecked == true));
            switches.Children.Add(box);
            return box;
        }

        Switch(loc["Cubes.Line"], loc["Cubes.LineTip"], s0.CubeOverlayAlert, (s, v) => s.CubeOverlayAlert = v);
        CheckBox sound = Switch(loc["Cubes.Sound"], loc["Cubes.SoundTip"], s0.CubeSoundEnabled, (s, v) => s.CubeSoundEnabled = v);
        var volumeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, -4, 0, 14) };
        volumeRow.Children.Add(ShellText(loc["Cubes.Volume"], "Brush.TextMuted", 12));
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = Math.Clamp(s0.CubeSoundVolume, 0, 100), Width = 180, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = true, TickFrequency = 5 };
        var volumeSave = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        volumeSave.Tick += (_, _) =>
        {
            volumeSave.Stop();
            Save(s => s.CubeSoundVolume = (int)volume.Value);
        };
        volume.ValueChanged += (_, _) =>
        {
            volumeSave.Stop();
            volumeSave.Start();
        };
        volumeRow.Children.Add(volume);
        var test = new Button { Content = loc["Cubes.Test"], Padding = new Thickness(12, 3, 12, 3), VerticalAlignment = VerticalAlignment.Center };
        test.Click += (_, _) => NotifySounds.PlayCubeArrival((int)volume.Value);
        volumeRow.Children.Add(test);
        switches.Children.Insert(switches.Children.IndexOf(sound) + 1, volumeRow);
        Switch(loc["Cubes.Map"], loc["Cubes.MapTip"], s0.ShowMapOnCube, (s, v) => s.ShowMapOnCube = v);
        Switch(loc["Cubes.Mark"], loc["Cubes.MarkTip"], s0.PetMapGatherItems.Contains("Hidden Cube"), (s, v) =>
        {
            s.PetMapGatherItems.Remove("Hidden Cube");
            if (v)
            {
                s.PetMapGatherItems.Add("Hidden Cube");
            }
        });
        column.Children.Add(HomeCard(loc["Cubes.Switches"], switches, null, 0));

        var more = new StackPanel { Orientation = Orientation.Horizontal };
        more.Children.Add(ShellLink(loc["Cubes.OpenMap"], () => ShowShellPage("petmap")));
        column.Children.Add(more);
        TextBlock note = Wrapped(loc["Cubes.Note"], "Brush.TextSubtle", 11.5);
        note.Margin = new Thickness(0, 14, 0, 0);
        column.Children.Add(note);

        return new ToolPage
        {
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = column },
            Cleanup = () =>
            {
                timer.Stop();
                if (volumeSave.IsEnabled)
                {
                    volumeSave.Stop();
                    Save(s => s.CubeSoundVolume = (int)volume.Value);
                }
            },
        };
    }
}

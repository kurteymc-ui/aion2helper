using System.Windows;
using System.Windows.Controls;

namespace AionDPS.Ui;

/// <summary>The switches of the pets on the game's own map (GameMapOverlay), on the Interactive map page.</summary>
public partial class MainWindow
{
    private UIElement GameMapSwitches()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        MeterSettings now = MeterSettings.Load();
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 10), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(ShellText(loc["GameMap.Title"], "Brush.Text", 13.5, bold: true));
        TextBlock what = Wrapped(loc[GameMapMatcher.Unavailable ? "GameMap.Unavailable" : "GameMap.What"], "Brush.TextMuted", 12);
        what.Margin = new Thickness(0, 2, 0, 6);
        stack.Children.Add(what);
        var row = new WrapPanel();
        CheckBox Switch(string label, bool on, Action<MeterSettings, bool> set)
        {
            var box = new CheckBox { IsChecked = on, Content = ShellText(label, "Brush.Text", 12.5), Margin = new Thickness(0, 0, 18, 4) };
            box.Click += (_, _) =>
            {
                MeterSettings s = MeterSettings.Load();
                set(s, box.IsChecked == true);
                s.Save();
            };
            row.Children.Add(box);
            return box;
        }

        Switch(loc["GameMap.Marks"], now.GameMapMarks, (s, v) => s.GameMapMarks = v);
        Switch(loc["GameMap.Guide"], now.GameMapGuide, (s, v) => s.GameMapGuide = v);
        Switch(loc["GameMap.LegendSwitch"], now.GameMapLegend, (s, v) => s.GameMapLegend = v);
        stack.Children.Add(row);
        return stack;
    }
}

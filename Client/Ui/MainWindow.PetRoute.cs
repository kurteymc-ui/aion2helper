using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>The pet route's switches on the Interactive map page: the guide on or off, the genera, where it starts, the next stop, and the map's shape for the minimap.</summary>
public partial class MainWindow
{
    /// <summary>The route card, and what refreshes its status line (the page's timer calls it).</summary>
    private (UIElement Card, Action Refresh) PetRouteControls()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        MeterSettings now = MeterSettings.Load();
        var stack = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
        stack.Children.Add(ShellText(loc["PetRoute.Title"], "Brush.Text", 14, bold: true));
        TextBlock lede = Wrapped(loc["PetRoute.Lede"], "Brush.TextMuted", 12);
        lede.Margin = new Thickness(0, 2, 0, 8);
        stack.Children.Add(lede);

        void Save(Action<MeterSettings> change)
        {
            MeterSettings s = MeterSettings.Load();
            change(s);
            s.Save();
        }

        CheckBox Check(string label, bool on, Action<MeterSettings, bool> set, string? tip = null)
        {
            var box = new CheckBox { IsChecked = on, Content = ShellText(label, "Brush.Text", 12.5), ToolTip = tip, Margin = new Thickness(0, 0, 18, 6), VerticalAlignment = VerticalAlignment.Center };
            box.Click += (_, _) => Save(s => set(s, box.IsChecked == true));
            return box;
        }

        stack.Children.Add(Check(loc["PetRoute.On"], now.PetRouteOn, (s, v) => s.PetRouteOn = v));

        // the genera, as the website's chips
        var genera = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        genera.Children.Add(ShellText(loc["PetRoute.Genera"], "Brush.TextMuted", 12));
        ((TextBlock)genera.Children[0]).Margin = new Thickness(0, 0, 10, 0);
        var picked = (now.PetRouteGenera is { Count: > 0 } g ? g : PetRoute.Genera.ToList()).ToHashSet();
        foreach (string genus in PetRoute.Genera)
        {
            string id = genus;
            string shown = genus switch { "Cogni" => "Cognia", "Varian" => "Varia", _ => genus }; // as the meter's pet pages name them
            genera.Children.Add(Check(shown, picked.Contains(id), (s, v) =>
            {
                var list = (s.PetRouteGenera is { Count: > 0 } had ? had : PetRoute.Genera.ToList()).Where(x => x != id).ToList();
                if (v)
                {
                    list.Add(id);
                }

                s.PetRouteGenera = list.Count == 0 ? null : PetRoute.Genera.Where(list.Contains).ToList();
            }));
        }

        stack.Children.Add(genera);
        var start = new WrapPanel();
        start.Children.Add(Check(loc["PetRoute.FromHere"], now.PetRouteFromHere, (s, v) => s.PetRouteFromHere = v, loc["PetRoute.FromHereTip"]));
        stack.Children.Add(start);

        // the stop being farmed, and the buttons
        TextBlock status = Wrapped("", "Brush.Text", 12.5);
        status.Margin = new Thickness(0, 4, 0, 6);
        stack.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var next = new Button { Content = loc["PetRoute.Next"], Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 8, 0), ToolTip = loc["PetRoute.NextTip"] };
        next.Click += (_, _) => _petMapController?.NextStop();
        var again = new Button { Content = loc["PetRoute.Again"], Padding = new Thickness(12, 3, 12, 3), ToolTip = loc["PetRoute.AgainTip"] };
        again.Click += (_, _) => _petMapController?.ReplanHere(keepSkipped: false);
        buttons.Children.Add(next);
        buttons.Children.Add(again);
        stack.Children.Add(buttons);

        // the map's shape, to lay it over the game's minimap
        var shape = new WrapPanel();
        shape.Children.Add(Check(loc["PetRoute.Round"], now.PetMapRound, (s, v) => s.PetMapRound = v));
        shape.Children.Add(Check(loc["PetRoute.MarksOnly"], now.PetMapMarkersOnly, (s, v) => s.PetMapMarkersOnly = v, loc["PetRoute.MarksOnlyTip"]));
        stack.Children.Add(shape);

        void Refresh()
        {
            if (!MeterSettings.Load().PetRouteOn)
            {
                status.Text = loc["PetRoute.Off"];
                return;
            }

            if (_petMapController?.Route is not { } plan)
            {
                status.Text = loc["PetRoute.NoRoute"];
                return;
            }

            if (plan.Stops.Count == 0)
            {
                status.Text = loc["PetRoute.Done"];
                return;
            }

            PetRouteStop stop = plan.Stops[0];
            string pets = string.Join(", ", stop.Pets.Select(id => Aion2Pets.PetName(id, loc.Language) ?? PetRoute.NameOf(id) ?? $"#{id}"));
            status.Text = string.Format(CultureInfo.CurrentCulture, loc["PetRoute.Stop"], stop.Step, plan.Stops.Count, stop.Zone) + ": " + pets;
        }

        Refresh();
        return (stack, Refresh);
    }
}

using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Krate.Gui;

/// <summary>Any-time-unit-to-any-time-unit converter: a value, a from-unit and a to-unit, plus the
/// same value listed in every unit. All the unit maths is Core's (<see cref="Dates.TimeUnits"/>).</summary>
public sealed partial class DurationPage : UserControl
{
    public DurationPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Duration_Name");
        AllLabel.Text = Strings.Get("Duration_All");
        foreach (var u in Dates.TimeUnits) { From.Items.Add(u.Label); To.Items.Add(u.Label); }
        From.SelectedIndex = IndexOf("h");
        To.SelectedIndex = IndexOf("s");
        Refresh();
    }

    static int IndexOf(string key)
    {
        for (var i = 0; i < Dates.TimeUnits.Length; i++) if (Dates.TimeUnits[i].Key == key) return i;
        return 0;
    }

    void OnChanged(object sender, object e) => Refresh();

    void Refresh()
    {
        if (AllUnits is null || From.SelectedIndex < 0 || To.SelectedIndex < 0) return; // handlers fire before the tree is built
        var value = double.IsNaN(Value.Value) ? 0 : Value.Value;
        var from = Dates.TimeUnits[From.SelectedIndex];
        var to = Dates.TimeUnits[To.SelectedIndex];
        var seconds = value * from.Seconds;

        Result.Text = $"{N(value)} {from.Label} = {N(seconds / to.Seconds)} {to.Label}";

        AllUnits.Children.Clear();
        foreach (var (label, val) in Dates.InEveryUnit(seconds))
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(new TextBlock { Text = label, Opacity = 0.75 });
            var v = new TextBlock { Text = val, FontFamily = new FontFamily("Consolas") };
            Grid.SetColumn(v, 1);
            grid.Children.Add(v);
            AllUnits.Children.Add(grid);
        }
    }

    static string N(double v) => string.Create(CultureInfo.InvariantCulture, $"{v:0.######}");
}

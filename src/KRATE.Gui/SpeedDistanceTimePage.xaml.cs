using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Speed / distance / time solver with unit dropdowns — fill any two, leave the third blank.
/// The physics is Core's <see cref="Physics.Solve"/>; this page just assembles its "100km 2h" input.</summary>
public sealed partial class SpeedDistanceTimePage : UserControl
{
    static readonly string[] DistUnits = ["km", "m", "mi", "ft", "yd"];
    static readonly string[] TimeUnits = ["h", "min", "s", "d"];
    static readonly string[] SpeedUnits = ["km/h", "m/s", "mph", "kn"];

    (NumberBox Box, ComboBox Unit, string[] Units)[] _rows = [];

    public SpeedDistanceTimePage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_SpeedDistanceTime_Name");
        Hint.Text = Strings.Get("Sdt_Hint");
        DistLabel.Text = Strings.Get("Sdt_Distance");
        TimeLabel.Text = Strings.Get("Sdt_Time");
        SpeedLabel.Text = Strings.Get("Sdt_Speed");

        _rows =
        [
            (DistValue, DistUnit, DistUnits),
            (TimeValue, TimeUnit, TimeUnits),
            (SpeedValue, SpeedUnit, SpeedUnits),
        ];
        foreach (var (_, combo, units) in _rows)
        {
            foreach (var u in units) combo.Items.Add(u);
            combo.SelectedIndex = 0;
        }
        // A worked example so the page isn't blank: 100 km in 2 h → solve for speed.
        DistValue.Value = 100;
        TimeValue.Value = 2;
        Solve();
    }

    void OnChanged(object sender, object e) => Solve();

    void Solve()
    {
        if (Result is null) return; // handlers fire before the tree is built
        var tokens = new List<string>();
        foreach (var (box, combo, units) in _rows)
            if (!double.IsNaN(box.Value) && combo.SelectedIndex >= 0)
                tokens.Add($"{box.Value.ToString(CultureInfo.InvariantCulture)}{units[combo.SelectedIndex]}");

        if (tokens.Count != 2) { Result.Text = Strings.Get("Sdt_Hint"); return; }
        try { Result.Text = Physics.Solve(string.Join(' ', tokens)); }
        catch (Exception ex) { Result.Text = ex.Message; }
    }
}

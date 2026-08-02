using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Unit converter as value + two dropdowns, grouped by what you're measuring — the shape
/// everyone expects. The maths is Core's <see cref="Units.Convert"/>.</summary>
public sealed partial class ConvertPage : UserControl
{
    readonly IReadOnlyDictionary<string, string[]> _units = Units.UnitsByDimension();

    public ConvertPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Convert_Name");
        Category.Header = Strings.Get("Conv_Category");
        Value.Header = Strings.Get("Conv_Value");

        foreach (var dimension in _units.Keys.OrderBy(k => k))
            Category.Items.Add(dimension);
        Category.SelectedIndex = 0;
    }

    void OnCategory(object sender, SelectionChangedEventArgs e)
    {
        if (Category.SelectedItem is not string dimension) return;
        var units = _units[dimension];
        Fill(From, units, 0);
        Fill(To, units, Math.Min(1, units.Length - 1));
        Convert();
    }

    static void Fill(ComboBox box, string[] units, int select)
    {
        box.Items.Clear();
        foreach (var u in units) box.Items.Add(u);
        box.SelectedIndex = select;
    }

    void OnChanged(object sender, object e) => Convert();

    void Convert()
    {
        if (Result is null) return; // Value's ValueChanged fires mid-parse, before From/To/Result exist
        if (From.SelectedItem is not string from || To.SelectedItem is not string to || double.IsNaN(Value.Value)) return;
        try
        {
            Result.Text = Units.Convert($"{Value.Value.ToString(CultureInfo.InvariantCulture)} {from} {to}");
        }
        catch (Exception ex) { Result.Text = ex.Message; }
    }
}

using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>CSS unit conversion: a number and a unit dropdown, instead of typing "16px".</summary>
public sealed partial class CssUnitsPage : UserControl
{
    static readonly string[] Units = ["px", "rem", "em", "pt", "%"];

    public CssUnitsPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_CssUnits_Name");
        foreach (var u in Units) Unit.Items.Add(u);
        Unit.SelectedIndex = 0;
    }

    void OnChanged(object sender, object e)
    {
        if (Result is null || Unit.SelectedIndex < 0) return;
        var v = double.IsNaN(Value.Value) ? 0 : Value.Value;
        try { Result.Text = Css.Units($"{v.ToString(CultureInfo.InvariantCulture)}{Units[Unit.SelectedIndex]}"); }
        catch (Exception ex) { Result.Text = ex.Message; }
    }
}

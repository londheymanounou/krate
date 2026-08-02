using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Shoe size: a number plus region and gender dropdowns, instead of remembering that Core
/// keys off letters in a typed string ("42 eu women").</summary>
public sealed partial class ShoePage : UserControl
{
    static readonly string[] Systems = ["eu", "uk", "us", "cm"];

    public ShoePage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_ShoeSize_Name");
        foreach (var s in Systems) System.Items.Add(s.ToUpperInvariant());
        Gender.Items.Add(Strings.Get("Shoe_Men"));
        Gender.Items.Add(Strings.Get("Shoe_Women"));
        System.SelectedIndex = 0;
        Gender.SelectedIndex = 0;
    }

    void OnChanged(object sender, object e)
    {
        if (Result is null || System.SelectedIndex < 0 || Gender.SelectedIndex < 0) return;
        var value = double.IsNaN(Size.Value) ? 0 : Size.Value;
        var gender = Gender.SelectedIndex == 1 ? "women" : "men";
        try { Result.Text = Sizes.Shoe($"{value.ToString(CultureInfo.InvariantCulture)} {Systems[System.SelectedIndex]} {gender}"); }
        catch (Exception ex) { Result.Text = ex.Message; }
    }
}

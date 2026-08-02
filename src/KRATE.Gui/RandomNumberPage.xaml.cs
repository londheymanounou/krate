using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Random number generator: a min/max form and a Draw button (crypto-backed via Core's
/// <see cref="Generators.RandomNumber"/>).</summary>
public sealed partial class RandomNumberPage : UserControl
{
    public RandomNumberPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Random_Name");
        Min.Header = Strings.Get("Rnd_Min");
        Max.Header = Strings.Get("Rnd_Max");
        DrawButton.Content = Strings.Get("Rnd_Draw");
        Draw();
    }

    void OnDraw(object sender, RoutedEventArgs e) => Draw();

    void Draw()
    {
        var min = (long)(double.IsNaN(Min.Value) ? 0 : Min.Value);
        var max = (long)(double.IsNaN(Max.Value) ? 0 : Max.Value);
        try { Result.Text = Generators.RandomNumber($"{min.ToString(CultureInfo.InvariantCulture)} {max.ToString(CultureInfo.InvariantCulture)}"); }
        catch (Exception ex) { Result.Text = ex.Message; }
    }
}

using Krate.Core;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Aspect ratio: two dimension fields instead of typing "1920x1080".</summary>
public sealed partial class AspectRatioPage : UserControl
{
    public AspectRatioPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_AspectRatio_Name");
        Update();
    }

    void OnChanged(NumberBox sender, NumberBoxValueChangedEventArgs e) => Update();

    void Update()
    {
        if (Result is null) return;
        var w = (long)(double.IsNaN(Wid.Value) ? 0 : Wid.Value);
        var h = (long)(double.IsNaN(Hgt.Value) ? 0 : Hgt.Value);
        try { Result.Text = Images.Ratio($"{w}x{h}"); }
        catch (Exception ex) { Result.Text = ex.Message; }
    }
}

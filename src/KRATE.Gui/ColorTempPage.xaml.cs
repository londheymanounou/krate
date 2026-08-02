using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinColor = Windows.UI.Color;

namespace Krate.Gui;

/// <summary>A Kelvin slider with a live swatch of that white point (Core's <see cref="Colors.KelvinToRgb"/>).</summary>
public sealed partial class ColorTempPage : UserControl
{
    public ColorTempPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_ColorTemp_Name");
        Update();
    }

    void OnChanged(object sender, object e) => Update();

    void Update()
    {
        if (Values is null) return; // Kelvin fires mid-parse; Swatch exists by then but Values (declared later) doesn't
        var k = (int)Kelvin.Value;
        var (r, g, b) = Colors.KelvinToRgb(k);
        var color = WinColor.FromArgb(255, (byte)r, (byte)g, (byte)b);
        Swatch.Background = new SolidColorBrush(color);
        // Dark text on warm/light temps, light text on cool ones, so the label stays readable.
        KelvinLabel.Foreground = new SolidColorBrush(0.299 * r + 0.587 * g + 0.114 * b > 150 ? Windows.UI.Color.FromArgb(255, 0, 0, 0) : Windows.UI.Color.FromArgb(255, 255, 255, 255));
        KelvinLabel.Text = $"{k:N0} K — {Describe(k)}";
        Values.Text = Colors.Describe((r, g, b));
    }

    static string Describe(int k) => k switch
    {
        < 2200 => "candlelight",
        < 3200 => "warm white",
        < 4500 => "neutral white",
        < 5500 => "daylight",
        < 7000 => "cool daylight",
        _ => "overcast / blue sky",
    };
}

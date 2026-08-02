using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using WinColor = Windows.UI.Color;

namespace Krate.Gui;

/// <summary>Two colour swatches + angle, with a live gradient preview and the CSS snippet (from
/// Core's <see cref="Css.Gradient"/>) ready to copy.</summary>
public sealed partial class GradientPage : UserControl
{
    public GradientPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Gradient_Name");
        CopyButton.Content = Strings.Get("Gui_Copy");
        Picker1.Color = WinColor.FromArgb(255, 0xFF, 0x51, 0x51);
        Picker2.Color = WinColor.FromArgb(255, 0x51, 0x8B, 0xFF);
        Update();
    }

    void OnChanged(object sender, object e) => Update();

    void Update()
    {
        if (Css is null) return; // Angle's ValueChanged fires mid-parse; guard the last-declared element it touches
        var c1 = Picker1.Color;
        var c2 = Picker2.Color;
        Swatch1.Fill = new SolidColorBrush(c1);
        Swatch2.Fill = new SolidColorBrush(c2);

        var angle = double.IsNaN(Angle.Value) ? 90 : Angle.Value;
        // WinUI gradient goes start→end; derive endpoints from the CSS angle (0° = up, 90° = right).
        var rad = (angle - 90) * Math.PI / 180;
        var dx = Math.Cos(rad) / 2;
        var dy = Math.Sin(rad) / 2;
        Preview.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0.5 - dx, 0.5 - dy),
            EndPoint = new Point(0.5 + dx, 0.5 + dy),
            GradientStops =
            {
                new GradientStop { Color = c1, Offset = 0 },
                new GradientStop { Color = c2, Offset = 1 },
            },
        };

        Css.Text = Krate.Core.Css.Gradient($"#{c1.R:X2}{c1.G:X2}{c1.B:X2} #{c2.R:X2}{c2.G:X2}{c2.B:X2} {angle:0}deg");
    }

    void OnCopy(object sender, RoutedEventArgs e)
    {
        var data = new DataPackage();
        data.SetText(Css.Text);
        Clipboard.SetContent(data);
    }
}

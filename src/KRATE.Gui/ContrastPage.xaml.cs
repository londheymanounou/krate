using Krate.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinColor = Windows.UI.Color;

namespace Krate.Gui;

/// <summary>WCAG contrast with two colour swatches and a live sample-text preview. The ratio and
/// pass/fail come from Core's <see cref="Colors.Contrast"/>.</summary>
public sealed partial class ContrastPage : UserControl
{
    public ContrastPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Contrast_Name");
        FgLabel.Text = Strings.Get("Contrast_Fg");
        BgLabel.Text = Strings.Get("Contrast_Bg");
        var sample = Strings.Get("Contrast_Sample");
        SampleLarge.Text = sample;
        SampleSmall.Text = sample;
        FgPicker.Color = WinColor.FromArgb(255, 0x11, 0x11, 0x11);
        BgPicker.Color = WinColor.FromArgb(255, 0xFF, 0xFF, 0xFF);
        Update();
    }

    void OnColor(ColorPicker sender, ColorChangedEventArgs args) => Update();

    void Update()
    {
        var fg = FgPicker.Color;
        var bg = BgPicker.Color;
        FgSwatch.Fill = new SolidColorBrush(fg);
        BgSwatch.Fill = new SolidColorBrush(bg);
        Preview.Background = new SolidColorBrush(bg);
        SampleLarge.Foreground = SampleSmall.Foreground = new SolidColorBrush(fg);
        Result.Text = Colors.Contrast($"#{fg.R:X2}{fg.G:X2}{fg.B:X2}\n#{bg.R:X2}{bg.G:X2}{bg.B:X2}");
    }
}

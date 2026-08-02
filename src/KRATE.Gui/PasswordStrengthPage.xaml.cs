using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinColor = Windows.UI.Color;

namespace Krate.Gui;

/// <summary>A masked field with a live strength meter, over Core's <see cref="Security.Entropy"/>.</summary>
public sealed partial class PasswordStrengthPage : UserControl
{
    public PasswordStrengthPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_PasswordStrength_Name");
        Update();
    }

    void OnChanged(object sender, RoutedEventArgs e) => Update();

    void Update()
    {
        var pw = Input.Password;
        if (pw.Length == 0) { Meter.Value = 0; Rating.Text = ""; Details.Text = ""; return; }

        var bits = Security.Entropy(pw);
        Meter.Value = Math.Min(bits, 128) / 128 * 100;       // 128 bits = full bar
        var band = Security.Band(bits);
        Meter.Foreground = new SolidColorBrush(BandColor(band));
        Rating.Text = Strings.Get(band);
        Details.Text = Security.Strength(pw);
    }

    static WinColor BandColor(string band) => band switch
    {
        "Pw_VeryWeak" => WinColor.FromArgb(255, 0xE8, 0x11, 0x23), // red
        "Pw_Weak" => WinColor.FromArgb(255, 0xF7, 0x63, 0x0C),     // orange
        "Pw_Reasonable" => WinColor.FromArgb(255, 0xFC, 0xE1, 0x00), // yellow
        "Pw_Strong" => WinColor.FromArgb(255, 0x6C, 0xB4, 0x2C),   // green
        _ => WinColor.FromArgb(255, 0x10, 0x89, 0x3E),            // deep green
    };
}

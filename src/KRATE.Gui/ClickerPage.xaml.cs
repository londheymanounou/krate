using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>A tally counter that survives closing the app — the count lives in the settings file,
/// like the notepad. Nothing computational here, so it stays a GUI-only page.</summary>
public sealed partial class ClickerPage : UserControl
{
    int _count;

    public ClickerPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Clicker_Title");
        PlusButton.Content = "+1";
        MinusButton.Content = "−1";
        ResetButton.Content = Strings.Get("Clicker_Reset");
        _count = int.TryParse(Settings.Get("clicker"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        Render();
    }

    void OnPlus(object sender, RoutedEventArgs e) { _count++; Save(); }
    void OnMinus(object sender, RoutedEventArgs e) { if (_count > 0) _count--; Save(); }
    void OnReset(object sender, RoutedEventArgs e) { _count = 0; Save(); }

    void Save()
    {
        Settings.Set("clicker", _count.ToString(CultureInfo.InvariantCulture));
        Render();
    }

    void Render() => CountText.Text = _count.ToString(CultureInfo.InvariantCulture);
}

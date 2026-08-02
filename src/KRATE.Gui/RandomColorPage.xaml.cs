using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using WinColor = Windows.UI.Color;

namespace Krate.Gui;

/// <summary>A random colour with a big swatch, instead of a text box that ignores its input. The colour
/// (crypto-random) and its notations come from Core's <see cref="Generators.RandomColor"/>.</summary>
public sealed partial class RandomColorPage : UserControl
{
    string _hex = "#000000";

    public RandomColorPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_RandomColor_Name");
        RandomButton.Content = Strings.Get("RandomColor_New");
        ToolTipService.SetToolTip(Swatch, Strings.Get("Gui_Copy"));
        Randomize();
    }

    void OnRandomize(object sender, RoutedEventArgs e) => Randomize();

    void Randomize()
    {
        if (Swatch is null) return;
        var text = Generators.RandomColor("");            // "HEX  #RRGGBB\nRGB ...\nHSL ..."
        _hex = text.Split('\n')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];
        var (r, g, b) = Colors.Parse(_hex);

        Swatch.Background = new SolidColorBrush(WinColor.FromArgb(255, (byte)r, (byte)g, (byte)b));
        var darkText = 0.299 * r + 0.587 * g + 0.114 * b > 140;
        HexText.Foreground = new SolidColorBrush(darkText ? WinColor.FromArgb(255, 0, 0, 0) : WinColor.FromArgb(255, 255, 255, 255));
        HexText.Text = _hex;
        Details.Text = text;
    }

    void OnCopy(object sender, RoutedEventArgs e)
    {
        var data = new DataPackage();
        data.SetText(_hex);
        Clipboard.SetContent(data);
    }
}

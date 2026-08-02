using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using WinColor = Windows.UI.Color;

namespace Krate.Gui;

/// <summary>One page for the colour tools that take a colour and show related colours: pick a colour,
/// see clickable swatches. Two configs (palette harmonies, colour-blindness sim) — the colour maths is
/// all Core's (<see cref="Colors.FromHsl"/>, <see cref="Colors.SimulateProtanopia"/>, …).</summary>
public sealed partial class ColorSwatchesPage : UserControl
{
    readonly Func<(int R, int G, int B), List<(string Label, (int R, int G, int B)[] Colors)>> _build;

    public ColorSwatchesPage(string titleKey, Func<(int R, int G, int B), List<(string, (int, int, int)[])>> build)
    {
        InitializeComponent();
        Title.Text = Strings.Get(titleKey);
        _build = build;
        Picker.Color = WinColor.FromArgb(255, 0x3A, 0x7B, 0xD5);
        Render();
    }

    void OnColorChanged(ColorPicker sender, ColorChangedEventArgs args) => Render();

    void Render()
    {
        if (Rows is null) return; // ColorChanged can fire before the tree is built
        var c = Picker.Color;
        Rows.Children.Clear();
        foreach (var (label, colors) in _build((c.R, c.G, c.B)))
            Rows.Children.Add(Row(label, colors));
    }

    static FrameworkElement Row(string label, (int R, int G, int B)[] colors)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = 0.85 });
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var col in colors) swatches.Children.Add(Swatch(col));
        panel.Children.Add(swatches);
        return panel;
    }

    // A colour swatch that copies its hex when clicked.
    static Button Swatch((int R, int G, int B) c)
    {
        var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        var fill = WinColor.FromArgb(255, (byte)c.R, (byte)c.G, (byte)c.B);
        var darkText = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 140; // light swatch → dark caption
        var caption = new TextBlock
        {
            Text = hex,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = new SolidColorBrush(darkText ? WinColor.FromArgb(255, 0, 0, 0) : WinColor.FromArgb(255, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 6),
        };
        var border = new Border
        {
            Width = 104,
            Height = 68,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(fill),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(WinColor.FromArgb(40, 128, 128, 128)),
            Child = caption,
        };
        var button = new Button
        {
            Content = border,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(WinColor.FromArgb(0, 0, 0, 0)),
        };
        ToolTipService.SetToolTip(button, Strings.Get("Gui_Copy"));
        button.Click += (_, _) =>
        {
            var data = new DataPackage();
            data.SetText(hex);
            Clipboard.SetContent(data);
        };
        return button;
    }

    // ---- The two configurations ----

    public static ColorSwatchesPage Palette() => new("Cp_Title", PaletteRows);
    public static ColorSwatchesPage ColorBlind() => new("Tool_ColorBlind_Name", CvdRows);

    static List<(string, (int, int, int)[])> PaletteRows((int R, int G, int B) c)
    {
        var (h, s, l) = Colors.ToHsl(c);
        (int, int, int) At(double degrees) => Colors.FromHsl(h + degrees, s, l);
        return
        [
            (Strings.Get("Color_Base"), [At(0)]),
            (Strings.Get("Color_Complementary"), [At(180)]),
            (Strings.Get("Color_Triadic"), [At(120), At(240)]),
            (Strings.Get("Color_Analogous"), [At(-30), At(30)]),
            (Strings.Get("Color_SplitComp"), [At(150), At(210)]),
            (Strings.Get("Color_Tetradic"), [At(90), At(180), At(270)]),
        ];
    }

    static List<(string, (int, int, int)[])> CvdRows((int R, int G, int B) c)
    {
        var g = (int)Math.Clamp(Math.Round(0.299 * c.R + 0.587 * c.G + 0.114 * c.B), 0, 255);
        return
        [
            (Strings.Get("Cvd_Normal"), [c]),
            (Strings.Get("Cvd_Protan"), [Colors.SimulateProtanopia(c)]),
            (Strings.Get("Cvd_Deuter"), [Colors.SimulateDeuteranopia(c)]),
            (Strings.Get("Cvd_Tritan"), [Colors.SimulateTritanopia(c)]),
            (Strings.Get("Cvd_Achroma"), [(g, g, g)]),
        ];
    }
}

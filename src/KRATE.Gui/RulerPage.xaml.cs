using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Krate.Gui;

/// <summary>An on-screen ruler with centimetre and inch scales. True physical size depends on the
/// monitor's real pixel density, which Windows only estimates — so there's a calibration slider:
/// hold a bank card to the screen and nudge it until 0–8.56&#160;cm matches the card's long edge.</summary>
// ponytail: 96px/inch is the CSS baseline (right on most displays); the slider is the calibration
// knob a physical measurement always needs — no per-monitor DPI database.
public sealed partial class RulerPage : UserControl
{
    const double BasePxPerCm = 96.0 / 2.54; // logical px per cm at the standard 96 DPI

    public RulerPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Ruler_Title");
        CalibLabel.Text = Strings.Get("Ruler_Calib");
    }

    void OnResize(object sender, SizeChangedEventArgs e) => Draw();
    void OnCalib(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) => Draw();

    SolidColorBrush Ink => (SolidColorBrush)Application.Current.Resources["TextFillColorPrimaryBrush"];
    SolidColorBrush Faint => (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"];

    void Draw()
    {
        if (Board is null || Calib is null) return; // ValueChanged can fire mid-parse, before the tree is built
        Board.Children.Clear();
        double w = Board.ActualWidth, h = Board.ActualHeight;
        if (w < 1 || h < 1) return;

        var cmPx = BasePxPerCm * Calib.Value;
        // Top scale: centimetres (major every cm, mid at 5 mm, minor at 1 mm).
        for (int mm = 0; mm * cmPx / 10 <= w; mm++)
        {
            var x = mm * cmPx / 10;
            var len = mm % 10 == 0 ? 40.0 : mm % 5 == 0 ? 26.0 : 14.0;
            Tick(x, 0, len, down: true, major: mm % 10 == 0);
            if (mm % 10 == 0 && mm > 0) Label(x, len, (mm / 10).ToString(CultureInfo.InvariantCulture), below: true);
        }
        // Bottom scale: inches (major every inch, mid at 1/2, minor at 1/8).
        var inPx = cmPx * 2.54;
        for (int eighth = 0; eighth * inPx / 8 <= w; eighth++)
        {
            var x = eighth * inPx / 8;
            var len = eighth % 8 == 0 ? 40.0 : eighth % 4 == 0 ? 26.0 : 14.0;
            Tick(x, h, len, down: false, major: eighth % 8 == 0);
            if (eighth % 8 == 0 && eighth > 0) Label(x, h - len, (eighth / 8).ToString(CultureInfo.InvariantCulture), below: false);
        }
        Unit("cm", 4, 4, below: true);
        Unit("in", 4, h - 22, below: false);
    }

    void Tick(double x, double y, double len, bool down, bool major)
    {
        Board.Children.Add(new Line
        {
            X1 = x, X2 = x, Y1 = y, Y2 = down ? y + len : y - len,
            Stroke = major ? Ink : Faint, StrokeThickness = major ? 1.4 : 1,
        });
    }

    void Label(double x, double y, string text, bool below)
    {
        var t = new TextBlock { Text = text, FontSize = 12, Foreground = Ink };
        Canvas.SetLeft(t, x + 3);
        Canvas.SetTop(t, below ? y + 2 : y - 16);
        Board.Children.Add(t);
    }

    void Unit(string text, double x, double y, bool below)
    {
        var t = new TextBlock { Text = text, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Faint };
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y);
        Board.Children.Add(t);
    }
}

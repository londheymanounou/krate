using System.Security.Cryptography;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Krate.Gui;

/// <summary>Wheel of fortune: type options, spin, it lands on a crypto-random winner. The wheel is
/// drawn as pie slices on a Canvas — no charting or animation dependency, just a RotateTransform.</summary>
public sealed partial class WheelPage : UserControl
{
    const double Center = 140, Radius = 130;
    static readonly Color[] Palette =
    [
        Color.FromArgb(255, 0x4C, 0x9F, 0x70), Color.FromArgb(255, 0x2E, 0x6D, 0xB4),
        Color.FromArgb(255, 0xE3, 0xB1, 0x1E), Color.FromArgb(255, 0xD1, 0x4A, 0x3F),
        Color.FromArgb(255, 0x7E, 0x57, 0xC2), Color.FromArgb(255, 0x00, 0x96, 0x88),
        Color.FromArgb(255, 0xEF, 0x6C, 0x00), Color.FromArgb(255, 0xC2, 0x18, 0x5B),
    ];

    string[] _entries = [];
    double _rotation;
    bool _spinning;

    public WheelPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Wheel_Title");
        EntriesLabel.Text = Strings.Get("Wheel_EntriesLabel");
        SpinButton.Content = Strings.Get("Wheel_Spin");
        Entries.Text = Strings.Get("Wheel_Sample"); // e.g. "Pizza\nSushi\nTacos\nSalad"
    }

    void OnLoaded(object sender, RoutedEventArgs e) => Draw();

    void OnEntriesChanged(object sender, TextChangedEventArgs e) => Draw();

    void Draw()
    {
        if (Wheel is null) return; // TextChanged can fire mid-parse before the canvas exists
        _entries = Entries.Text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Wheel.Children.Clear();
        SpinButton.IsEnabled = _entries.Length >= 2 && !_spinning;
        if (_entries.Length == 0) return;

        var slice = 360.0 / _entries.Length;
        for (var i = 0; i < _entries.Length; i++)
        {
            Wheel.Children.Add(Wedge(i * slice, (i + 1) * slice, Palette[i % Palette.Length]));
            Wheel.Children.Add(Label(_entries[i], (i + 0.5) * slice));
        }
    }

    // A pie wedge from angle a to angle b (degrees clockwise from the top).
    Path Wedge(double a, double b, Color fill)
    {
        var figure = new PathFigure { StartPoint = new Point(Center, Center), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment { Point = Point(a) });
        figure.Segments.Add(new ArcSegment
        {
            Point = Point(b),
            Size = new Size(Radius, Radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = b - a > 180,
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return new Path
        {
            Data = geometry,
            Fill = new SolidColorBrush(fill),
            Stroke = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            StrokeThickness = 1,
        };
    }

    TextBlock Label(string text, double angle)
    {
        var p = Point(angle, Radius * 0.62);
        var label = new TextBlock
        {
            Text = text.Length > 12 ? text[..12] : text,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };
        Canvas.SetLeft(label, p.X - 34);
        Canvas.SetTop(label, p.Y - 10);
        label.Width = 68;
        label.TextAlignment = TextAlignment.Center;
        return label;
    }

    // Point on the wheel at a given angle (clockwise from top) and radius.
    static Point Point(double angleDeg, double radius = Radius)
    {
        var r = angleDeg * Math.PI / 180;
        return new Point(Center + radius * Math.Sin(r), Center - radius * Math.Cos(r));
    }

    void OnSpin(object sender, RoutedEventArgs e)
    {
        if (_spinning || _entries.Length < 2) return;
        _spinning = true;
        SpinButton.IsEnabled = false;
        Winner.Text = "";

        var winner = RandomNumberGenerator.GetInt32(_entries.Length);
        var slice = 360.0 / _entries.Length;
        var centerAngle = (winner + 0.5) * slice;

        // Land the winning slice under the top pointer, after at least four full turns.
        var needed = (360 - centerAngle % 360) % 360;
        var newAngle = _rotation - _rotation % 360 + needed;
        while (newAngle < _rotation + 1440) newAngle += 360;

        var spin = new DoubleAnimation
        {
            From = _rotation,
            To = newAngle,
            Duration = new Duration(TimeSpan.FromSeconds(3.5)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(spin, Rot);
        Storyboard.SetTargetProperty(spin, "Angle");
        var story = new Storyboard();
        story.Children.Add(spin);
        story.Completed += (_, _) =>
        {
            _rotation = newAngle;
            _spinning = false;
            SpinButton.IsEnabled = true;
            Winner.Text = Strings.Get("Wheel_Winner", _entries[winner]);
        };
        story.Begin();
    }
}

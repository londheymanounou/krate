using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Krate.Gui;

/// <summary>A live wall clock: analog face plus a digital readout and the date. Pure display, so it
/// stays GUI-only — no Core logic, just DateTime.Now on a timer.</summary>
public sealed partial class ClockPage : UserControl
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public ClockPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Clock_Title");
        AddTicks();
        _timer.Tick += OnTick;
        Update();
    }

    void OnLoaded(object sender, RoutedEventArgs e) => _timer.Start();
    void OnUnloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    // The twelve hour marks around the dial.
    void AddTicks()
    {
        for (var i = 0; i < 12; i++)
        {
            var a = i * 30 * Math.PI / 180;
            var size = i % 3 == 0 ? 8.0 : 5.0; // quarters a touch bigger
            var dot = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            };
            Canvas.SetLeft(dot, 120 + 104 * Math.Sin(a) - size / 2);
            Canvas.SetTop(dot, 120 - 104 * Math.Cos(a) - size / 2);
            Face.Children.Add(dot);
        }
    }

    void OnTick(object? sender, object e) => Update();

    void Update()
    {
        var now = DateTime.Now;
        var sec = now.Second + now.Millisecond / 1000.0;
        var min = now.Minute + sec / 60.0;
        var hour = now.Hour % 12 + min / 60.0;
        SecondRot.Angle = sec * 6;
        MinuteRot.Angle = min * 6;
        HourRot.Angle = hour * 30;

        Digital.Text = now.ToString("HH:mm:ss");
        DateText.Text = now.ToString("dddd, dd MMMM yyyy", Strings.Culture);
    }
}

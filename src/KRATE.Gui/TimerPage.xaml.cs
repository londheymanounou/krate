using System.Diagnostics;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Stopwatch and countdown (25/5 = pomodoro) in one page. A monotonic Stopwatch drives
/// the maths so the display stays accurate even when a timer tick is late.</summary>
public sealed partial class TimerPage : UserControl
{
    readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly Stopwatch _clock = new();
    TimeSpan _target = TimeSpan.Zero; // Zero = counting up (stopwatch); otherwise counting down
    TimeSpan _carried = TimeSpan.Zero; // elapsed accumulated across pauses

    public TimerPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Timer_Title");
        StopwatchButton.Content = Strings.Get("Timer_Stopwatch");
        Pomodoro25.Content = Strings.Get("Timer_Focus");
        Pomodoro5.Content = Strings.Get("Timer_Break");
        Preset10.Content = "10 min";
        ResetButton.Content = Strings.Get("Timer_Reset");
        _ticker.Tick += (_, _) => Update();
        UpdateStartButton();
        Render(TimeSpan.Zero);
    }

    void OnStartStop(object sender, RoutedEventArgs e)
    {
        if (_clock.IsRunning) { _carried += _clock.Elapsed; _clock.Reset(); _ticker.Stop(); }
        else { _clock.Restart(); _ticker.Start(); }
        UpdateStartButton();
    }

    void OnReset(object sender, RoutedEventArgs e)
    {
        _clock.Reset();
        _ticker.Stop();
        _carried = TimeSpan.Zero;
        Render(_target);
        UpdateStartButton();
    }

    void OnStopwatch(object sender, RoutedEventArgs e) => SetMode(TimeSpan.Zero);
    void OnPreset(object sender, RoutedEventArgs e) => SetMode(TimeSpan.FromMinutes(int.Parse((string)((Button)sender).Tag)));

    void SetMode(TimeSpan target)
    {
        _target = target;
        _clock.Reset();
        _ticker.Stop();
        _carried = TimeSpan.Zero;
        Render(target);
        UpdateStartButton();
    }

    void Update()
    {
        var elapsed = _carried + _clock.Elapsed;
        if (_target == TimeSpan.Zero) { Render(elapsed); return; } // stopwatch

        var remaining = _target - elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            _clock.Reset(); _ticker.Stop(); _carried = TimeSpan.Zero;
            Render(TimeSpan.Zero);
            Flash();
            UpdateStartButton();
            return;
        }
        Render(remaining);
    }

    // No audio dependency: signal the end by flashing the display instead of beeping.
    void Flash()
    {
        var original = Display.Opacity;
        var blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        var count = 0;
        blink.Tick += (_, _) =>
        {
            Display.Opacity = Display.Opacity < 1 ? 1 : 0.2;
            if (++count >= 6) { blink.Stop(); Display.Opacity = original; }
        };
        blink.Start();
    }

    void Render(TimeSpan t) =>
        // Tenths for a stopwatch, whole seconds for a countdown — no phantom precision on a 100 ms tick.
        Display.Text = _target == TimeSpan.Zero
            ? $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}"
            : $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";

    void UpdateStartButton() =>
        StartButton.Content = Strings.Get(_clock.IsRunning ? "Timer_Pause" : "Timer_Start");
}

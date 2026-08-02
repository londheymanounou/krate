using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>World-clock view of one time across cities: pick a time and a source city, every zone
/// updates live. All zone maths is Core's (<see cref="Dates.InstantFrom"/> / <see cref="Dates.InZone"/>).</summary>
public sealed partial class TimezonePage : UserControl
{
    public TimezonePage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Timezone_Name");
        InLabel.Text = Strings.Get("Tz_In");
        NowButton.Content = Strings.Get("Tz_Now");
        foreach (var (label, _) in Dates.CommonZones) Source.Items.Add(label);
        Source.SelectedIndex = Dates.CommonZones.ToList().FindIndex(z => z.Id == "Europe/Paris");
        Time.SelectedTime = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, TimeZoneInfo.Local).TimeOfDay;
        Refresh();
    }

    void OnChanged(object sender, object e) => Refresh();

    void OnNow(object sender, RoutedEventArgs e)
    {
        Time.SelectedTime = DateTimeOffset.Now.TimeOfDay;
        Refresh();
    }

    void Refresh()
    {
        if (Results is null || Source.SelectedIndex < 0) return; // handlers can fire before the tree is built
        Results.Children.Clear();
        try
        {
            var t = Time.SelectedTime ?? DateTimeOffset.Now.TimeOfDay;
            var sourceId = Dates.CommonZones[Source.SelectedIndex].Id;
            var instant = Dates.InstantFrom(t.Hours, t.Minutes, sourceId);

            foreach (var (label, id) in Dates.CommonZones)
            {
                var there = Dates.InZone(instant, id); // TimeZoneInfo lookups can throw on a system missing a zone
                var isSource = id == sourceId;
                Results.Children.Add(new TextBlock
                {
                    Text = string.Create(CultureInfo.InvariantCulture, $"{label,-14} {there:ddd HH:mm}  {there:zzz}"),
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontWeight = isSource ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                    Opacity = isSource ? 1 : 0.85,
                });
            }
        }
        catch (Exception ex) { Results.Children.Add(new TextBlock { Text = ex.Message, Opacity = 0.7 }); }
    }
}

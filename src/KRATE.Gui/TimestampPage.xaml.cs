using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Two-way Unix timestamp ↔ date, with a "Now" button. Both directions format via Core's
/// <see cref="Dates.Timestamp"/>.</summary>
public sealed partial class TimestampPage : UserControl
{
    public TimestampPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Timestamp_Name");
        FromUnixLabel.Text = Strings.Get("Ts_FromUnix");
        FromDateLabel.Text = Strings.Get("Ts_FromDate");
        NowButton.Content = Strings.Get("Ts_Now");
        Date.Date = DateTimeOffset.Now;
        Time.SelectedTime = DateTime.Now.TimeOfDay;
        UnixToDate();
        DateToUnix();
    }

    void OnUnixChanged(NumberBox sender, NumberBoxValueChangedEventArgs e) => UnixToDate();
    void OnDateChanged(object sender, object e) => DateToUnix();

    void OnNow(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Unix.Value = DateTimeOffset.Now.ToUnixTimeSeconds();
        Date.Date = DateTimeOffset.Now;
        Time.SelectedTime = DateTime.Now.TimeOfDay;
    }

    void UnixToDate()
    {
        if (FromUnix is null) return; // Unix's ValueChanged fires mid-parse, before FromUnix exists
        if (double.IsNaN(Unix.Value)) { FromUnix.Text = ""; return; }
        try { FromUnix.Text = Dates.Timestamp(((long)Unix.Value).ToString(CultureInfo.InvariantCulture)); }
        catch (Exception ex) { FromUnix.Text = ex.Message; }
    }

    void DateToUnix()
    {
        if (Date.Date is not { } d) { FromDate.Text = ""; return; }
        var time = Time.SelectedTime ?? TimeSpan.Zero;
        var local = new DateTimeOffset(d.Year, d.Month, d.Day, time.Hours, time.Minutes, time.Seconds, d.Offset);
        try { FromDate.Text = Dates.Timestamp(local.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)); }
        catch (Exception ex) { FromDate.Text = ex.Message; }
    }
}

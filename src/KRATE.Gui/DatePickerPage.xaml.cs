using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Reusable "pick a date → run a Core tool" page — a real calendar instead of typing a date
/// into a text box. Configured per tool at construction.</summary>
public sealed partial class DatePickerPage : UserControl
{
    readonly Func<string, string> _run;

    public DatePickerPage(string titleKey, Func<string, string> run)
    {
        InitializeComponent();
        _run = run;
        Title.Text = Strings.Get(titleKey);
        Date.Date = DateTimeOffset.Now;
        Update();
    }

    void OnChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs e) => Update();

    void Update()
    {
        var iso = (Date.Date ?? DateTimeOffset.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        try { Result.Text = _run(iso); }
        catch (Exception ex) { Result.Text = ex.Message; }
    }
}

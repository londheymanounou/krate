using Krate.Core;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Date difference with two native calendar pickers instead of typing dates. Maths is
/// Core's <see cref="Dates.Difference"/>.</summary>
public sealed partial class DateDiffPage : UserControl
{
    public DateDiffPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_DateDiff_Name");
        From.Header = Strings.Get("Date_From");
        To.Header = Strings.Get("Date_To");
        From.Date = DateTimeOffset.Now.AddYears(-1);
        To.Date = DateTimeOffset.Now;
        Update();
    }

    void OnChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => Update();

    void Update()
    {
        if (From.Date is not { } a || To.Date is not { } b) { Result.Text = ""; return; }
        try { Result.Text = Dates.Difference($"{a:yyyy-MM-dd} {b:yyyy-MM-dd}"); }
        catch (Exception ex) { Result.Text = ex.Message; }
    }
}

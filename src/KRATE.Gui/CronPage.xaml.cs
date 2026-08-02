using Krate.Core;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Cron expression → plain-English description plus the next fire times (Core's
/// <see cref="Cron.Describe"/> and <see cref="Cron.NextRuns"/>).</summary>
public sealed partial class CronPage : UserControl
{
    public CronPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Cron_Name");
        NextLabel.Text = Strings.Get("Cron_NextRuns");
        Expr.Text = "0 9 * * 1-5";
    }

    void OnChanged(object sender, TextChangedEventArgs e)
    {
        if (Expr.Text.Trim().Length == 0) { Description.Text = ""; NextRuns.Text = ""; return; }
        try
        {
            Description.Text = Cron.Describe(Expr.Text);
            var runs = Cron.NextRuns(Expr.Text, 5, DateTime.Now);
            NextRuns.Text = string.Join('\n', runs.Select(r => r.ToString("ddd  yyyy-MM-dd  HH:mm", Strings.Culture)));
        }
        catch (Exception ex) { Description.Text = ex.Message; NextRuns.Text = ""; }
    }
}

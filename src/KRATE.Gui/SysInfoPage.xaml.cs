using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>System info as a read-only panel — no input box, since the tool ignores input anyway.
/// The facts come from Core's <see cref="Everyday.SysInfo"/>.</summary>
public sealed partial class SysInfoPage : UserControl
{
    public SysInfoPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_SysInfo_Name");
        RefreshButton.Content = Strings.Get("SysInfo_Refresh");
        Refresh();
    }

    void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

    void Refresh()
    {
        // DriveInfo can throw on a flaky drive; never let a stats read crash the page.
        try { Info.Text = Everyday.SysInfo(""); }
        catch (Exception ex) { Info.Text = ex.Message; }
    }
}

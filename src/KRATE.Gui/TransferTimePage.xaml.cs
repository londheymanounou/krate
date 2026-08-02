using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>File-transfer time: a size and a bandwidth, with unit dropdowns that keep the bits-vs-bytes
/// distinction explicit. The maths is Core's <see cref="Transfer.Time"/>.</summary>
public sealed partial class TransferTimePage : UserControl
{
    static readonly string[] SizeUnits = ["GB", "MB", "KB", "GiB", "MiB", "B"];
    static readonly string[] RateUnits = ["Mbps", "Gbps", "Kbps", "MB/s", "MiB/s", "GB/s"];

    public TransferTimePage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_TransferTime_Name");
        SizeLabel.Text = Strings.Get("Transfer_Size");
        RateLabel.Text = Strings.Get("Transfer_Rate");
        foreach (var u in SizeUnits) SizeUnit.Items.Add(u);
        foreach (var u in RateUnits) RateUnit.Items.Add(u);
        SizeUnit.SelectedIndex = 0;
        RateUnit.SelectedIndex = 0;
        SizeValue.Value = 5;
        RateValue.Value = 100;
        Convert();
    }

    void OnChanged(object sender, object e) => Convert();

    void Convert()
    {
        if (Result is null || SizeUnit.SelectedIndex < 0 || RateUnit.SelectedIndex < 0) return;
        var size = double.IsNaN(SizeValue.Value) ? 0 : SizeValue.Value;
        var rate = double.IsNaN(RateValue.Value) ? 0 : RateValue.Value;
        var input = $"{size.ToString(CultureInfo.InvariantCulture)}{SizeUnits[SizeUnit.SelectedIndex]} " +
                    $"{rate.ToString(CultureInfo.InvariantCulture)}{RateUnits[RateUnit.SelectedIndex]}";
        try { Result.Text = Transfer.Time(input); }
        catch (Exception ex) { Result.Text = ex.Message; }
    }
}

using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Amount + two currency dropdowns. The rate fetch (Core's <see cref="Currency.Convert"/>)
/// runs off the UI thread; results are cached, so switching amount/target is instant.</summary>
public sealed partial class CurrencyPage : UserControl
{
    bool _loading = true;

    public CurrencyPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Currency_Name");
        foreach (var code in Currency.CommonCodes) { From.Items.Add(code); To.Items.Add(code); }
        From.SelectedItem = "USD";
        To.SelectedItem = "EUR";
        _loading = false;
        Convert();
    }

    void OnChanged(object sender, object e) { if (!_loading) Convert(); }

    void OnSwap(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        (From.SelectedItem, To.SelectedItem) = (To.SelectedItem, From.SelectedItem);
    }

    async void Convert()
    {
        if (From.SelectedItem is not string from || To.SelectedItem is not string to || double.IsNaN(Amount.Value)) return;
        var input = $"{Amount.Value.ToString(CultureInfo.InvariantCulture)} {from} {to}";
        Result.Text = "…";
        Detail.Text = "";
        try
        {
            var text = await Task.Run(() => Currency.Convert(input));
            var lines = text.Split('\n');
            Result.Text = lines[0];
            Detail.Text = string.Join('\n', lines.Skip(1));
        }
        catch (Exception ex) { Result.Text = ex.Message; }
    }
}

using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Krate.Gui;

/// <summary>UUID generator: pick how many and Generate, with a Copy button (Core's
/// <see cref="Generators.Uuid"/>).</summary>
public sealed partial class UuidPage : UserControl
{
    public UuidPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Uuid_Name");
        Count.Header = Strings.Get("Dice_Count");
        GenerateButton.Content = Strings.Get("Rnd_Generate");
        CopyButton.Content = Strings.Get("Gui_Copy");
        Generate();
    }

    void OnGenerate(object sender, RoutedEventArgs e) => Generate();

    void Generate()
    {
        var count = (int)(double.IsNaN(Count.Value) ? 1 : Count.Value);
        Result.Text = Generators.Uuid(count.ToString(CultureInfo.InvariantCulture));
    }

    void OnCopy(object sender, RoutedEventArgs e)
    {
        if (Result.Text.Length == 0) return;
        var data = new DataPackage();
        data.SetText(Result.Text);
        Clipboard.SetContent(data);
    }
}

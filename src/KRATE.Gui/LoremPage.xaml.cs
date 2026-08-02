using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Krate.Gui;

/// <summary>Lorem Ipsum with a length slider and a words/paragraphs switch — over Core's <see cref="Text.Lorem"/>.</summary>
public sealed partial class LoremPage : UserControl
{
    public LoremPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Lorem_Name");
        Mode.OffContent = Strings.Get("Lorem_Words");
        Mode.OnContent = Strings.Get("Lorem_Paragraphs");
        CopyButton.Content = Strings.Get("Gui_Copy");
        Generate();
    }

    void OnChanged(object sender, object e) => Generate();

    void Generate()
    {
        if (Output is null) return;
        var count = (int)Count.Value;
        var paragraphs = Mode.IsOn;
        CountLabel.Text = Strings.Get(paragraphs ? "Lorem_NPara" : "Lorem_NWords", count);
        try { Output.Text = Text.Lorem(paragraphs ? $"{count}p" : $"{count}"); }
        catch (Exception ex) { Output.Text = ex.Message; }
    }

    void OnCopy(object sender, RoutedEventArgs e)
    {
        var data = new DataPackage();
        data.SetText(Output.Text);
        Clipboard.SetContent(data);
    }
}

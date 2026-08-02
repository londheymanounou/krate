using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Krate.Gui;

/// <summary>A real control-driven tool: length slider, character-class toggles, live regeneration.
/// The generation and strength maths live in Core; this page is only knobs and display.</summary>
public sealed partial class PasswordPage : UserControl
{
    public PasswordPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Password_Name");
        LengthLabel.Text = Strings.Get("Pwd_Length");
        Upper.Header = Strings.Get("Pwd_Upper");
        Lower.Header = Strings.Get("Pwd_Lower");
        Digits.Header = Strings.Get("Pwd_Digits");
        Symbols.Header = Strings.Get("Pwd_Symbols");
        Regenerate.Content = Strings.Get("Pwd_Regenerate");
        CopyButton.Content = Strings.Get("Gui_Copy");
        Generate();
    }

    void OnChanged(object sender, object e) => Generate();

    void Generate()
    {
        if (StrengthText is null) return; // toggles/slider fire mid-parse; StrengthText is the last-declared element touched
        LengthValue.Text = ((int)Length.Value).ToString();
        try
        {
            Output.Text = Generators.Password((int)Length.Value, Upper.IsOn, Lower.IsOn, Digits.IsOn, Symbols.IsOn);
            StrengthText.Text = Security.Strength(Output.Text).Replace("\n", "   ");
        }
        catch (Exception ex) { Output.Text = ""; StrengthText.Text = ex.Message; }
    }

    void OnCopy(object sender, RoutedEventArgs e)
    {
        if (Output.Text.Length == 0) return;
        var data = new DataPackage();
        data.SetText(Output.Text);
        Clipboard.SetContent(data);
    }
}

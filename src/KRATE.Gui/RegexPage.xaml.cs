using Krate.Core;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Regex tester with a pattern field, flag toggles and a test string — live matches via Core's
/// <see cref="Dev.RegexTest"/>. Builds the /pattern/flags form the tested engine already understands.</summary>
public sealed partial class RegexPage : UserControl
{
    public RegexPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Regex_Name");
    }

    void OnChanged(object sender, object e)
    {
        MatchCount.Text = "";
        if (Pattern.Text.Length == 0) { Result.Text = ""; return; }

        var flags = (IgnoreCase.IsChecked == true ? "i" : "") + (Multiline.IsChecked == true ? "m" : "") + (Singleline.IsChecked == true ? "s" : "");
        var pattern = flags.Length > 0 ? $"/{Pattern.Text}/{flags}" : Pattern.Text;

        try
        {
            var output = Dev.RegexTest($"{pattern}\n{Subject.Text}");
            // The first line of the tested output is either the count or "No match."
            var lines = output.Split('\n');
            MatchCount.Text = lines[0];
            Result.Text = lines.Length > 1 ? string.Join('\n', lines.Skip(1)) : "";
        }
        catch (Exception ex) { Result.Text = ex.Message; }
    }
}

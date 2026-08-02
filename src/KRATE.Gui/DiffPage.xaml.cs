using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinColor = Windows.UI.Color;

namespace Krate.Gui;

/// <summary>Two panes with a colour-coded line diff below (Core's <see cref="Text.Diff"/>): removed
/// lines red, added lines green, unchanged lines dimmed.</summary>
public sealed partial class DiffPage : UserControl
{
    static readonly SolidColorBrush Added = new(WinColor.FromArgb(255, 0x4C, 0xAF, 0x50));
    static readonly SolidColorBrush Removed = new(WinColor.FromArgb(255, 0xE5, 0x73, 0x73));

    public DiffPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Diff_Name");
    }

    void OnChanged(object sender, TextChangedEventArgs e)
    {
        Result.Children.Clear();
        if (A.Text.Length == 0 && B.Text.Length == 0) return;

        string output;
        try { output = Text.Diff($"{A.Text}\n---\n{B.Text}"); }
        catch (Exception ex) { output = ex.Message; }

        foreach (var line in output.Split('\n'))
        {
            var block = new TextBlock { Text = line, FontFamily = new FontFamily("Consolas"), FontSize = 13, TextWrapping = TextWrapping.NoWrap };
            if (line.StartsWith("+ ")) block.Foreground = Added;
            else if (line.StartsWith("- ")) block.Foreground = Removed;
            else block.Opacity = 0.6;
            Result.Children.Add(block);
        }
    }
}

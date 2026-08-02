using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Windows-Calculator-style multi-mode calculator (Standard / Scientific / Programmer) with
/// history and memory, over Core's <see cref="StandardCalculator"/> and <see cref="ProgrammerCalculator"/>
/// engines. Faithful to that app's behaviour; not its C++/UWP source.</summary>
public sealed partial class CalculatorPage : UserControl
{
    readonly StandardCalculator _calc = new();
    readonly ProgrammerCalculator _prog = new();
    bool _programmer;

    public CalculatorPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Calc_Name");
        HistoryLabel.Text = Strings.Get("Calc_History");
        HistoryEmpty.Text = Strings.Get("Calc_HistoryEmpty");
        Mode.Items.Add(Strings.Get("Calc_Standard"));
        Mode.Items.Add(Strings.Get("Calc_Scientific"));
        Mode.Items.Add(Strings.Get("Calc_Programmer"));
        Mode.SelectedIndex = 0;
        RefreshHistory();
    }

    void OnMode(object sender, SelectionChangedEventArgs e)
    {
        _programmer = Mode.SelectedIndex == 2;
        var scientific = Mode.SelectedIndex == 1;

        SciPad.Visibility = scientific ? Visibility.Visible : Visibility.Collapsed;
        StdPad.Visibility = _programmer ? Visibility.Collapsed : Visibility.Visible;
        ProgPad.Visibility = _programmer ? Visibility.Visible : Visibility.Collapsed;
        Bases.Visibility = _programmer ? Visibility.Visible : Visibility.Collapsed;
        Expr.Visibility = _programmer ? Visibility.Collapsed : Visibility.Visible;

        Show();
    }

    void OnKey(object sender, RoutedEventArgs e)
    {
        var tag = (string)((Button)sender).Tag;
        if (_programmer) _prog.Input(tag);
        else { _calc.Input(tag); if (tag == "=") RefreshHistory(); }
        Show();
    }

    void OnBase(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string t }) _prog.SetBase(int.Parse(t));
        Show();
    }

    void OnAngle(object sender, RoutedEventArgs e)
    {
        _calc.AngleMode = (StandardCalculator.Angle)(((int)_calc.AngleMode + 1) % 3);
        AngleButton.Content = _calc.AngleMode.ToString().ToUpperInvariant();
    }

    bool _second;

    // "2nd" flips the trig keys to their inverse, like Windows Calculator.
    void OnSecond(object sender, RoutedEventArgs e)
    {
        _second = !_second;
        SecondButton.Background = _second ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] : null;
        SinButton.Tag = _second ? "asin" : "sin"; SinButton.Content = _second ? "sin⁻¹" : "sin";
        CosButton.Tag = _second ? "acos" : "cos"; CosButton.Content = _second ? "cos⁻¹" : "cos";
        TanButton.Tag = _second ? "atan" : "tan"; TanButton.Content = _second ? "tan⁻¹" : "tan";
    }

    void Show()
    {
        if (_programmer)
        {
            Display.Text = _prog.Display;
            BaseReadout.Text = $"HEX {_prog.Hex}\nDEC {_prog.Dec}\nOCT {_prog.Oct}\nBIN {_prog.Bin}";
        }
        else
        {
            Display.Text = _calc.Display;
            Expr.Text = _calc.Expression;
            MemoryTag.Text = _calc.Memory != 0 ? "M" : "";
        }
    }

    void RefreshHistory()
    {
        History.Items.Clear();
        foreach (var line in ((IEnumerable<string>)_calc.History).Reverse())
            History.Items.Add(line);
        HistoryEmpty.Visibility = _calc.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}

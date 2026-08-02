using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Roman ↔ Arabic both ways at once: a number spinner up top, a text box below. Both call
/// Core's <see cref="Units.Roman"/>, which detects the direction from the input.</summary>
public sealed partial class RomanPage : UserControl
{
    public RomanPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Roman_Name");
        ToRomanLabel.Text = Strings.Get("Roman_ToRoman");
        ToArabicLabel.Text = Strings.Get("Roman_ToArabic");
        RomanIn.Text = "MMXXIV";
        OnArabic(null!, null!);
    }

    void OnArabic(object sender, object e)
    {
        if (RomanOut is null) return; // ValueChanged fires before RomanOut exists
        try { RomanOut.Text = double.IsNaN(Arabic.Value) ? "" : Units.Roman(((long)Arabic.Value).ToString(CultureInfo.InvariantCulture)); }
        catch (Exception ex) { RomanOut.Text = ex.Message; }
    }

    void OnRoman(object sender, TextChangedEventArgs e)
    {
        if (ArabicOut is null) return;
        var s = RomanIn.Text.Trim();
        if (s.Length == 0) { ArabicOut.Text = ""; return; }
        try { ArabicOut.Text = Units.Roman(s); }
        catch (Exception ex) { ArabicOut.Text = ex.Message; }
    }
}

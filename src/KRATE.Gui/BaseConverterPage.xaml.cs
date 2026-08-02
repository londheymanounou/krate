using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Type a number in any base; the other three update live. Editing one field drives the rest.</summary>
public sealed partial class BaseConverterPage : UserControl
{
    bool _updating;

    public BaseConverterPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Bases_Name");
        Subtitle.Text = Strings.Get("Tool_Bases_Desc");
        _updating = true;
        Dec.Text = "255";
        _updating = false;
        Sync(Dec, 10);
    }

    void OnChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        var box = (TextBox)sender;
        var radix = box == Hex ? 16 : box == Dec ? 10 : box == Oct ? 8 : 2;
        Sync(box, radix);
    }

    void Sync(TextBox source, int radix)
    {
        var clean = source.Text.Trim().Replace("_", "").Replace(" ", "");
        if (clean.Length == 0) { SetAll(source, "", "", "", ""); Error.Text = ""; return; }

        long value;
        try { value = Convert.ToInt64(clean, radix); }
        catch { Error.Text = Strings.Get("Base_Invalid"); return; }
        Error.Text = "";

        SetAll(source,
            Convert.ToString(value, 16).ToUpperInvariant(),
            value.ToString(CultureInfo.InvariantCulture),
            Convert.ToString(value, 8),
            Convert.ToString(value, 2));
    }

    // Update every field except the one being edited, so the caret doesn't jump.
    void SetAll(TextBox source, string hex, string dec, string oct, string bin)
    {
        _updating = true;
        if (source != Hex) Hex.Text = hex;
        if (source != Dec) Dec.Text = dec;
        if (source != Oct) Oct.Text = oct;
        if (source != Bin) Bin.Text = bin;
        _updating = false;
    }
}

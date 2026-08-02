using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Number → words, with a language dropdown (Core's <see cref="Words.Spell"/>).</summary>
public sealed partial class SpellPage : UserControl
{
    static readonly (string Name, string Code)[] Languages = [("English", "en"), ("Français", "fr")];

    public SpellPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Spell_Name");
        foreach (var (name, _) in Languages) LangBox.Items.Add(name);
        LangBox.SelectedIndex = 0;
    }

    void OnChanged(object sender, object e)
    {
        if (Output is null || LangBox.SelectedIndex < 0) return;
        var n = double.IsNaN(Number.Value) ? 0 : (long)Number.Value;
        try { Output.Text = Words.Spell($"{n.ToString(CultureInfo.InvariantCulture)} {Languages[LangBox.SelectedIndex].Code}"); }
        catch (Exception ex) { Output.Text = ex.Message; }
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>A Settings-style card (icon, header, description, a control on the right) — the PowerToys
/// look, hand-rolled so no toolkit dependency is needed.</summary>
public sealed partial class SettingCard : UserControl
{
    public SettingCard() => InitializeComponent();

    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingCard), new PropertyMetadata(""));

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingCard), new PropertyMetadata(""));

    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingCard), new PropertyMetadata("",
            (d, _) => ((SettingCard)d).Bindings.Update()));

    public object? Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }
    public static readonly DependencyProperty ActionProperty =
        DependencyProperty.Register(nameof(Action), typeof(object), typeof(SettingCard), new PropertyMetadata(null));

    // Hide the description line when there isn't one, so a title-only card stays tight.
    public Visibility HasDescription => string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;
}

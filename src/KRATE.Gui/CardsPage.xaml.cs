using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Krate.Gui;

/// <summary>A drawn card model for the ItemsRepeater template: rank, suit glyph, and the ink colour.</summary>
public sealed class CardVm
{
    public string Rank { get; init; } = "";
    public string Suit { get; init; } = "";
    public Brush Color { get; init; } = null!;
}

/// <summary>Draws real playing cards instead of a text list. The draw itself is Core's crypto-backed
/// <see cref="Generators.Cards"/>; this page only turns each "6♠" token into a card face.</summary>
public sealed partial class CardsPage : UserControl
{
    static readonly SolidColorBrush Red = new(Windows.UI.Color.FromArgb(255, 0xC8, 0x10, 0x1A));
    static readonly SolidColorBrush Black = new(Windows.UI.Color.FromArgb(255, 0x1A, 0x1A, 0x1A));

    public CardsPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Cards_Name");
        CountLabel.Text = Strings.Get("Cards_Count");
        DealButton.Content = Strings.Get("Cards_Deal");
        Deal();
    }

    void OnCount(NumberBox sender, NumberBoxValueChangedEventArgs e) => Deal();
    void OnDeal(object sender, RoutedEventArgs e) => Deal();

    void Deal()
    {
        if (Hand is null) return; // ValueChanged fires before the tree is built
        var count = double.IsNaN(Count.Value) ? 1 : (int)Count.Value;
        var tokens = Generators.Cards(count.ToString(CultureInfo.InvariantCulture)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Hand.ItemsSource = tokens.Select(ToCard).ToList();
    }

    static CardVm ToCard(string token)
    {
        var suit = token[^1..];
        return new CardVm { Rank = token[..^1], Suit = suit, Color = suit is "♥" or "♦" ? Red : Black };
    }
}

using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Krate.Gui;

/// <summary>A coin you actually flip: a gold coin that spins on flip (crypto-backed result from Core's
/// <see cref="Generators.Coin"/>), with a running heads/tails tally.</summary>
public sealed partial class CoinPage : UserControl
{
    readonly string _heads = Strings.Get("Random_Heads");
    int _headCount, _tailCount;

    public CoinPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Coin_Name");
        FlipButton.Content = Strings.Get("Rnd_Flip");
        Face.Text = _heads;
        UpdateTally();
    }

    void OnFlip(object sender, RoutedEventArgs e)
    {
        var result = Generators.Coin("");
        if (result == _heads) _headCount++; else _tailCount++;
        Face.Text = result;
        UpdateTally();

        // A quick 3D vertical spin — two full turns so it lands upright on the new face.
        var spin = new DoubleAnimation
        {
            From = 0,
            To = 720,
            Duration = new Duration(TimeSpan.FromMilliseconds(650)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(spin, Flip);
        Storyboard.SetTargetProperty(spin, "RotationX");
        var story = new Storyboard();
        story.Children.Add(spin);
        story.Begin();
    }

    void UpdateTally() => Tally.Text = Strings.Get("Coin_Tally", _headCount, _tailCount);
}

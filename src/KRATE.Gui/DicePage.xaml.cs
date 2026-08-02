using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Krate.Gui;

/// <summary>Dice you roll and see: pick a die type and how many, and each die shows real pips (d6) or its
/// number, with a brief shuffle before it settles. Rolls come from Core's crypto-backed
/// <see cref="Generators.Roll"/>.</summary>
public sealed partial class DicePage : UserControl
{
    static readonly int[] Types = [4, 6, 8, 10, 12, 20];
    // Pip positions (row,col in a 3×3 grid) for each face of a d6.
    static readonly Dictionary<int, (int R, int C)[]> Pips = new()
    {
        [1] = [(1, 1)],
        [2] = [(0, 0), (2, 2)],
        [3] = [(0, 0), (1, 1), (2, 2)],
        [4] = [(0, 0), (0, 2), (2, 0), (2, 2)],
        [5] = [(0, 0), (0, 2), (1, 1), (2, 0), (2, 2)],
        [6] = [(0, 0), (0, 2), (1, 0), (1, 2), (2, 0), (2, 2)],
    };

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    int _shuffles;
    int[] _final = [];

    public DicePage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Dice_Name");
        RollButton.Content = Strings.Get("Rnd_Roll");
        DieType.Header = Strings.Get("Dice_Type");
        Count.Header = Strings.Get("Dice_Count");
        foreach (var t in Types) DieType.Items.Add($"d{t}");
        DieType.SelectedIndex = 1; // d6
        _timer.Tick += OnShuffle;
        Render(Generators.Roll(1, 6));
    }

    int Faces => Types[Math.Max(0, DieType.SelectedIndex)];

    void OnRoll(object sender, RoutedEventArgs e)
    {
        if (_timer.IsEnabled) return;
        var count = double.IsNaN(Count.Value) ? 1 : (int)Count.Value;
        _final = Generators.Roll(count, Faces);
        _shuffles = 0;
        Total.Text = "";
        RollButton.IsEnabled = false;
        _timer.Start();
    }

    void OnShuffle(object? sender, object e)
    {
        if (_shuffles++ < 8) { Render(Generators.Roll(_final.Length, Faces)); return; }
        _timer.Stop();
        Render(_final);
        Total.Text = Strings.Get("Dice_Total", _final.Sum());
        RollButton.IsEnabled = true;
    }

    void Render(int[] values)
    {
        DicePanel.Children.Clear();
        DicePanel.ColumnDefinitions.Clear();
        DicePanel.RowDefinitions.Clear();
        var cols = Math.Min(values.Length, 4);
        for (var c = 0; c < cols; c++) DicePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < values.Length; i++)
        {
            int row = i / cols, col = i % cols;
            while (DicePanel.RowDefinitions.Count <= row) DicePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var die = Die(values[i]);
            Grid.SetRow(die, row);
            Grid.SetColumn(die, col);
            DicePanel.Children.Add(die);
        }
    }

    Border Die(int value)
    {
        var ink = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        var die = new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        };

        if (Faces == 6 && Pips.TryGetValue(value, out var spots))
        {
            var grid = new Grid { Padding = new Thickness(10) };
            for (var i = 0; i < 3; i++) { grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.RowDefinitions.Add(new RowDefinition()); }
            foreach (var (r, c) in spots)
            {
                var pip = new Ellipse { Width = 11, Height = 11, Fill = ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(pip, r);
                Grid.SetColumn(pip, c);
                grid.Children.Add(pip);
            }
            die.Child = grid;
        }
        else
        {
            die.Child = new TextBlock { Text = value.ToString(), FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        return die;
    }
}

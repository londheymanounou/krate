using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.System;
using Krate.Core.Games;
using Windows.UI;

namespace Krate.Gui.Games;

public sealed partial class Game2048Page : UserControl
{
    private Game2048 _game;

    public Game2048Page()
    {
        this.InitializeComponent();
        
        // Setup Grid
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                var bg = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(255, 205, 193, 180)),
                    CornerRadius = new CornerRadius(4)
                };
                Grid.SetColumn(bg, x);
                Grid.SetRow(bg, y);
                GameGrid.Children.Add(bg);
            }
        }

        _game = new Game2048();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        this.Focus(FocusState.Programmatic);
        StartGame();
    }

    private void OnRestart(object sender, RoutedEventArgs e)
    {
        this.Focus(FocusState.Programmatic);
        StartGame();
    }

    private void StartGame()
    {
        GameOverOverlay.Visibility = Visibility.Collapsed;
        _game.Start();
        Draw();
    }

    private void Draw()
    {
        // Remove old tiles (keep backgrounds)
        for (int i = GameGrid.Children.Count - 1; i >= 16; i--)
        {
            GameGrid.Children.RemoveAt(i);
        }

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int val = _game.Board[x, y];
                if (val == 0) continue;

                var tile = new Border
                {
                    Background = GetColor(val),
                    CornerRadius = new CornerRadius(4),
                    Child = new TextBlock
                    {
                        Text = val.ToString(),
                        Foreground = new SolidColorBrush(val <= 4 ? Color.FromArgb(255, 119, 110, 101) : Colors.White),
                        FontSize = val > 512 ? 24 : 32,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

                Grid.SetColumn(tile, x);
                Grid.SetRow(tile, y);
                GameGrid.Children.Add(tile);
            }
        }

        ScoreText.Text = $"Score: {_game.Score}";

        if (_game.IsGameOver || _game.IsWon)
        {
            GameOverText.Text = _game.IsWon ? "You Win!" : "Game Over!";
            GameOverOverlay.Visibility = Visibility.Visible;
        }
    }

    private SolidColorBrush GetColor(int val)
    {
        return val switch
        {
            2 => new SolidColorBrush(Color.FromArgb(255, 238, 228, 218)),
            4 => new SolidColorBrush(Color.FromArgb(255, 237, 224, 200)),
            8 => new SolidColorBrush(Color.FromArgb(255, 242, 177, 121)),
            16 => new SolidColorBrush(Color.FromArgb(255, 245, 149, 99)),
            32 => new SolidColorBrush(Color.FromArgb(255, 246, 124, 95)),
            64 => new SolidColorBrush(Color.FromArgb(255, 246, 94, 59)),
            128 => new SolidColorBrush(Color.FromArgb(255, 237, 207, 114)),
            256 => new SolidColorBrush(Color.FromArgb(255, 237, 204, 97)),
            512 => new SolidColorBrush(Color.FromArgb(255, 237, 200, 80)),
            1024 => new SolidColorBrush(Color.FromArgb(255, 237, 197, 63)),
            2048 => new SolidColorBrush(Color.FromArgb(255, 237, 194, 46)),
            _ => new SolidColorBrush(Color.FromArgb(255, 60, 58, 50))
        };
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_game.IsGameOver || _game.IsWon) return;

        Direction? dir = e.Key switch
        {
            VirtualKey.Up or VirtualKey.W => Direction.Up,
            VirtualKey.Down or VirtualKey.S => Direction.Down,
            VirtualKey.Left or VirtualKey.A => Direction.Left,
            VirtualKey.Right or VirtualKey.D => Direction.Right,
            _ => null
        };
        
        if (dir.HasValue)
        {
            _game.Move(dir.Value);
            Draw();
        }
    }
}

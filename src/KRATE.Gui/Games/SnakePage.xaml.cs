using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI;
using Windows.System;
using Krate.Core.Games;

namespace Krate.Gui.Games;

public sealed partial class SnakePage : UserControl
{
    private SnakeGame _game;
    private DispatcherTimer _timer;
    private const int GridSize = 20; // 20x20 grid
    private const int CellSize = 20; // 20px per cell

    public SnakePage()
    {
        this.InitializeComponent();
        
        _game = new SnakeGame(GridSize, GridSize);
        _timer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(150) };
        _timer.Tick += (s, e) => UpdateGame();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        this.Focus(FocusState.Programmatic);
        StartGame();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
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
        _timer.Start();
        Draw();
    }

    private void UpdateGame()
    {
        _game.Tick();
        Draw();

        if (_game.IsGameOver)
        {
            _timer.Stop();
            GameOverOverlay.Visibility = Visibility.Visible;
        }
    }

    private void Draw()
    {
        GameCanvas.Children.Clear();

        // Draw Food
        var food = new Rectangle
        {
            Width = CellSize,
            Height = CellSize,
            Fill = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            RadiusX = 10,
            RadiusY = 10
        };
        Canvas.SetLeft(food, _game.Food.x * CellSize);
        Canvas.SetTop(food, _game.Food.y * CellSize);
        GameCanvas.Children.Add(food);

        // Draw Snake
        for (int i = 0; i < _game.Snake.Count; i++)
        {
            var p = _game.Snake[i];
            var isHead = i == 0;
            
            var rect = new Rectangle
            {
                Width = CellSize,
                Height = CellSize,
                Fill = isHead ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] : (Brush)Application.Current.Resources["AccentFillColorTertiaryBrush"],
                RadiusX = isHead ? 6 : 4,
                RadiusY = isHead ? 6 : 4
            };
            
            // Add a tiny margin for visual separation of segments
            rect.Width -= 2;
            rect.Height -= 2;
            Canvas.SetLeft(rect, (p.x * CellSize) + 1);
            Canvas.SetTop(rect, (p.y * CellSize) + 1);
            
            GameCanvas.Children.Add(rect);
        }

        ScoreText.Text = $"Score: {_game.Score}";
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_game.IsGameOver) return;

        var current = _game.CurrentDirection;
        var dir = e.Key switch
        {
            VirtualKey.Up or VirtualKey.W when current != Direction.Down => Direction.Up,
            VirtualKey.Down or VirtualKey.S when current != Direction.Up => Direction.Down,
            VirtualKey.Left or VirtualKey.A when current != Direction.Right => Direction.Left,
            VirtualKey.Right or VirtualKey.D when current != Direction.Left => Direction.Right,
            _ => current
        };
        
        _game.CurrentDirection = dir;
    }
}

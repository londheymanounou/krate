using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI;
using Windows.System;
using Krate.Core.Games;
using Windows.UI;

namespace Krate.Gui.Games;

public sealed partial class TetrisPage : UserControl
{
    private TetrisGame _game;
    private DispatcherTimer _timer;
    private const int CellSize = 25;

    private readonly SolidColorBrush[] _colors = new[]
    {
        new SolidColorBrush(Colors.Transparent),
        new SolidColorBrush(Color.FromArgb(255, 0, 255, 255)), // I - Cyan
        new SolidColorBrush(Color.FromArgb(255, 0, 0, 255)),   // J - Blue
        new SolidColorBrush(Color.FromArgb(255, 255, 127, 0)), // L - Orange
        new SolidColorBrush(Color.FromArgb(255, 255, 255, 0)), // O - Yellow
        new SolidColorBrush(Color.FromArgb(255, 0, 255, 0)),   // S - Green
        new SolidColorBrush(Color.FromArgb(255, 128, 0, 128)), // T - Purple
        new SolidColorBrush(Color.FromArgb(255, 255, 0, 0))    // Z - Red
    };

    public TetrisPage()
    {
        this.InitializeComponent();
        
        _game = new TetrisGame();
        _timer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(500) };
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
        _game.MoveDown();
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

        // Draw Board
        for (int y = 0; y < _game.Height; y++)
        {
            for (int x = 0; x < _game.Width; x++)
            {
                if (_game.Board[y, x] != 0)
                {
                    DrawBlock(x, y, _game.Board[y, x]);
                }
            }
        }

        // Draw Current Piece
        if (_game.CurrentShape != null)
        {
            for (int y = 0; y < _game.CurrentShape.GetLength(0); y++)
            {
                for (int x = 0; x < _game.CurrentShape.GetLength(1); x++)
                {
                    if (_game.CurrentShape[y, x] != 0)
                    {
                        DrawBlock(_game.CurrentX + x, _game.CurrentY + y, _game.CurrentColor);
                    }
                }
            }
        }

        ScoreText.Text = $"Score: {_game.Score}";
    }

    private void DrawBlock(int x, int y, int colorIndex)
    {
        var rect = new Rectangle
        {
            Width = CellSize - 1,
            Height = CellSize - 1,
            Fill = _colors[colorIndex],
            RadiusX = 2,
            RadiusY = 2
        };
        Canvas.SetLeft(rect, x * CellSize);
        Canvas.SetTop(rect, y * CellSize);
        GameCanvas.Children.Add(rect);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_game.IsGameOver) return;

        bool handled = true;
        switch (e.Key)
        {
            case VirtualKey.Left:
                _game.MoveLeft();
                break;
            case VirtualKey.Right:
                _game.MoveRight();
                break;
            case VirtualKey.Up:
                _game.Rotate();
                break;
            case VirtualKey.Down:
                _game.MoveDown();
                break;
            case VirtualKey.Space:
                _game.Drop();
                break;
            default:
                handled = false;
                break;
        }

        if (handled)
        {
            e.Handled = true;
            Draw();
        }
    }
}

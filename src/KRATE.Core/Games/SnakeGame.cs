using System;
using System.Collections.Generic;

namespace Krate.Core.Games;

public enum Direction { Up, Down, Left, Right }

public class SnakeGame
{
    public int Width { get; }
    public int Height { get; }
    public List<(int x, int y)> Snake { get; } = new();
    public (int x, int y) Food { get; private set; }
    public Direction CurrentDirection { get; set; } = Direction.Right;
    public bool IsGameOver { get; private set; }
    public int Score => Math.Max(0, Snake.Count - 3);

    private Random _rand = new();

    public SnakeGame(int width, int height)
    {
        Width = width;
        Height = height;
        Start();
    }

    public void Start()
    {
        Snake.Clear();
        Snake.Add((Width / 2, Height / 2));
        Snake.Add((Width / 2 - 1, Height / 2));
        Snake.Add((Width / 2 - 2, Height / 2));
        CurrentDirection = Direction.Right;
        IsGameOver = false;
        SpawnFood();
    }

    public void Tick()
    {
        if (IsGameOver) return;

        var head = Snake[0];
        (int x, int y) newHead = CurrentDirection switch
        {
            Direction.Up => (head.x, head.y - 1),
            Direction.Down => (head.x, head.y + 1),
            Direction.Left => (head.x - 1, head.y),
            Direction.Right => (head.x + 1, head.y),
            _ => head
        };

        // Wall collision
        if (newHead.x < 0 || newHead.x >= Width || newHead.y < 0 || newHead.y >= Height)
        {
            IsGameOver = true;
            return;
        }

        // Self collision (ignore the very last tail piece as it will move forward)
        for (int i = 0; i < Snake.Count - 1; i++)
        {
            if (Snake[i] == newHead)
            {
                IsGameOver = true;
                return;
            }
        }

        Snake.Insert(0, newHead);

        if (newHead == Food)
        {
            SpawnFood();
        }
        else
        {
            Snake.RemoveAt(Snake.Count - 1);
        }
    }

    private void SpawnFood()
    {
        // Simple spawn logic. Might loop if board is 100% full, but that's a rare win state.
        int attempts = 0;
        while (attempts < 1000)
        {
            int x = _rand.Next(Width);
            int y = _rand.Next(Height);
            if (!Snake.Contains((x, y)))
            {
                Food = (x, y);
                return;
            }
            attempts++;
        }
    }
}

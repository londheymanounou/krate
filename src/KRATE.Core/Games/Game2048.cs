using System;
using System.Collections.Generic;
using System.Linq;

namespace Krate.Core.Games;

public class Game2048
{
    public int[,] Board { get; } = new int[4, 4];
    public int Score { get; private set; }
    public bool IsGameOver { get; private set; }
    public bool IsWon { get; private set; }

    private Random _rand = new();

    public Game2048()
    {
        Start();
    }

    public void Start()
    {
        Array.Clear(Board, 0, Board.Length);
        Score = 0;
        IsGameOver = false;
        IsWon = false;
        Spawn();
        Spawn();
    }

    private void Spawn()
    {
        var empty = new List<(int x, int y)>();
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                if (Board[x, y] == 0) empty.Add((x, y));
        
        if (empty.Count == 0) return;
        var p = empty[_rand.Next(empty.Count)];
        Board[p.x, p.y] = _rand.NextDouble() < 0.9 ? 2 : 4;
    }

    public void Move(Direction dir)
    {
        if (IsGameOver || IsWon) return;

        bool moved = false;

        for (int i = 0; i < 4; i++)
        {
            var line = new List<int>();
            for (int j = 0; j < 4; j++)
            {
                int val = dir switch
                {
                    Direction.Left => Board[j, i],
                    Direction.Right => Board[3 - j, i],
                    Direction.Up => Board[i, j],
                    Direction.Down => Board[i, 3 - j],
                    _ => 0
                };
                if (val != 0) line.Add(val);
            }

            // Merge
            for (int k = 0; k < line.Count - 1; k++)
            {
                if (line[k] == line[k + 1])
                {
                    line[k] *= 2;
                    Score += line[k];
                    if (line[k] == 2048) IsWon = true;
                    line.RemoveAt(k + 1);
                }
            }

            // Write back
            for (int j = 0; j < 4; j++)
            {
                int newVal = j < line.Count ? line[j] : 0;
                int oldVal = dir switch
                {
                    Direction.Left => Board[j, i],
                    Direction.Right => Board[3 - j, i],
                    Direction.Up => Board[i, j],
                    Direction.Down => Board[i, 3 - j],
                    _ => 0
                };

                if (newVal != oldVal) moved = true;

                switch (dir)
                {
                    case Direction.Left: Board[j, i] = newVal; break;
                    case Direction.Right: Board[3 - j, i] = newVal; break;
                    case Direction.Up: Board[i, j] = newVal; break;
                    case Direction.Down: Board[i, 3 - j] = newVal; break;
                }
            }
        }

        if (moved)
        {
            Spawn();
            CheckGameOver();
        }
    }

    private void CheckGameOver()
    {
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                if (Board[x, y] == 0) return;
                if (x < 3 && Board[x, y] == Board[x + 1, y]) return;
                if (y < 3 && Board[x, y] == Board[x, y + 1]) return;
            }
        IsGameOver = true;
    }
}

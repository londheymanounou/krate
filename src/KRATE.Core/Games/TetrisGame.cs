using System;
using System.Collections.Generic;

namespace Krate.Core.Games;

public class TetrisGame
{
    public int Width { get; } = 10;
    public int Height { get; } = 20;
    public int[,] Board { get; }
    
    public int Score { get; private set; }
    public bool IsGameOver { get; private set; }
    
    public int CurrentX { get; private set; }
    public int CurrentY { get; private set; }
    public int[,] CurrentShape { get; private set; }
    public int CurrentColor { get; private set; }

    private Random _rand = new();

    private static readonly int[][,] Shapes =
    {
        // I
        new int[,] {{1, 1, 1, 1}},
        // J
        new int[,] {{1, 0, 0}, {1, 1, 1}},
        // L
        new int[,] {{0, 0, 1}, {1, 1, 1}},
        // O
        new int[,] {{1, 1}, {1, 1}},
        // S
        new int[,] {{0, 1, 1}, {1, 1, 0}},
        // T
        new int[,] {{0, 1, 0}, {1, 1, 1}},
        // Z
        new int[,] {{1, 1, 0}, {0, 1, 1}}
    };

    public TetrisGame()
    {
        Board = new int[Height, Width];
        Start();
    }

    public void Start()
    {
        Array.Clear(Board, 0, Board.Length);
        Score = 0;
        IsGameOver = false;
        Spawn();
    }

    private void Spawn()
    {
        int shapeIdx = _rand.Next(Shapes.Length);
        CurrentShape = (int[,])Shapes[shapeIdx].Clone();
        CurrentColor = shapeIdx + 1; // Colors 1 to 7
        
        CurrentX = Width / 2 - CurrentShape.GetLength(1) / 2;
        CurrentY = 0;

        if (!IsValidPosition(CurrentX, CurrentY, CurrentShape))
        {
            IsGameOver = true;
        }
    }

    public void MoveLeft()
    {
        if (IsGameOver) return;
        if (IsValidPosition(CurrentX - 1, CurrentY, CurrentShape))
            CurrentX--;
    }

    public void MoveRight()
    {
        if (IsGameOver) return;
        if (IsValidPosition(CurrentX + 1, CurrentY, CurrentShape))
            CurrentX++;
    }

    public void Rotate()
    {
        if (IsGameOver) return;
        
        int h = CurrentShape.GetLength(0);
        int w = CurrentShape.GetLength(1);
        int[,] newShape = new int[w, h];
        
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                newShape[x, h - 1 - y] = CurrentShape[y, x];
            }
        }

        if (IsValidPosition(CurrentX, CurrentY, newShape))
            CurrentShape = newShape;
    }

    public void MoveDown()
    {
        if (IsGameOver) return;

        if (IsValidPosition(CurrentX, CurrentY + 1, CurrentShape))
        {
            CurrentY++;
        }
        else
        {
            LockPiece();
        }
    }

    public void Drop()
    {
        if (IsGameOver) return;
        while (IsValidPosition(CurrentX, CurrentY + 1, CurrentShape))
        {
            CurrentY++;
        }
        LockPiece();
    }

    private bool IsValidPosition(int nx, int ny, int[,] shape)
    {
        for (int y = 0; y < shape.GetLength(0); y++)
        {
            for (int x = 0; x < shape.GetLength(1); x++)
            {
                if (shape[y, x] != 0)
                {
                    int boardX = nx + x;
                    int boardY = ny + y;

                    if (boardX < 0 || boardX >= Width || boardY >= Height)
                        return false;

                    if (boardY >= 0 && Board[boardY, boardX] != 0)
                        return false;
                }
            }
        }
        return true;
    }

    private void LockPiece()
    {
        for (int y = 0; y < CurrentShape.GetLength(0); y++)
        {
            for (int x = 0; x < CurrentShape.GetLength(1); x++)
            {
                if (CurrentShape[y, x] != 0)
                {
                    if (CurrentY + y < 0)
                    {
                        IsGameOver = true;
                        return;
                    }
                    Board[CurrentY + y, CurrentX + x] = CurrentColor;
                }
            }
        }

        ClearLines();
        Spawn();
    }

    private void ClearLines()
    {
        int linesCleared = 0;
        
        for (int y = Height - 1; y >= 0; y--)
        {
            bool isFull = true;
            for (int x = 0; x < Width; x++)
            {
                if (Board[y, x] == 0)
                {
                    isFull = false;
                    break;
                }
            }

            if (isFull)
            {
                // Shift down
                for (int yy = y; yy > 0; yy--)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        Board[yy, x] = Board[yy - 1, x];
                    }
                }
                for (int x = 0; x < Width; x++)
                {
                    Board[0, x] = 0;
                }
                linesCleared++;
                y++; // Recheck the same line index
            }
        }

        Score += linesCleared switch
        {
            1 => 100,
            2 => 300,
            3 => 500,
            4 => 800,
            _ => 0
        };
    }
}

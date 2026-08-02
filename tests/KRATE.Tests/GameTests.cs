using System.Globalization;
using Krate.Core;
using Krate.Core.Games;
using Xunit;

/// <summary>The games spawn tiles and food at random, so every test here pins the board first
/// and asserts on the rule being exercised — never on what the generator happened to produce.</summary>
public class Game2048Tests
{
    public Game2048Tests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    static Game2048 Blank()
    {
        var game = new Game2048();
        Array.Clear(game.Board, 0, game.Board.Length);
        return game;
    }

    [Fact]
    public void Start_DealsExactlyTwoTiles_EachATwoOrAFour()
    {
        var game = new Game2048();
        var tiles = new List<int>();
        foreach (var v in game.Board) if (v != 0) tiles.Add(v);

        Assert.Equal(2, tiles.Count);
        Assert.All(tiles, t => Assert.True(t is 2 or 4, $"opening tile was {t}"));
        Assert.Equal(0, game.Score);
        Assert.False(game.IsGameOver);
    }

    [Fact]
    public void Move_MergesEqualNeighbours_AndScoresTheResultingTile()
    {
        var game = Blank();
        game.Board[0, 0] = 2;
        game.Board[1, 0] = 2;

        game.Move(Direction.Left);

        Assert.Equal(4, game.Board[0, 0]);
        Assert.Equal(4, game.Score);
    }

    [Fact]
    public void Move_MergesEachTileOnlyOncePerMove()
    {
        // [2,2,2,2] must become [4,4] and score 8 — not a single 8, and not 16.
        var game = Blank();
        for (var x = 0; x < 4; x++) game.Board[x, 0] = 2;

        game.Move(Direction.Left);

        Assert.Equal(4, game.Board[0, 0]);
        Assert.Equal(4, game.Board[1, 0]);
        Assert.Equal(8, game.Score);
    }

    [Fact]
    public void Move_SlidesTilesAcrossGapsWithoutMerging()
    {
        var game = Blank();
        game.Board[0, 0] = 2;
        game.Board[3, 0] = 4;

        game.Move(Direction.Left);

        Assert.Equal(2, game.Board[0, 0]);
        Assert.Equal(4, game.Board[1, 0]);
        Assert.Equal(0, game.Score);          // sliding is not scoring
    }

    [Fact]
    public void Move_WorksInAllFourDirections()
    {
        foreach (var (dir, x, y) in new[]
        {
            (Direction.Left, 0, 1), (Direction.Right, 3, 1),
            (Direction.Up, 1, 0), (Direction.Down, 1, 3),
        })
        {
            var game = Blank();
            game.Board[1, 1] = 8;
            game.Move(dir);
            Assert.True(game.Board[x, y] == 8, $"{dir} did not land the tile at ({x},{y})");
        }
    }

    [Fact]
    public void ReachingTwentyFortyEight_WinsTheGame()
    {
        var game = Blank();
        game.Board[0, 0] = 1024;
        game.Board[1, 0] = 1024;

        game.Move(Direction.Left);

        Assert.True(game.IsWon);
        Assert.Equal(2048, game.Board[0, 0]);
    }

    /// <summary>The stuck position, asserted directly. Going through IsGameOver would be
    /// non-deterministic: the flag is only evaluated after a move that changed something, and
    /// every such move spawns a random tile that can itself create a new merge.</summary>
    [Fact]
    public void AFullBoardWithNoEqualNeighbours_AdmitsNoMoveInAnyDirection()
    {
        var game = Blank();
        int[,] values = { { 2, 4, 2, 4 }, { 4, 2, 4, 2 }, { 2, 4, 2, 4 }, { 4, 2, 4, 2 } };
        for (var x = 0; x < 4; x++)
            for (var y = 0; y < 4; y++)
                game.Board[x, y] = values[x, y];

        foreach (var dir in new[] { Direction.Left, Direction.Right, Direction.Up, Direction.Down })
        {
            game.Move(dir);
            for (var x = 0; x < 4; x++)
                for (var y = 0; y < 4; y++)
                    Assert.True(values[x, y] == game.Board[x, y],
                        $"{dir} changed a board that has no legal move (at {x},{y})");
        }
        Assert.Equal(0, game.Score);   // nothing merged, so nothing scored
    }
}

public class SnakeGameTests
{
    public SnakeGameTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Start_PlacesAThreeSegmentSnakeFacingRight()
    {
        var game = new SnakeGame(20, 10);

        Assert.Equal(3, game.Snake.Count);
        Assert.Equal((10, 5), game.Snake[0]);
        Assert.Equal(Direction.Right, game.CurrentDirection);
        Assert.False(game.IsGameOver);
        Assert.Equal(0, game.Score);
        Assert.DoesNotContain(game.Food, game.Snake);   // food never starts under the snake
    }

    [Fact]
    public void Tick_MovesTheHeadOneCellInTheCurrentDirection()
    {
        var game = new SnakeGame(20, 10);
        var head = game.Snake[0];

        game.Tick();

        Assert.Equal((head.x + 1, head.y), game.Snake[0]);
        Assert.False(game.IsGameOver);
    }

    [Fact]
    public void HittingAWall_EndsTheGame()
    {
        var game = new SnakeGame(20, 10);
        game.CurrentDirection = Direction.Up;   // head starts at y = 5

        for (var i = 0; i < 5; i++) game.Tick();
        Assert.False(game.IsGameOver);           // y reached 0, still on the board

        game.Tick();                             // y would be -1
        Assert.True(game.IsGameOver);
    }

    [Fact]
    public void ReversingIntoItsOwnNeck_EndsTheGame()
    {
        var game = new SnakeGame(20, 10);
        game.CurrentDirection = Direction.Left;  // straight back into segment 1

        game.Tick();

        Assert.True(game.IsGameOver);
    }

    [Fact]
    public void AFinishedGame_IgnoresFurtherTicks()
    {
        var game = new SnakeGame(20, 10);
        game.CurrentDirection = Direction.Left;
        game.Tick();
        var frozen = game.Snake.ToList();

        game.Tick();
        game.Tick();

        Assert.Equal(frozen, game.Snake);
    }

    [Fact]
    public void Start_ResetsAGameThatWasAlreadyOver()
    {
        var game = new SnakeGame(20, 10);
        game.CurrentDirection = Direction.Left;
        game.Tick();
        Assert.True(game.IsGameOver);

        game.Start();

        Assert.False(game.IsGameOver);
        Assert.Equal(3, game.Snake.Count);
        Assert.Equal(Direction.Right, game.CurrentDirection);
    }
}

public class TetrisGameTests
{
    public TetrisGameTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Start_PutsAPieceOnAnEmptyBoard()
    {
        var game = new TetrisGame();

        Assert.Equal(10, game.Width);
        Assert.Equal(20, game.Height);
        Assert.Equal(0, game.Score);
        Assert.False(game.IsGameOver);
        Assert.NotNull(game.CurrentShape);
        Assert.InRange(game.CurrentColor, 1, 7);
        Assert.All(game.Board.Cast<int>(), cell => Assert.Equal(0, cell));
    }

    [Fact]
    public void Drop_LandsThePieceOnTheFloor()
    {
        var game = new TetrisGame();

        game.Drop();

        Assert.Contains(game.Board.Cast<int>(), cell => cell != 0);
    }

    [Fact]
    public void CompletingALine_ClearsItAndScores100()
    {
        var game = new TetrisGame();
        // Fill the bottom row by hand, then land any piece to trigger the line check.
        for (var x = 0; x < game.Width; x++) game.Board[game.Height - 1, x] = 1;

        game.Drop();

        Assert.Equal(100, game.Score);
        // The full row is gone; what remains is only the piece that just landed.
        var filled = 0;
        for (var x = 0; x < game.Width; x++) if (game.Board[game.Height - 1, x] != 0) filled++;
        Assert.True(filled < game.Width, "the completed row was not cleared");
    }

    [Fact]
    public void MoveLeftAndRight_StopAtTheWalls()
    {
        var game = new TetrisGame();

        for (var i = 0; i < 20; i++) game.MoveLeft();
        Assert.True(game.CurrentX >= 0);

        for (var i = 0; i < 40; i++) game.MoveRight();
        Assert.True(game.CurrentX + game.CurrentShape.GetLength(1) <= game.Width);
    }

    [Fact]
    public void Rotate_NeverPushesThePieceOffTheBoard()
    {
        var game = new TetrisGame();

        for (var i = 0; i < 8; i++)
        {
            game.Rotate();
            Assert.True(game.CurrentX >= 0, "rotation pushed the piece off the left edge");
            Assert.True(game.CurrentX + game.CurrentShape.GetLength(1) <= game.Width,
                "rotation pushed the piece off the right edge");
        }
    }

    [Fact]
    public void MoveDown_AdvancesThePieceUntilItLocks()
    {
        var game = new TetrisGame();
        var start = game.CurrentY;

        game.MoveDown();

        // Either it fell one row, or it locked and a fresh piece appeared at the top.
        Assert.True(game.CurrentY == start + 1 || game.CurrentY == 0);
    }
}

/// <summary>Weather and the three games are registered in the catalog so they are searchable,
/// but their Run delegate is a placeholder — the GUI and CLI handle them directly. If one ever
/// gains a text-in/text-out implementation, this test fails and the catalog entry can be fixed.</summary>
public class CatalogPlaceholderTests
{
    [Theory]
    [InlineData("Weather")]
    [InlineData("Snake")]
    [InlineData("Game2048")]
    [InlineData("Tetris")]
    public void InteractiveTools_AreSearchableButNotRunnableAsText(string id)
    {
        var tool = Catalog.Tools.Single(t => t.Id == id);
        // The managed implementation, deliberately: NotSupportedException is what the C# placeholder
        // throws. Through Run() the native core answers instead, and it reports rather than throws
        // that particular type — ThePlaceholders_AreRefusedByBothSides covers that path.
        Assert.Throws<NotSupportedException>(() => tool.CSharp(""));
    }
}

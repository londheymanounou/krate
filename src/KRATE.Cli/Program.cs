using System.Reflection;
using System.Text;
using Krate.Cli;
using Krate.Core;

// The Windows console defaults to a legacy codepage; without this, × and é come out as mojibake.
try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { /* output redirected */ }

var argv = args.ToList();

// --lang <code|auto>: persisted interface language override, usable alongside any other command.
var langAt = argv.IndexOf("--lang");
if (langAt >= 0)
{
    var code = langAt + 1 < argv.Count ? argv[langAt + 1] : "auto";
    argv.RemoveRange(langAt, Math.Min(2, argv.Count - langAt));
    try { Settings.Language = code is "auto" or "" ? null : code; }
    catch (System.Globalization.CultureNotFoundException) { Console.Error.WriteLine($"Unknown language: {code}"); return 2; }
    Console.WriteLine(Strings.Get("Cli_LanguageSet", code));
    if (argv.Count == 0) return 0;
}

if (argv.Count == 0)
{
    Header(); PrintUsage();
    Console.WriteLine();
    Console.WriteLine(Strings.Get("Cli_Tools", Catalog.Tools.Count));
    List(Catalog.Tools);
    return 0;
}

// Top-level commands.
switch (argv[0].ToLowerInvariant())
{
    case "-h" or "--help": Header(); PrintUsage(); Examples(); return 0;
    case "-v" or "--version": Console.WriteLine($"{Strings.Get("App_Name")} {Version()}"); return 0;
    case "search" or "find":
        var results = Catalog.Search(string.Join(' ', argv.Skip(1))).ToList();
        if (results.Count == 0) { Console.WriteLine(Strings.Get("Cli_NoMatch")); return 1; }
        List(results);
        return 0;
    case "completion": return Completion(argv.Count > 1 ? argv[1].ToLowerInvariant() : "");
    case "stats" or "usage": return Stats(argv.Skip(1).ToList());
    case "encrypt": return RunCrypt(argv.Skip(1).ToList(), encrypt: true);
    case "decrypt": return RunCrypt(argv.Skip(1).ToList(), encrypt: false);
    case "youtube" or "yt" or "download": return RunYouTube(argv.Skip(1).ToList());
    case "gamepad" or "joystick": return GamepadMonitor.Run();
    case "weather": return RunWeather(argv.Skip(1).ToList());
    case "clicker" or "tally": return RunClicker();
    case "snake" or "play": return RunSnake();
    case "2048": return Run2048();
    case "tetris": return RunTetris();
    case "notepad" or "edit": return RunNotepad(argv.Skip(1).ToList());
    case "media" or "convertfile": return RunMedia(argv.Skip(1).ToList());
    // `convert <file> <format>` is a media conversion; anything else (e.g. "10 km mi") stays the unit
    // converter tool, reached by falling through to the normal tool dispatch below.
    case "convert" when argv.Count == 3 && File.Exists(argv[1].Trim('"')) && Media.Format(argv[2]) is not null:
        return RunMedia(argv.Skip(1).ToList());
}

var tool = Catalog.Find(argv[0]);
if (tool is null) { Console.Error.WriteLine(Strings.Get("Cli_UnknownTool", argv[0])); return 1; }

// `krate <tool> --help` explains one tool.
if (argv.Count > 1 && argv[1] is "-h" or "--help") { ToolHelp(tool); return 0; }

// No text argument = read stdin if piped, else empty.
var input = argv.Count > 1 ? string.Join(' ', argv.Skip(1)) : (Console.IsInputRedirected ? Console.In.ReadToEnd().TrimEnd('\n', '\r') : "");
try { Console.WriteLine(tool.Run(input)); Usage.Record(tool.Id); return 0; }
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; } // a tool's own error → stderr + exit 1

// ---------- media conversion (ffmpeg) ----------

int RunMedia(List<string> a)
{
    if (a.Count == 0 || a[0] is "list" or "formats")
    {
        Console.WriteLine(Media.HasFfmpeg ? Strings.Get("Media_Ready") : Strings.Get("Media_NeedSetup"));
        foreach (var group in Media.Formats.GroupBy(f => f.Category))
            Console.WriteLine($"  {group.Key,-6} {string.Join(", ", group.Select(f => f.Id))}");
        Console.WriteLine();
        Console.WriteLine(Strings.Get("Media_CliUsage"));
        return 0;
    }

    if (a[0] is "setup" or "install")
    {
        if (Media.HasFfmpeg) { Console.WriteLine(Strings.Get("Media_Ready")); return 0; }
        try { Media.EnsureFfmpegAsync(new Progress<string>(Console.WriteLine)).GetAwaiter().GetResult(); Console.WriteLine(Strings.Get("Media_Ready")); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }

    if (a.Count < 2) { Console.Error.WriteLine(Strings.Get("Media_CliUsage")); return 2; }
    if (Media.Format(a[1]) is null) { Console.Error.WriteLine(Strings.Get("Media_UnknownFormat", a[1])); return 1; }
    if (!Media.HasFfmpeg) { Console.Error.WriteLine(Strings.Get("Media_NeedSetup")); return 1; }

    try
    {
        Console.Error.WriteLine(Strings.Get("Media_Converting", Path.GetFileName(a[0].Trim('"')), a[1]));
        Console.WriteLine(Media.Convert(a[0], a[1]));
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
}

// ---------- terminal notepad (micro) ----------

int RunNotepad(List<string> a)
{
    if (!Notepad.HasMicro)
    {
        try { Notepad.EnsureMicroAsync(new Progress<string>(Console.Error.WriteLine)).GetAwaiter().GetResult(); }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
    
    var file = a.Count > 0 ? a[0] : null;
    try
    {
        return Notepad.Run(file);
    }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
}

// ---------- terminal clicker ----------

int RunClicker()
{
    Console.WriteLine(Strings.Get("Cli_ClickerTitle"));
    Console.WriteLine(Strings.Get("Cli_ClickerHelp"));
    int count = 0;
    
    // Clear the current line properly
    void Draw() => Console.Write($"\rCount: {count,-10}");
    Draw();
    
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Escape) break;
        else if (key.Key == ConsoleKey.Spacebar || key.Key == ConsoleKey.Enter) count++;
        else if (key.Key == ConsoleKey.Backspace && count > 0) count--;
        else if (key.Key == ConsoleKey.R) count = 0;
        Draw();
    }
    Console.WriteLine();
    Usage.Record("Clicker");
    return 0;
}

// ---------- snake ----------

int RunSnake()
{
    Console.Clear();
    Console.CursorVisible = false;
    
    int w = 20;
    int h = 10; // Keep it somewhat proportional in console font (fonts are roughly 2x taller)
    
    var game = new Krate.Core.Games.SnakeGame(w, h);
    
    var lastTick = DateTime.Now;
    
    while (!game.IsGameOver)
    {
        // Input
        if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(true).Key;
            var current = game.CurrentDirection;
            if (key == ConsoleKey.Escape) break;
            
            game.CurrentDirection = key switch
            {
                ConsoleKey.UpArrow or ConsoleKey.W when current != Krate.Core.Games.Direction.Down => Krate.Core.Games.Direction.Up,
                ConsoleKey.DownArrow or ConsoleKey.S when current != Krate.Core.Games.Direction.Up => Krate.Core.Games.Direction.Down,
                ConsoleKey.LeftArrow or ConsoleKey.A when current != Krate.Core.Games.Direction.Right => Krate.Core.Games.Direction.Left,
                ConsoleKey.RightArrow or ConsoleKey.D when current != Krate.Core.Games.Direction.Left => Krate.Core.Games.Direction.Right,
                _ => current
            };
        }
        
        // Tick every 150ms
        if ((DateTime.Now - lastTick).TotalMilliseconds >= 150)
        {
            game.Tick();
            lastTick = DateTime.Now;
            
            // Render
            Console.SetCursorPosition(0, 0);
            
            // Top border
            Console.WriteLine("+" + new string('-', w * 2) + "+");
            
            for (int y = 0; y < h; y++)
            {
                Console.Write("|");
                for (int x = 0; x < w; x++)
                {
                    if (game.Food == (x, y)) Console.Write("O ");
                    else if (game.Snake.Contains((x, y)))
                    {
                        if (game.Snake[0] == (x, y)) Console.Write("##");
                        else Console.Write("[]");
                    }
                    else Console.Write("  ");
                }
                Console.WriteLine("|");
            }
            
            // Bottom border
            Console.WriteLine("+" + new string('-', w * 2) + "+");
            Console.WriteLine($"Score: {game.Score}   (Press ESC to quit)");
        }
        else
        {
            System.Threading.Thread.Sleep(10);
        }
    }
    
    Console.CursorVisible = true;
    Console.WriteLine();
    if (game.IsGameOver) Console.WriteLine("Game Over!");
    Usage.Record("Snake");
    return 0;
}

// ---------- 2048 ----------

int Run2048()
{
    Console.Clear();
    Console.CursorVisible = false;
    
    var game = new Krate.Core.Games.Game2048();
    
    while (!game.IsGameOver && !game.IsWon)
    {
        Console.SetCursorPosition(0, 0);
        Console.WriteLine("+" + new string('-', 24) + "+");
        for (int y = 0; y < 4; y++)
        {
            Console.Write("|");
            for (int x = 0; x < 4; x++)
            {
                int val = game.Board[x, y];
                if (val == 0) Console.Write("      ");
                else Console.Write($"{val,4}  ");
            }
            Console.WriteLine("|");
        }
        Console.WriteLine("+" + new string('-', 24) + "+");
        Console.WriteLine($"Score: {game.Score}   (Arrows to move, ESC to quit)");
        
        var key = Console.ReadKey(true).Key;
        if (key == ConsoleKey.Escape) break;
        
        if (key is ConsoleKey.UpArrow or ConsoleKey.W) game.Move(Krate.Core.Games.Direction.Up);
        if (key is ConsoleKey.DownArrow or ConsoleKey.S) game.Move(Krate.Core.Games.Direction.Down);
        if (key is ConsoleKey.LeftArrow or ConsoleKey.A) game.Move(Krate.Core.Games.Direction.Left);
        if (key is ConsoleKey.RightArrow or ConsoleKey.D) game.Move(Krate.Core.Games.Direction.Right);
    }
    
    Console.CursorVisible = true;
    Console.WriteLine();
    if (game.IsWon) Console.WriteLine("You Win!");
    else if (game.IsGameOver) Console.WriteLine("Game Over!");
    Usage.Record("Game2048");
    return 0;
}

// ---------- Tetris ----------

int RunTetris()
{
    Console.Clear();
    Console.CursorVisible = false;
    
    var game = new Krate.Core.Games.TetrisGame();
    
    DateTime lastDrop = DateTime.Now;
    TimeSpan dropRate = TimeSpan.FromMilliseconds(500);

    while (!game.IsGameOver)
    {
        if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.Escape) break;
            
            if (key == ConsoleKey.LeftArrow) game.MoveLeft();
            if (key == ConsoleKey.RightArrow) game.MoveRight();
            if (key == ConsoleKey.UpArrow) game.Rotate();
            if (key == ConsoleKey.DownArrow) game.MoveDown();
            if (key == ConsoleKey.Spacebar) game.Drop();
        }

        if (DateTime.Now - lastDrop > dropRate)
        {
            game.MoveDown();
            lastDrop = DateTime.Now;
        }

        Console.SetCursorPosition(0, 0);
        Console.WriteLine("+" + new string('-', game.Width * 2) + "+");
        for (int y = 0; y < game.Height; y++)
        {
            Console.Write("|");
            for (int x = 0; x < game.Width; x++)
            {
                // Check current shape first
                bool isShape = false;
                if (game.CurrentShape != null &&
                    y >= game.CurrentY && y < game.CurrentY + game.CurrentShape.GetLength(0) &&
                    x >= game.CurrentX && x < game.CurrentX + game.CurrentShape.GetLength(1))
                {
                    if (game.CurrentShape[y - game.CurrentY, x - game.CurrentX] != 0)
                    {
                        Console.Write("[]");
                        isShape = true;
                    }
                }
                
                if (!isShape)
                {
                    if (game.Board[y, x] != 0) Console.Write("[]");
                    else Console.Write(" .");
                }
            }
            Console.WriteLine("|");
        }
        Console.WriteLine("+" + new string('-', game.Width * 2) + "+");
        Console.WriteLine($"Score: {game.Score}   (Arrows to move/rotate, SPACE to drop, ESC to quit)");
        
        System.Threading.Thread.Sleep(30);
    }
    
    Console.CursorVisible = true;
    Console.WriteLine();
    Console.WriteLine("Game Over!");
    Usage.Record("Tetris");
    return 0;
}

// ---------- weather ----------

int RunWeather(List<string> a)
{
    var city = string.Join(" ", a).Trim();
    try
    {
        var info = Krate.Core.WeatherApi.GetAsync(city).GetAwaiter().GetResult();
        Console.WriteLine($"{info.Location}: {info.TempC:0.#}°C, {info.Description} {info.Icon}");
        Usage.Record("Weather");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

// ---------- usage statistics ----------

int Stats(List<string> a)
{
    if (a.Count > 0 && a[0].Equals("reset", StringComparison.OrdinalIgnoreCase))
    {
        Usage.Reset();
        Console.WriteLine(Strings.Get("Stats_Reset"));
        return 0;
    }
    var ranked = Usage.Ranked();
    if (ranked.Count == 0) { Console.WriteLine(Strings.Get("Stats_Empty")); return 0; }
    Console.WriteLine(Strings.Get("Stats_Header", Usage.Total(), ranked.Count));
    foreach (var (id, count) in ranked)
        Console.WriteLine($"  {count,6}  {Usage.DisplayName(id)}");
    return 0;
}

// ---------- file encryption (hidden password prompt) ----------

int RunCrypt(List<string> a, bool encrypt)
{
    if (a.Count == 0) { Console.Error.WriteLine(Strings.Get("Cli_CryptUsage")); return 2; }
    if (a[0] is "-h" or "--help") { ToolHelp(Catalog.Find(encrypt ? "Encrypt" : "Decrypt")!); return 0; }

    var path = string.Join(' ', a).Trim().Trim('"');
    string password;

    // "path | password" still works for scripts; otherwise prompt for it without echoing.
    if (path.Contains('|'))
    {
        var i = path.LastIndexOf('|');
        password = path[(i + 1)..].Trim();
        path = path[..i].Trim().Trim('"');
    }
    else
    {
        Console.Error.Write(Strings.Get("Cli_Password"));
        password = ReadPassword();
        if (encrypt)
        {
            Console.Error.Write(Strings.Get("Cli_Confirm"));
            if (ReadPassword() != password) { Console.Error.WriteLine(Strings.Get("Cli_PasswordMismatch")); return 2; }
        }
    }

    try
    {
        Console.WriteLine(encrypt ? Crypt.EncryptFile(path, password) : Crypt.DecryptFile(path, password));
        Usage.Record(encrypt ? "Encrypt" : "Decrypt");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
}

// Reads a password without echoing (like ssh/psql). If stdin is piped, reads a line instead.
string ReadPassword()
{
    if (Console.IsInputRedirected) return (Console.ReadLine() ?? "").Trim();
    var sb = new StringBuilder();
    ConsoleKeyInfo key;
    while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; }
        else if (!char.IsControl(key.KeyChar)) sb.Append(key.KeyChar);
    }
    Console.Error.WriteLine();
    return sb.ToString();
}

// ---------- video downloader (yt-dlp) ----------

int RunYouTube(List<string> a)
{
    if (a.Count == 0) { Console.Error.WriteLine(Strings.Get("Yt_CliUsage")); return 2; }
    if (a[0] is "setup" or "install")
    {
        try { YouTube.EnsureYtDlpAsync(new Progress<string>(Console.Error.WriteLine)).GetAwaiter().GetResult(); Console.WriteLine(Strings.Get("Yt_Ready")); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
    if (!YouTube.HasYtDlp) { Console.Error.WriteLine(Strings.Get("Yt_NoYtDlp")); return 1; }

    var url = a[0];
    var format = a.Count > 1 ? a[1].ToLowerInvariant() : "mp4";
    try
    {
        var progress = new Progress<double>(p => Console.Error.Write($"\r{p * 100,3:0}%  "));
        var message = YouTube.Download(url, format, Directory.GetCurrentDirectory(), progress);
        Console.Error.WriteLine();
        Console.WriteLine(message);
        Usage.Record("Yt_Title");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
}

// ---------- helpers ----------

void List(IEnumerable<Tool> tools)
{
    foreach (var t in tools) Console.WriteLine($"  {t.Id,-16} {t.Name} — {t.Description}");
}

static string Version() => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

void Header() => Console.WriteLine($"{Strings.Get("App_Name")} {Version()} — {Strings.Get("App_Tagline")}\n");

void PrintUsage()
{
    Console.WriteLine(Strings.Get("Cli_Usage"));
    Console.WriteLine(Strings.Get("Cli_Options"));
    Console.WriteLine(Strings.Get("Cli_Search"));
    Console.WriteLine(Strings.Get("Cli_HelpLine"));
}

void Examples()
{
    Console.WriteLine();
    Console.WriteLine(Strings.Get("Cli_Examples"));
    Console.WriteLine("  krate sha256 hello");
    Console.WriteLine("  echo hello | krate sha256");
    Console.WriteLine("  krate convert \"10 km mi\"");
    Console.WriteLine("  krate search color");
    Console.WriteLine("  krate convert song.wav mp3");
    Console.WriteLine("  krate gamepad");
    Console.WriteLine("  krate completion powershell");
}

void ToolHelp(Tool t)
{
    Console.WriteLine($"{t.Name} ({t.Id})");
    Console.WriteLine(t.Description);
    Console.WriteLine();
    Console.WriteLine($"{Strings.Get("Cli_UsageLabel")}  krate {t.Id.ToLowerInvariant()} <input>   |   … | krate {t.Id.ToLowerInvariant()}");
    Console.WriteLine($"{Strings.Get("Cli_KeywordsLabel")}  {string.Join(", ", t.Aliases)}");
}

int Completion(string shell)
{
    // Every completion word: the tool ids plus the top-level commands/flags.
    var words = Catalog.Tools.Select(t => t.Id.ToLowerInvariant())
        .Concat(["search", "find", "completion", "gamepad", "notepad", "edit", "clicker", "tally", "snake", "play", "2048", "tetris", "weather", "media", "stats", "youtube", "encrypt", "decrypt", "stripmetadata", "--help", "--version", "--lang"]).ToArray();
    var spaceList = string.Join(' ', words);

    switch (shell)
    {
        case "bash":
            Console.WriteLine("# krate bash completion — add to ~/.bashrc:  krate completion bash >> ~/.bashrc");
            Console.WriteLine("_crate() { COMPREPLY=($(compgen -W \"" + spaceList + "\" -- \"${COMP_WORDS[COMP_CWORD]}\")); }");
            Console.WriteLine("complete -F _crate krate");
            return 0;
        case "zsh":
            Console.WriteLine("# krate zsh completion — add to ~/.zshrc:  krate completion zsh >> ~/.zshrc");
            Console.WriteLine("_crate() { compadd " + spaceList + " }");
            Console.WriteLine("compdef _crate krate");
            return 0;
        case "powershell" or "pwsh":
            var psArray = "@('" + string.Join("','", words) + "')";
            Console.WriteLine("# krate PowerShell completion — add to $PROFILE:  krate completion powershell >> $PROFILE");
            Console.WriteLine("Register-ArgumentCompleter -Native -CommandName krate -ScriptBlock {");
            Console.WriteLine("  param($wordToComplete, $commandAst, $cursorPosition)");
            Console.WriteLine("  " + psArray + " | Where-Object { $_ -like \"$wordToComplete*\" } | ForEach-Object {");
            Console.WriteLine("    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_) } }");
            return 0;
        default:
            Console.Error.WriteLine(Strings.Get("Cli_CompletionUsage"));
            return 2;
    }
}

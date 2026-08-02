using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Krate.Core;

/// <summary>Video / audio downloading, powered by a fetched yt-dlp (works for YouTube and the many other
/// sites yt-dlp supports). yt-dlp is downloaded once into %LocalAppData%\KRATE\yt-dlp and then used
/// offline; the existing ffmpeg handles merging best video+audio and audio extraction.</summary>
// ponytail: shell out to yt-dlp, exactly like the converter shells out to ffmpeg. yt-dlp IS the tool that
// does this well and is updated constantly — binding a library would just lag it. Original wrapper code;
// yt-dlp is a standalone tool, invoked here the same way OnionMedia and countless front-ends invoke it.
public static partial class YouTube
{
    const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

    static string CacheDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KRATE", "yt-dlp");

    /// <summary>Where yt-dlp lives: next to the app (bundled), in the cache (downloaded), or on PATH.</summary>
    public static string? FindYtDlp()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe"),
            Path.Combine(CacheDir, "yt-dlp.exe"),
        ];
        foreach (var c in candidates) if (File.Exists(c)) return c;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try { var p = Path.Combine(dir.Trim(), "yt-dlp.exe"); if (File.Exists(p)) return p; } catch { /* bad PATH entry */ }
        }
        return null;
    }

    public static bool HasYtDlp => FindYtDlp() is not null;

    /// <summary>The user's Downloads folder, or their Videos folder as a fallback.</summary>
    public static string DefaultFolder
    {
        get
        {
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return Directory.Exists(downloads) ? downloads : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        }
    }

    /// <summary>Downloads yt-dlp once into the cache. No-op if it is already available.</summary>
    public static async Task EnsureYtDlpAsync(IProgress<string>? status = null, CancellationToken ct = default)
    {
        if (HasYtDlp) return;
        Directory.CreateDirectory(CacheDir);
        status?.Report(Strings.Get("Yt_Getting"));
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        await using var stream = await http.GetStreamAsync(YtDlpUrl, ct);
        await using var file = File.Create(Path.Combine(CacheDir, "yt-dlp.exe"));
        await stream.CopyToAsync(file, ct);
    }

    public record VideoInfo(string Title, string Channel, string Duration, string Thumbnail);

    /// <summary>Fetches the video's metadata (title, channel, duration, thumbnail). Returns null if yt-dlp is missing or the URL is invalid.</summary>
    public static async Task<VideoInfo?> GetVideoInfoAsync(string url, CancellationToken ct = default)
    {
        url = url.Trim();
        if (url.Length == 0) return null;
        var ytdlp = FindYtDlp();
        if (ytdlp is null) return null;

        try
        {
            var psi = new ProcessStartInfo(ytdlp)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--no-playlist");
            psi.ArgumentList.Add("--print");
            psi.ArgumentList.Add("%(title)s|%(uploader)s|%(duration_string)s|%(thumbnail)s");
            psi.ArgumentList.Add(url);

            using var process = Process.Start(psi);
            if (process is null) return null;
            
            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            
            if (process.ExitCode == 0)
            {
                var output = await outputTask;
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0)
                {
                    var parts = lines[0].Split('|');
                    if (parts.Length >= 4)
                        return new VideoInfo(parts[0].Trim(), parts[1].Trim(), parts[2].Trim(), parts[3].Trim());
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>Searches for videos matching the query and returns their titles, URLs, and thumbnails.</summary>
    public static async Task<List<(string Title, string Url, string Thumbnail)>> SearchAsync(string query, CancellationToken ct = default)
    {
        var results = new List<(string Title, string Url, string Thumbnail)>();
        query = query.Trim();
        if (query.Length == 0) return results;
        var ytdlp = FindYtDlp();
        if (ytdlp is null) return results;

        try
        {
            var psi = new ProcessStartInfo(ytdlp)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true, // We ignore stderr to hide warnings
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add($"ytsearch5:{query}");
            psi.ArgumentList.Add("--print");
            psi.ArgumentList.Add("%(title)s|https://youtube.com/watch?v=%(id)s|%(thumbnail)s");

            using var process = Process.Start(psi);
            if (process is null) return results;
            
            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            
            if (process.ExitCode == 0)
            {
                var output = await outputTask;
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length == 3) results.Add((parts[0].Trim(), parts[1].Trim(), parts[2].Trim()));
                }
            }
        }
        catch { }
        return results;
    }

    /// <summary>The formats offered in the picker: id → whether it's audio-only.</summary>
    public static readonly IReadOnlyList<(string Id, bool Audio)> Formats =
    [
        ("mp4", false), ("mkv", false), ("webm", false),
        ("mp3", true), ("m4a", true), ("wav", true),
    ];

    /// <summary>Downloads a URL into <paramref name="folder"/> in the chosen format. Reports 0..1 progress.
    /// Throws with yt-dlp's own message on failure.</summary>
    public static string Download(string url, string format, string folder, IProgress<double>? progress = null)
    {
        url = url.Trim();
        if (url.Length == 0) throw new ArgumentException(Strings.Get("Yt_NeedUrl"));
        var ytdlp = FindYtDlp() ?? throw new InvalidOperationException(Strings.Get("Yt_NoYtDlp"));
        Directory.CreateDirectory(folder);

        var ffmpeg = Media.FindFfmpeg();
        var audio = Formats.FirstOrDefault(f => f.Id == format).Audio;

        var args = new List<string> { "--no-playlist", "--newline", "--no-part", "-o", Path.Combine(folder, "%(title)s.%(ext)s") };
        if (ffmpeg is not null) { args.Add("--ffmpeg-location"); args.Add(Path.GetDirectoryName(ffmpeg)!); }

        if (audio)
        {
            if (ffmpeg is null) throw new InvalidOperationException(Strings.Get("Yt_NeedFfmpeg"));
            args.AddRange(["-x", "--audio-format", format, "--audio-quality", "0"]);
        }
        else
        {
            // Merging best video+audio needs ffmpeg; without it, fall back to the best pre-merged stream.
            args.AddRange(ffmpeg is not null ? ["-f", "bv*+ba/b", "--merge-output-format", format] : ["-f", "b"]);
        }
        args.Add(url);

        var name = Run(ytdlp, args, progress);
        return Strings.Get("Yt_Done", name.Length > 0 ? name : Path.GetFileName(folder));
    }

    static string Run(string ytdlp, IEnumerable<string> args, IProgress<double>? progress)
    {
        var psi = new ProcessStartInfo(ytdlp) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException(Strings.Get("Yt_NoYtDlp"));
        var errTask = process.StandardError.ReadToEndAsync(); // drain stderr concurrently so it can't deadlock

        string? output = null;
        var tail = new Queue<string>();
        string? line;
        while ((line = process.StandardOutput.ReadLine()) is not null)
        {
            tail.Enqueue(line);
            if (tail.Count > 15) tail.Dequeue();
            if (Merged().Match(line) is { Success: true } m) output = m.Groups[1].Value.Trim();
            else if (Dest().Match(line) is { Success: true } d) output = d.Groups[1].Value.Trim();
            else if (progress is not null && Percent().Match(line) is { Success: true } p)
                progress.Report(Math.Clamp(double.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture) / 100, 0, 1));
        }
        process.WaitForExit();
        var err = errTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException((err.Trim().Length > 0 ? err.Trim() : string.Join('\n', tail)).Trim());
        progress?.Report(1);
        return output is null ? "" : Path.GetFileName(output);
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)%")] private static partial Regex Percent();
    [GeneratedRegex(@"Destination:\s*(.+?)\s*$")] private static partial Regex Dest();
    [GeneratedRegex(@"Merging formats into ""(.+?)""")] private static partial Regex Merged();
}

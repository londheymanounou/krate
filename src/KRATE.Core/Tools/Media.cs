using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Krate.Core;

/// <summary>One output format the converter can target: the ffmpeg arguments and the file extension.</summary>
public sealed record MediaFormat(string Id, string Category, string Extension, string[] Args);

/// <summary>Audio / video / image file conversion, powered by a bundled ffmpeg. ffmpeg is fetched once
/// (the second deliberate network exception after currency) into %LocalAppData%\KRATE\ffmpeg and then
/// used fully offline. One binary covers audio, video and modern image formats (WebP/AVIF).</summary>
// ponytail: shell out to ffmpeg rather than bind a native lib — it is the one tool that does all of
// this, and the CLI and GUI share these presets. Stills that Windows already handles (png/jpg/…) also
// stay available in the dedicated Image tool; this is the "any file → any format" surface.
public static partial class Media
{
    public static readonly IReadOnlyList<MediaFormat> Formats =
    [
        // Audio (‑vn drops any video track, so "video → mp3" extracts the audio).
        new("mp3", "audio", ".mp3", ["-vn", "-c:a", "libmp3lame", "-q:a", "2"]),
        new("wav", "audio", ".wav", ["-vn", "-c:a", "pcm_s16le"]),
        new("flac", "audio", ".flac", ["-vn", "-c:a", "flac"]),
        new("ogg", "audio", ".ogg", ["-vn", "-c:a", "libvorbis", "-q:a", "5"]),
        new("aac", "audio", ".m4a", ["-vn", "-c:a", "aac", "-b:a", "192k"]),
        // Video.
        new("mp4", "video", ".mp4", ["-c:v", "libx264", "-crf", "23", "-preset", "medium", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k"]),
        new("mkv", "video", ".mkv", ["-c:v", "libx264", "-crf", "23", "-preset", "medium", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k"]),
        new("webm", "video", ".webm", ["-c:v", "libvpx-vp9", "-b:v", "0", "-crf", "32", "-c:a", "libopus"]),
        new("avi", "video", ".avi", ["-c:v", "libxvid", "-q:v", "4", "-c:a", "libmp3lame", "-q:a", "3"]),
        new("ogv", "video", ".ogv", ["-c:v", "libtheora", "-q:v", "7", "-c:a", "libvorbis", "-q:a", "5"]),
        new("gif", "video", ".gif", ["-vf", "fps=12,scale=480:-1:flags=lanczos"]),
        // Image.
        new("png", "image", ".png", ["-frames:v", "1"]),
        new("jpg", "image", ".jpg", ["-frames:v", "1", "-q:v", "3"]),
        new("webp", "image", ".webp", ["-frames:v", "1", "-c:v", "libwebp", "-quality", "80"]),
        new("avif", "image", ".avif", ["-frames:v", "1", "-c:v", "libaom-av1", "-crf", "30", "-still-picture", "1"]),
    ];

    public static MediaFormat? Format(string id) => Formats.FirstOrDefault(f => f.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    const string FfmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    static string CacheDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KRATE", "ffmpeg");

    /// <summary>Where ffmpeg lives: next to the app (bundled), in the cache (downloaded), or on PATH.</summary>
    public static string? FindFfmpeg()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(CacheDir, "ffmpeg.exe"),
        ];
        foreach (var c in candidates) if (File.Exists(c)) return c;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try { var p = Path.Combine(dir.Trim(), "ffmpeg.exe"); if (File.Exists(p)) return p; } catch { /* bad PATH entry */ }
        }
        return null;
    }

    public static bool HasFfmpeg => FindFfmpeg() is not null;

    /// <summary>Downloads ffmpeg once into the cache. No-op if it is already available.</summary>
    public static async Task EnsureFfmpegAsync(IProgress<string>? status = null, CancellationToken ct = default)
    {
        if (HasFfmpeg) return;
        Directory.CreateDirectory(CacheDir);
        var zip = Path.Combine(Path.GetTempPath(), "krate-ffmpeg.zip");
        status?.Report(Strings.Get("Media_Downloading"));
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
        await using (var stream = await http.GetStreamAsync(FfmpegUrl, ct))
        await using (var file = File.Create(zip))
            await stream.CopyToAsync(file, ct);

        status?.Report(Strings.Get("Media_Extracting"));
        using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
        {
            var entry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException(Strings.Get("Media_ExtractFailed"));
            entry.ExtractToFile(Path.Combine(CacheDir, "ffmpeg.exe"), overwrite: true);
        }
        try { File.Delete(zip); } catch { /* temp file */ }
    }

    /// <summary>Converts one file to the given format, writing the result beside the source. Reports 0..1
    /// progress if a receiver is passed. Throws with ffmpeg's own message on failure.</summary>
    public static string Convert(string inputPath, string formatId, IProgress<double>? progress = null)
    {
        inputPath = inputPath.Trim().Trim('"');
        if (!File.Exists(inputPath)) throw new ArgumentException(Strings.Get("Error_NoFile", inputPath));
        var format = Format(formatId) ?? throw new ArgumentException(Strings.Get("Media_UnknownFormat", formatId));
        var ffmpeg = FindFfmpeg() ?? throw new InvalidOperationException(Strings.Get("Media_NoFfmpeg"));

        var output = UniquePath(inputPath, format.Extension);
        var args = new List<string> { "-hide_banner", "-y", "-i", inputPath };
        args.AddRange(format.Args);
        args.Add(output);
        Run(ffmpeg, args, progress);

        if (!File.Exists(output)) throw new InvalidOperationException(Strings.Get("Media_NoOutput"));
        return Strings.Get("Media_Done", Path.GetFileName(output), HumanSize(new FileInfo(output).Length));
    }

    /// <summary>Removes metadata from audio or video using ffmpeg.</summary>
    public static string StripMetadata(string inputPath, string outputPath)
    {
        inputPath = inputPath.Trim().Trim('"');
        if (!File.Exists(inputPath)) throw new ArgumentException(Strings.Get("Error_NoFile", inputPath));
        var ffmpeg = FindFfmpeg() ?? throw new InvalidOperationException(Strings.Get("Media_NoFfmpeg"));
        
        var args = new List<string> { "-hide_banner", "-y", "-i", inputPath, "-map_metadata", "-1", "-c", "copy", outputPath };
        Run(ffmpeg, args, null);
        
        if (!File.Exists(outputPath)) throw new InvalidOperationException(Strings.Get("Media_NoOutput"));
        return Strings.Get("ImageMetadata_Success", outputPath);
    }

    static void Run(string ffmpeg, IEnumerable<string> args, IProgress<double>? progress)
    {
        var psi = new ProcessStartInfo(ffmpeg) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException(Strings.Get("Media_NoFfmpeg"));
        double totalSeconds = 0;
        var tail = new Queue<string>();
        string? line;
        while ((line = process.StandardError.ReadLine()) is not null)
        {
            tail.Enqueue(line);
            if (tail.Count > 12) tail.Dequeue();   // keep only the last lines for an error message
            if (totalSeconds == 0 && Duration().Match(line) is { Success: true } d) totalSeconds = Ts(d);
            else if (progress is not null && totalSeconds > 0 && TimePos().Match(line) is { Success: true } t)
                progress.Report(Math.Clamp(Ts(t) / totalSeconds, 0, 1));
        }
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(string.Join('\n', tail).Trim());
        progress?.Report(1);
    }

    static double Ts(Match m) =>
        int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);

    [GeneratedRegex(@"Duration:\s*(\d+):(\d+):(\d+\.\d+)")] private static partial Regex Duration();
    [GeneratedRegex(@"time=\s*(\d+):(\d+):(\d+\.\d+)")] private static partial Regex TimePos();

    // "song.wav" → "song.mp3"; if that exists (or equals the input), add " (1)", " (2)"…
    static string UniquePath(string input, string extension)
    {
        var dir = Path.GetDirectoryName(input) ?? ".";
        var name = Path.GetFileNameWithoutExtension(input);
        var candidate = Path.Combine(dir, name + extension);
        for (var n = 1; (File.Exists(candidate) && !string.Equals(candidate, input, StringComparison.OrdinalIgnoreCase)) || string.Equals(candidate, input, StringComparison.OrdinalIgnoreCase); n++)
            candidate = Path.Combine(dir, $"{name} ({n}){extension}");
        return candidate;
    }

    static string HumanSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        // Interpolated "0.#" would use CurrentCulture, so a French machine reported "1,5 GB" while
        // Files.HumanSize beside it says "1.5 GB". Same leak as Exif's rationals had.
        return string.Create(CultureInfo.InvariantCulture, $"{size:0.#} {units[unit]}");
    }
}

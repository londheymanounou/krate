using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace Krate.Core;

public static class Notepad
{
    static readonly string _binDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KRATE", "micro");
    static readonly string _cachedExe = Path.Combine(_binDir, "micro-2.0.13", "micro.exe");

    /// <summary>Where micro actually is: bundled beside the app first, then the download cache.
    ///
    /// This used to point only at the cache, so a copy shipped with the app was never found and the
    /// editor downloaded itself on first use even when it was already there. Media and YouTube both
    /// look beside the app first; this now matches them.</summary>
    static string Exe
    {
        get
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "micro.exe");
            return File.Exists(bundled) ? bundled : _cachedExe;
        }
    }

    public static bool HasMicro => File.Exists(Exe);

    public static async Task EnsureMicroAsync(IProgress<string> progress)
    {
        if (HasMicro) return;
        progress.Report("Downloading terminal notepad (micro)...");
        Directory.CreateDirectory(_binDir);
        var zipPath = Path.Combine(_binDir, "micro.zip");
        
        using var client = new HttpClient();
        var bytes = await client.GetByteArrayAsync("https://github.com/zyedidia/micro/releases/download/v2.0.13/micro-2.0.13-win64.zip");
        await File.WriteAllBytesAsync(zipPath, bytes);
        
        progress.Report("Extracting notepad...");
        ZipFile.ExtractToDirectory(zipPath, _binDir, overwriteFiles: true);
        File.Delete(zipPath);
    }

    public static int Run(string? file)
    {
        var psi = new ProcessStartInfo(Exe)
        {
            UseShellExecute = false
        };
        if (!string.IsNullOrWhiteSpace(file))
        {
            psi.ArgumentList.Add(file);
        }
        var process = Process.Start(psi);
        process?.WaitForExit();
        return process?.ExitCode ?? 1;
    }
}

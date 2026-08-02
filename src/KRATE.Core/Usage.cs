using System.Globalization;

namespace Krate.Core;

/// <summary>Counts how often each tool is used, so the app can show what you reach for most. One
/// "id=count" line per tool in a plain text file — no database, same spirit as <see cref="Settings"/>.
/// CLI and GUI share this one file, so their counts combine. Every operation reads the file fresh and
/// rewrites it, so a count made in one process isn't clobbered by a stale snapshot in the other.
/// Everything is best-effort: statistics must never break a tool or slow it perceptibly.</summary>
// ponytail: read-modify-write, no file lock. Two processes incrementing in the same instant could lose
// one count — add a lock file only if that ever matters (it won't at open/run frequency).
public static class Usage
{
    static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KRATE", "usage.txt");

    static Dictionary<string, int> Load()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(Path))
                foreach (var line in File.ReadAllLines(Path))
                {
                    var i = line.LastIndexOf('=');
                    if (i > 0 && int.TryParse(line[(i + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                        d[line[..i].Trim()] = n;
                }
        }
        catch { /* ignore a corrupt/locked file — start fresh */ }
        return d;
    }

    /// <summary>Records one use of a tool id (or GUI page key). Merges with the current file first so a
    /// CLI run and an open GUI don't overwrite each other's counts. Never throws into the caller.</summary>
    public static void Record(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        try
        {
            var counts = Load();
            counts[id] = counts.GetValueOrDefault(id) + 1;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllLines(Path, counts.Select(kv => $"{kv.Key}={kv.Value}"));
        }
        catch { /* best-effort */ }
    }

    /// <summary>Tools by use count, most-used first (read live, so it includes the other process's uses).</summary>
    public static IReadOnlyList<(string Id, int Count)> Ranked() =>
        Load().Where(kv => kv.Value > 0)
              .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
              .Select(kv => (kv.Key, kv.Value)).ToList();

    public static int Total() => Load().Values.Sum();

    public static void Reset()
    {
        try { if (File.Exists(Path)) File.Delete(Path); } catch { }
    }

    /// <summary>Human name for a usage key: a catalog tool's localized name, else a GUI page's title
    /// (the key is a resource key like "Clock_Title"), else the raw key.</summary>
    public static string DisplayName(string id) => Catalog.Find(id)?.Name ?? Strings.Get(id);
}

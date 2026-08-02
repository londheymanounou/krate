using System.Globalization;
using System.Resources;

namespace Krate.Core;

/// <summary>All user-facing text. Keys are dynamic ("Tool_Sha256_Name"), so we use
/// ResourceManager directly instead of a generated strongly-typed wrapper.</summary>
public static class Strings
{
    static readonly ResourceManager Rm = new("Krate.Core.Resources.Strings", typeof(Strings).Assembly);

    /// <summary>UI culture. Defaults to the OS language; overridden by the persisted setting.</summary>
    public static CultureInfo Culture { get; set; } = Settings.Language is { } l
        ? CultureInfo.GetCultureInfo(l)
        : CultureInfo.CurrentUICulture;

    // ponytail: missing key returns the key itself. Never empty, never throws — visible in dev, harmless in prod.
    public static string Get(string key) => Rm.GetString(key, Culture) ?? key;

    public static string Get(string key, params object[] args) =>
        string.Format(Culture, Get(key), args);
}

/// <summary>Local settings. One key=value line per setting, no database, no JSON dependency.</summary>
public static class Settings
{
    static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KRATE", "settings.txt");

    static readonly Dictionary<string, string> Values = Load();

    static Dictionary<string, string> Load()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(Path)) return d;
        foreach (var line in File.ReadAllLines(Path))
        {
            var i = line.IndexOf('=');
            if (i > 0) d[line[..i].Trim()] = line[(i + 1)..].Trim();
        }
        return d;
    }

    public static string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;

    public static void Set(string key, string value)
    {
        Values[key] = value;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllLines(Path, Values.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    /// <summary>Recently-opened tool ids, newest first.</summary>
    public static IReadOnlyList<string> Recents =>
        (Get("recents") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static void PushRecent(string id) => Set("recents", string.Join(',', Mru.Update(Recents, id)));

    /// <summary>Pinned tool ids, in the order they were starred.</summary>
    public static IReadOnlyList<string> Favorites =>
        (Get("favorites") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool IsFavorite(string id) => Favorites.Contains(id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Stars an unstarred tool, or unstars a starred one.</summary>
    public static void ToggleFavorite(string id)
    {
        var list = Favorites.ToList();
        if (list.RemoveAll(f => f.Equals(id, StringComparison.OrdinalIgnoreCase)) == 0) list.Add(id);
        Set("favorites", string.Join(',', list));
    }

    /// <summary>Manual language override, or null to follow the OS.</summary>
    public static string? Language
    {
        get => Get("language") is { Length: > 0 } v ? v : null;
        set
        {
            Set("language", value ?? "");
            Strings.Culture = value is null ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(value);
            // The native core keeps its own copy of the language, so it has to be told too.
            RustCore.SetLanguage(Strings.Culture);
        }
    }
}

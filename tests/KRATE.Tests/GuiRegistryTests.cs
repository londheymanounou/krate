using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Krate.Core;
using Xunit;

/// <summary>The GUI cannot be instantiated from a test process — WinUI needs its own application
/// host and most pages take a window handle and open file pickers. What CAN be checked is the
/// wiring, and that is where this layer actually breaks: a page is registered against a resource
/// key or a tool id that no longer exists, nothing throws, and the UI quietly shows a raw key or
/// a duplicate entry. So these read the GUI source as text and verify it against Core.
///
/// This is how the BulkRename -> Rename catalog rename was caught: the page still claimed to
/// replace "BulkRename", so the tool appeared twice in the tool list for months.</summary>
public class GuiRegistryTests
{
    public GuiRegistryTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    static string GuiDir => Path.Combine(FindRepoRoot(), "src", "KRATE.Gui");
    static string ResourceDir => Path.Combine(FindRepoRoot(), "src", "KRATE.Core", "Resources");

    static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "KRATE.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new DirectoryNotFoundException("KRATE.sln not found above " + AppContext.BaseDirectory);
    }

    /// <summary>One row of MainWindow's `_interactive` table: the category it files under, the
    /// resource key for its title, and the catalog tool ids the page takes over from.</summary>
    record Entry(string Category, string NameKey, string[] Replaces);

    static readonly Lazy<Entry[]> Registry = new(() =>
    {
        var source = File.ReadAllText(Path.Combine(GuiDir, "MainWindow.xaml.cs"));
        var start = source.IndexOf("_interactive =", StringComparison.Ordinal);
        var end = source.IndexOf("ShowTools(Catalog.Tools)", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "could not locate the _interactive table in MainWindow.xaml.cs");

        var rows = Regex.Matches(source[start..end], """\("(\w+)",\s*"(\w+)",\s*.+?,\s*\[(.*?)\]\)""", RegexOptions.Singleline);
        return rows.Select(m => new Entry(
            m.Groups[1].Value,
            m.Groups[2].Value,
            Regex.Matches(m.Groups[3].Value, "\"(\\w+)\"").Select(r => r.Groups[1].Value).ToArray())).ToArray();
    });

    static HashSet<string> ResourceKeys(string file) =>
        XDocument.Load(Path.Combine(ResourceDir, file)).Root!.Elements("data")
            .Select(d => d.Attribute("name")!.Value).ToHashSet();

    [Fact]
    public void TheRegistry_WasActuallyParsed()
    {
        // Guards the tests below: if the table's shape changes and the regex silently matches
        // nothing, every other test here would pass vacuously.
        Assert.True(Registry.Value.Length > 50, $"only parsed {Registry.Value.Length} interactive pages");
        Assert.Contains(Registry.Value, e => e.NameKey == "Tool_Regex_Name");
    }

    [Fact]
    public void EveryPageTitle_ResolvesInEveryLanguage()
    {
        var missing = new List<string>();
        foreach (var file in Directory.GetFiles(ResourceDir, "Strings*.resx"))
        {
            var keys = ResourceKeys(Path.GetFileName(file));
            // The neutral file is the fallback, so only it has to carry every key.
            if (Path.GetFileName(file) != "Strings.resx") continue;
            missing.AddRange(Registry.Value.Select(e => e.NameKey).Distinct()
                .Where(k => !keys.Contains(k))
                .Select(k => $"{Path.GetFileName(file)}: {k}"));
        }
        Assert.Empty(missing);
    }

    /// <summary>A page "replaces" the plain text-box version of a tool. If the id is wrong the
    /// replacement silently does nothing and the tool is listed twice — once as a real page and
    /// once as a text box.</summary>
    [Fact]
    public void EveryReplacedToolId_ExistsInTheCatalog()
    {
        var ids = Catalog.Tools.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var unknown = Registry.Value
            .SelectMany(e => e.Replaces.Select(r => (e.NameKey, Replaced: r)))
            .Where(x => !ids.Contains(x.Replaced))
            .Select(x => $"{x.NameKey} claims to replace '{x.Replaced}', which is not a catalog tool")
            .ToList();
        Assert.Empty(unknown);
    }

    [Fact]
    public void NoToolIsClaimedByTwoPages()
    {
        var duplicates = Registry.Value
            .SelectMany(e => e.Replaces)
            .GroupBy(r => r, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"'{g.Key}' is replaced by {g.Count()} pages")
            .ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void EveryPage_FilesUnderARealCategory()
    {
        var categories = Catalog.Tools.Select(t => t.Category).ToHashSet(StringComparer.Ordinal);
        categories.Add("Settings");     // the settings page is a page, not a tool
        var unknown = Registry.Value.Select(e => e.Category).Distinct()
            .Where(c => !categories.Contains(c)).ToList();
        Assert.Empty(unknown);
    }

    /// <summary>Catches the failure mode this project already shipped: Strings.Get echoes the key
    /// back when it is missing, so a typo renders in the window as "Tool_Foo_Name".</summary>
    [Fact]
    public void EveryStringKeyTheGuiAsksFor_Exists()
    {
        var keys = ResourceKeys("Strings.resx");
        var missing = new List<string>();
        foreach (var file in Directory.GetFiles(GuiDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"Strings\.Get\(\s*""([^""]+)"""))
                if (!keys.Contains(m.Groups[1].Value))
                    missing.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryRegisteredPageType_HasASourceFile()
    {
        var source = File.ReadAllText(Path.Combine(GuiDir, "MainWindow.xaml.cs"));
        var start = source.IndexOf("_interactive =", StringComparison.Ordinal);
        var end = source.IndexOf("ShowTools(Catalog.Tools)", StringComparison.Ordinal);

        var types = Regex.Matches(source[start..end], @"new (?:Games\.)?(\w+Page)\(")
            .Select(m => m.Groups[1].Value).Distinct();
        var files = Directory.GetFiles(GuiDir, "*.xaml.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetFileName(f).Replace(".xaml.cs", "")).ToHashSet();

        var orphans = types.Where(t => !files.Contains(t)).ToList();
        Assert.Empty(orphans);
    }

    /// <summary>A full Tool_X_Name/_Desc/_Aliases triple with no matching catalog entry is dead
    /// weight that still has to be translated into all 17 languages.</summary>
    [Fact]
    public void NoDeadToolStringsAreShipped()
    {
        var ids = Catalog.Tools.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var keys = ResourceKeys("Strings.resx");
        var dead = keys.Select(k => Regex.Match(k, @"^Tool_(\w+)_Name$"))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .Where(id => !ids.Contains(id)
                         && keys.Contains($"Tool_{id}_Desc")
                         && keys.Contains($"Tool_{id}_Aliases"))
            .ToList();
        Assert.Empty(dead);
    }
}

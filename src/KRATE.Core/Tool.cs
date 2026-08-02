namespace Krate.Core;

/// <summary>A tool the GUI and CLI both surface. Localized text lives in resources,
/// keyed by Id: Tool_{Id}_Name, Tool_{Id}_Desc, Tool_{Id}_Aliases.</summary>
// ponytail: string in / string out covers most of the catalog. Tools needing options or files
// get their own Core API and a hand-written page; the registry stays the search index.
public sealed record Tool(string Id, string Category, Func<string, string> CSharp)
{
    /// <summary>Runs the tool. The Rust core is the implementation that actually runs; the managed
    /// one behind <see cref="CSharp"/> is the fallback when the native library is unavailable, and
    /// the reference the parity tests hold it to.</summary>
    public string Run(string input) =>
        RustCore.Available ? RustCore.Run(Id, input) : CSharp(input);

    public string Name => Strings.Get($"Tool_{Id}_Name");
    public string Description => Strings.Get($"Tool_{Id}_Desc");
    public string CategoryName => Strings.Get($"Category_{Category}");

    /// <summary>Localized search keywords, comma-separated in resources.</summary>
    public IEnumerable<string> Aliases =>
        Strings.Get($"Tool_{Id}_Aliases").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool Matches(string query) =>
        Id.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Aliases.Any(a => a.Contains(query, StringComparison.OrdinalIgnoreCase));
}

public static class Catalog
{
    public static readonly IReadOnlyList<Tool> Tools =
    [
        // Text
        new("Count", "Text", Text.Count),
        new("Upper", "Text", Text.Upper),
        new("Lower", "Text", Text.Lower),
        new("Title", "Text", Text.Title),
        new("Invert", "Text", Text.Invert),
        new("Naming", "Text", Text.Naming),
        new("Slug", "Text", Text.Slug),
        new("Deaccent", "Text", Text.Deaccent),
        new("Clean", "Text", Text.Clean),
        new("SortLines", "Text", Text.SortLines),
        new("SortByLength", "Text", Text.SortByLength),
        new("Dedupe", "Text", Text.Dedupe),
        new("Reverse", "Text", Text.ReverseText),
        new("ReverseLines", "Text", Text.ReverseLines),
        new("WordFrequency", "Text", Text.WordFrequency),
        new("FrenchTypography", "Text", Text.FrenchTypography),
        new("Mask", "Text", Text.Mask),
        new("Lorem", "Text", Text.Lorem),
        new("Morse", "Text", Text.Morse),
        new("Diff", "Text", Text.Diff),
        new("Fancy", "Text", Fancy.Convert),
        new("Toc", "Text", Text.Toc),
        new("MarkdownTable", "Text", Text.MarkdownTable),
        new("Zalgo", "Text", Text.Zalgo),

        // Encoding
        new("Base64", "Encoding", Encodings.Base64Encode),
        new("Base64Decode", "Encoding", Encodings.Base64Decode),
        new("UrlEncode", "Encoding", Encodings.UrlEncode),
        new("UrlDecode", "Encoding", Encodings.UrlDecode),
        new("HtmlEncode", "Encoding", Encodings.HtmlEncode),
        new("HtmlDecode", "Encoding", Encodings.HtmlDecode),
        new("Bases", "Encoding", Encodings.Bases),
        new("JsonEscape", "Encoding", Escapes.Json),
        new("JsonUnescape", "Encoding", Escapes.JsonUnescape),
        new("SqlEscape", "Encoding", Escapes.Sql),
        new("ShellEscape", "Encoding", Escapes.Shell),
        new("Jwt", "Encoding", Escapes.Jwt),
        new("Scientific", "Encoding", Escapes.Scientific),
        new("CsvToJson", "Encoding", Data.CsvToJson),
        new("JsonToCsv", "Encoding", Data.JsonToCsv),
        new("JsonToYaml", "Encoding", Data.JsonToYaml),
        new("MarkdownToHtml", "Encoding", Markdown.ToHtml),
        new("Cron", "Encoding", Cron.Describe),

        // Hashing & security
        new("Md5", "Hashing", Hashing.Md5),
        new("Sha1", "Hashing", Hashing.Sha1),
        new("Sha256", "Hashing", Hashing.Sha256),
        new("Sha512", "Hashing", Hashing.Sha512),
        new("HashAll", "Hashing", Hashing.All),
        new("Password", "Hashing", Generators.Password),
        new("PasswordStrength", "Hashing", Security.Strength),
        new("Uuid", "Hashing", Generators.Uuid),
        new("Encrypt", "Hashing", Crypt.Encrypt),
        new("Decrypt", "Hashing", Crypt.Decrypt),

        // Developer
        new("JsonFormat", "Developer", Json.Format),
        new("JsonMinify", "Developer", Json.Minify),
        new("JsonValidate", "Developer", Json.Validate),
        new("XmlFormat", "Developer", Dev.XmlFormat),
        new("XmlValidate", "Developer", Dev.XmlValidate),
        new("Regex", "Developer", Dev.RegexTest),
        new("QueryString", "Developer", Dev.QueryString),
        new("Crlf", "Developer", Escapes.ToCrlf),
        new("Lf", "Developer", Escapes.ToLf),
        new("PathConvert", "Developer", Escapes.Path),
        new("FilenameClean", "Developer", Escapes.Filename),
        new("Gitignore", "Developer", Dev.Gitignore),
        new("HexDump", "Developer", Dev.HexDump),
        new("SqlFormat", "Developer", Dev.SqlFormat),
        new("UrlParse", "Developer", Dev.UrlParse),
        new("Chmod", "Developer", Dev.Chmod),
        new("HttpStatus", "Developer", Dev.HttpStatus),

        // Colors
        new("Color", "Colors", Colors.Describe),
        new("Palette", "Colors", Colors.Palette),
        new("Contrast", "Colors", Colors.Contrast),
        new("ColorBlind", "Colors", Colors.ColorBlind),
        new("ColorTemp", "Colors", Colors.Temperature),
        new("CssUnits", "Colors", Css.Units),
        new("Gradient", "Colors", Css.Gradient),
        new("CssMinify", "Developer", Css.Minify),

        // Conversions
        new("Convert", "Conversions", Units.Convert),
        new("Roman", "Conversions", Units.Roman),
        new("Spell", "Conversions", Words.Spell),
        new("SpeedDistanceTime", "Conversions", Physics.Solve),
        new("TransferTime", "Conversions", Transfer.Time),
        new("Currency", "Conversions", Currency.Convert),
        new("ShoeSize", "Conversions", Sizes.Shoe),

        // Maths
        new("Calc", "Maths", Calc.Evaluate),
        new("Combinatorics", "Maths", Maths.Combinatorics),
        new("Percent", "Maths", Maths.Percent),
        new("Fraction", "Maths", Maths.Fraction),
        new("Factor", "Maths", Maths.Factor),
        new("Statistics", "Maths", Maths.Statistics),
        new("Sequence", "Maths", Maths.Sequence),
        new("Solve", "Maths", Maths.Solve),

        // Files — drop a file or a folder on the window to fill in the path
        new("FileHash", "Files", Files.Describe),
        new("FileCompare", "Files", Files.Compare),
        new("Tree", "Files", Files.Tree),
        new("FolderSize", "Files", Files.FolderSize),
        new("Duplicates", "Files", Files.Duplicates),
        new("FileSplit", "Files", Files.Split),
        new("FileJoin", "Files", Files.Join),
        new("TestFile", "Files", Files.TestFile),
        new("Zip", "Files", Files.Compress),
        new("Unzip", "Files", Files.Extract),
        new("Rename", "Files", Files.BulkRename),
        new("PdfSplit", "Files", Pdf.Split),
        new("PdfMerge", "Files", Pdf.Merge),

        // Images
        new("StripMetadata", "Images", Files.StripMetadata),
        new("ImageInfo", "Images", Images.Dimensions),
        new("Exif", "Images", Exif.Read),
        new("AspectRatio", "Images", Images.Ratio),

        // Developer (visual)
        new("Qr", "Developer", Qr.Unicode),
        new("Barcode", "Developer", Barcode.Code128),

        // Everyday
        new("Bmi", "Everyday", Everyday.Bmi),
        new("Tip", "Everyday", Everyday.Tip),
        new("Loan", "Everyday", Everyday.Loan),
        new("Subnet", "Everyday", Everyday.Subnet),
        new("SysInfo", "Everyday", Everyday.SysInfo),
        new("Weather", "Everyday", Everyday.Weather),
        new("Snake", "Everyday", Everyday.Snake),
        new("Game2048", "Everyday", Everyday.Game2048),
        new("Tetris", "Everyday", Everyday.Tetris),

        // Dates
        new("Timestamp", "Dates", Dates.Timestamp),
        new("DateDiff", "Dates", Dates.Difference),
        new("Timezone", "Dates", Dates.Timezone),
        new("Duration", "Dates", Dates.Duration),
        new("WeekInfo", "Dates", Dates.WeekInfo),

        // Randomness
        new("Random", "Random", Generators.RandomNumber),
        new("Dice", "Random", Generators.Dice),
        new("Coin", "Random", Generators.Coin),
        new("Pick", "Random", Generators.Pick),
        new("Shuffle", "Random", Generators.Shuffle),
        new("RandomColor", "Random", Generators.RandomColor),
        new("Cards", "Random", Generators.Cards),
        new("Teams", "Random", Generators.Teams),
        
        // New Additions
        new("PortLookup", "Developer", Dev.PortLookup),
        new("MimeType", "Developer", Dev.MimeTypeLookup),
        new("DnsLookup", "Developer", Dev.DnsLookup),
        new("CurlToCode", "Developer", Dev.CurlToCode),
        new("EnvVars", "Developer", Dev.EnvVars),
        new("Inspector", "Text", Text.Inspector),
        new("CaseConverter", "Text", Text.CaseConverter),
    ];

    public static Tool? Find(string id) =>
        Tools.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    // Search hits every tool on every keystroke, and Name/Aliases each cost a ResourceManager lookup
    // (+ a split). Precompute one lowercased blob per tool, rebuilt only when the UI language changes.
    static readonly Dictionary<string, string> _index = new();
    static System.Globalization.CultureInfo? _indexed;

    static void EnsureIndex()
    {
        if (ReferenceEquals(_indexed, Strings.Culture)) return;
        _indexed = Strings.Culture;
        _index.Clear();
        foreach (var t in Tools)
            _index[t.Id] = $"{t.Id} {t.Name} {string.Join(' ', t.Aliases)}".ToLowerInvariant();
    }

    public static IEnumerable<Tool> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Tools;
        EnsureIndex();
        var q = query.Trim().ToLowerInvariant();
        return Tools.Where(t => _index[t.Id].Contains(q, StringComparison.Ordinal));
    }
}

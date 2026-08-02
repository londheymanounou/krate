using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Krate.Core;

public static class Dev
{
    // Compact .gitignore fragments. Deliberately small — the common 90%, not github/gitignore's every case.
    // ponytail: hand-maintained list; extend when a language people actually use is missing.
    static readonly Dictionary<string, string[]> GitignoreTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["node"] = ["node_modules/", "npm-debug.log*", "yarn-error.log", ".npm/", "dist/", ".env", ".env.local"],
        ["python"] = ["__pycache__/", "*.py[cod]", ".venv/", "venv/", "*.egg-info/", ".pytest_cache/", ".mypy_cache/", "build/", "dist/"],
        ["dotnet"] = ["bin/", "obj/", "*.user", ".vs/", "*.suo", "TestResults/"],
        ["csharp"] = ["bin/", "obj/", "*.user", ".vs/"],
        ["rust"] = ["/target/", "Cargo.lock", "**/*.rs.bk"],
        ["go"] = ["*.exe", "*.test", "*.out", "/vendor/", "/bin/"],
        ["java"] = ["*.class", "target/", "*.jar", ".gradle/", "build/"],
        ["macos"] = [".DS_Store", ".AppleDouble", "._*", ".Spotlight-V100", ".Trashes"],
        ["windows"] = ["Thumbs.db", "Desktop.ini", "$RECYCLE.BIN/", "*.lnk"],
        ["visualstudio"] = [".vs/", "*.user", "bin/", "obj/"],
        ["jetbrains"] = [".idea/", "*.iml", "out/"],
        ["vscode"] = [".vscode/*", "!.vscode/settings.json", "!.vscode/extensions.json"],
    };

    /// <summary>".gitignore for the named tools/languages (comma or space separated), each as a labelled block.</summary>
    public static string Gitignore(string input)
    {
        var names = input.Split([',', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0) throw new ArgumentException(Strings.Get("Error_GitignoreUsage", string.Join(", ", GitignoreTemplates.Keys)));

        var blocks = new List<string>();
        var unknown = new List<string>();
        foreach (var name in names)
        {
            if (GitignoreTemplates.TryGetValue(name, out var lines))
                blocks.Add($"# {name}\n{string.Join('\n', lines)}");
            else unknown.Add(name);
        }
        if (blocks.Count == 0) throw new ArgumentException(Strings.Get("Error_GitignoreUnknown", string.Join(", ", unknown), string.Join(", ", GitignoreTemplates.Keys)));
        return string.Join("\n\n", blocks);
    }

    const int HexDumpLimit = 64 * 1024; // cap so dumping a huge file can't hang the UI

    /// <summary>Classic hex dump of a file (if the input is a path) or of the text itself:
    /// offset, 16 bytes of hex, and the printable ASCII.</summary>
    public static string HexDump(string input)
    {
        var trimmed = input.Trim().Trim('"');
        var bytes = File.Exists(trimmed)
            ? File.ReadAllBytes(trimmed).Take(HexDumpLimit).ToArray()
            : Encoding.UTF8.GetBytes(input);

        var sb = new StringBuilder();
        for (var offset = 0; offset < bytes.Length; offset += 16)
        {
            var row = bytes.AsSpan(offset, Math.Min(16, bytes.Length - offset));
            var hex = new StringBuilder();
            var ascii = new StringBuilder();
            for (var i = 0; i < 16; i++)
            {
                if (i < row.Length) { hex.Append($"{row[i]:x2} "); ascii.Append(row[i] is >= 0x20 and < 0x7F ? (char)row[i] : '.'); }
                else hex.Append("   ");
                if (i == 7) hex.Append(' '); // the traditional gap after 8 bytes
            }
            sb.AppendLine($"{offset:x8}  {hex}|{ascii}|");
        }
        if (File.Exists(trimmed) && new FileInfo(trimmed).Length > HexDumpLimit)
            sb.Append(Strings.Get("Files_TreeTruncated", HexDumpLimit));
        return sb.ToString().TrimEnd('\n', '\r');
    }

    public static string XmlFormat(string xml)
    {
        XDocument doc;
        // Parse threw a raw XmlException with .NET's own English message — the same leak
        // XmlValidate and the JSON tools had. Report the location in the interface language.
        try { doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace); }
        catch (XmlException ex)
        {
            throw new ArgumentException(Strings.Get("Xml_Invalid", ex.LineNumber, ex.LinePosition));
        }

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = doc.Declaration is null }))
            doc.Save(writer);
        return sb.ToString();
    }

    public static string XmlValidate(string xml)
    {
        try
        {
            XDocument.Parse(xml);
            return Strings.Get("Xml_Valid");
        }
        // ex.Message is .NET's own English text, and it repeats the position the localized part
        // already gives ("...column 6: ... Line 1, position 6."). Same leak Json.Validate had.
        catch (XmlException ex)
        {
            return Strings.Get("Xml_Invalid", ex.LineNumber, ex.LinePosition);
        }
    }

    /// <summary>First line is the pattern, the rest is the subject text.
    /// Reports every match with its position and its capture groups.</summary>
    public static string RegexTest(string input)
    {
        var split = input.Replace("\r\n", "\n").Split('\n', 2);
        if (split.Length < 2 || split[0].Trim().Length == 0) throw new ArgumentException(Strings.Get("Error_RegexUsage"));

        var pattern = split[0].Trim();
        var subject = split[1];
        // A pasted pattern often still wears its /…/flags delimiters.
        var options = RegexOptions.None;
        if (pattern.Length > 2 && pattern[0] == '/' && pattern.LastIndexOf('/') > 0)
        {
            var end = pattern.LastIndexOf('/');
            foreach (var flag in pattern[(end + 1)..])
                options |= flag switch
                {
                    'i' => RegexOptions.IgnoreCase,
                    'm' => RegexOptions.Multiline,
                    's' => RegexOptions.Singleline,
                    'x' => RegexOptions.IgnorePatternWhitespace,
                    _ => RegexOptions.None,
                };
            pattern = pattern[1..end];
        }

        // User-supplied patterns can backtrack forever; a timeout keeps the app responsive.
        var regex = new Regex(pattern, options, TimeSpan.FromSeconds(2));
        var matches = regex.Matches(subject);
        if (matches.Count == 0) return Strings.Get("Regex_NoMatch");

        var lines = new List<string> { Strings.Get("Regex_MatchCount", matches.Count) };
        foreach (Match m in matches)
        {
            lines.Add($"@{m.Index,-5} {m.Value}");
            foreach (Group g in m.Groups)
                if (g.Name != "0" && g.Success) lines.Add($"        {g.Name} = {g.Value}");
        }
        return string.Join('\n', lines);
    }

    static readonly string[] SqlClauses =
    [
        "SELECT", "FROM", "WHERE", "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "FULL JOIN", "CROSS JOIN", "JOIN",
        "GROUP BY", "ORDER BY", "HAVING", "LIMIT", "OFFSET", "UNION ALL", "UNION",
        "INSERT INTO", "VALUES", "UPDATE", "SET", "DELETE FROM",
    ];
    static readonly string[] SqlKeywords =
    [
        "AND", "OR", "NOT", "IN", "AS", "ON", "IS", "NULL", "LIKE", "BETWEEN", "DISTINCT", "COUNT",
        "ASC", "DESC", "INT", "TRUE", "FALSE", "EXISTS", "CASE", "WHEN", "THEN", "ELSE", "END",
    ];

    /// <summary>Light SQL formatter: uppercases keywords and puts each major clause on its own line.
    /// Not a full parser — it won't reindent nested subqueries, just makes a flat query readable.</summary>
    // ponytail: token-level, no AST. Good enough to read a query; a real pretty-printer is a library.
    public static string SqlFormat(string sql)
    {
        // Collapse whitespace first so the clause breaks are the only line breaks.
        var flat = Regex.Replace(sql.Trim(), @"\s+", " ");
        if (flat.Length == 0) throw new ArgumentException(Strings.Get("Error_NeedText"));

        // Uppercase standalone keywords (word boundaries) without touching identifiers/strings.
        foreach (var word in SqlClauses.Concat(SqlKeywords))
            flat = Regex.Replace(flat, $@"\b{word.Replace(" ", @"\s+")}\b", word, RegexOptions.IgnoreCase);

        // Break before each major clause; the leading one loses its blank line via TrimStart.
        foreach (var clause in SqlClauses)
            flat = Regex.Replace(flat, $@"\s+{Regex.Escape(clause)}\b", $"\n{clause}");

        // Indent the comma-separated items and boolean continuations for a readable shape.
        var lines = flat.Split('\n').Select(l => l.Trim());
        return string.Join('\n', lines).Trim();
    }

    /// <summary>Percent-decodes nothing and formats everything: a query string, one parameter per line.</summary>
    public static string QueryString(string input)
    {
        var s = input.Trim();
        var start = s.IndexOf('?');
        var query = start >= 0 ? s[(start + 1)..] : s;
        var pairs = query.Split(['&', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Select(p => $"{Uri.UnescapeDataString(p[0])}  =  {(p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : "")}");
        var lines = start > 0 ? [$"URL   {s[..start]}", ""] : Array.Empty<string>();
        return string.Join('\n', lines.Concat(pairs));
    }

    public static string UrlParse(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            throw new ArgumentException(Strings.Get("Error_UrlUsage"));

        var sb = new StringBuilder();
        sb.AppendLine($"Scheme: {uri.Scheme}");
        sb.AppendLine($"Host:   {uri.Host}");
        if (!uri.IsDefaultPort) sb.AppendLine($"Port:   {uri.Port}");
        sb.AppendLine($"Path:   {uri.AbsolutePath}");
        
        if (!string.IsNullOrEmpty(uri.Query))
        {
            sb.AppendLine();
            sb.AppendLine("Query Parameters:");
            var pairs = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in pairs)
            {
                var kv = p.Split('=', 2);
                sb.AppendLine($"- {Uri.UnescapeDataString(kv[0])}: {(kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "")}");
            }
        }
        if (!string.IsNullOrEmpty(uri.Fragment)) sb.AppendLine($"\nFragment: {uri.Fragment}");
        return sb.ToString().Trim();
    }

    public static string Chmod(string input)
    {
        var s = input.Trim();
        if (Regex.IsMatch(s, "^[0-7]{3,4}$"))
        {
            int val = Convert.ToInt32(s, 8);
            var result = new StringBuilder();
            if (s.Length == 4)
            {
                if ((val & 0x0800) != 0) result.Append("suid ");
                if ((val & 0x0400) != 0) result.Append("sgid ");
                if ((val & 0x0200) != 0) result.Append("sticky ");
            }
            string Perm(int v) => ((v & 4) != 0 ? "r" : "-") + ((v & 2) != 0 ? "w" : "-") + ((v & 1) != 0 ? "x" : "-");
            result.Append(Perm((val >> 6) & 7));
            result.Append(Perm((val >> 3) & 7));
            result.Append(Perm(val & 7));
            return result.ToString().Trim();
        }
        else if (Regex.IsMatch(s, "^([r-][w-][x-]){3}$"))
        {
            int val = 0;
            for (int i = 0; i < 9; i++)
            {
                if (s[i] != '-') val |= (1 << (8 - i));
            }
            return Convert.ToString(val, 8).PadLeft(3, '0');
        }
        throw new ArgumentException(Strings.Get("Error_ChmodUsage"));
    }

    public static string HttpStatus(string input)
    {
        if (!int.TryParse(input.Trim(), out int c)) throw new ArgumentException(Strings.Get("Error_HttpStatusUsage"));
        var name = Enum.GetName(typeof(System.Net.HttpStatusCode), c);
        if (name == null) return Strings.Get("HttpStatus_Unknown");
        var formatted = Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
        return $"{c} {formatted}";
    }

    public static string PortLookup(string input)
    {
        var ports = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            {"21", "FTP"}, {"22", "SSH"}, {"23", "Telnet"}, {"25", "SMTP"}, {"53", "DNS"},
            {"80", "HTTP"}, {"110", "POP3"}, {"143", "IMAP"}, {"443", "HTTPS"},
            {"1433", "SQL Server"}, {"1521", "Oracle"}, {"3306", "MySQL"}, {"5432", "PostgreSQL"},
            {"6379", "Redis"}, {"27017", "MongoDB"}
        };
        var s = input.Trim();
        if (ports.TryGetValue(s, out var service)) return $"{s} -> {service}";
        var rev = ports.FirstOrDefault(p => p.Value.Equals(s, StringComparison.OrdinalIgnoreCase));
        if (rev.Key != null) return $"{s} -> Port {rev.Key}";
        return "Unknown port or service";
    }

    public static string MimeTypeLookup(string input)
    {
        var s = input.Trim().ToLowerInvariant().TrimStart('.');
        var map = new Dictionary<string, string> {
            {"html", "text/html"}, {"css", "text/css"}, {"js", "application/javascript"},
            {"json", "application/json"}, {"xml", "application/xml"}, {"txt", "text/plain"},
            {"png", "image/png"}, {"jpg", "image/jpeg"}, {"jpeg", "image/jpeg"},
            {"svg", "image/svg+xml"}, {"pdf", "application/pdf"}, {"zip", "application/zip"}
        };
        return map.TryGetValue(s, out var mime) ? $"{s} -> {mime}" : "Unknown MIME type";
    }

    public static string DnsLookup(string input)
    {
        var domain = input.Trim().Replace("https://", "").Replace("http://", "").Split('/')[0];
        try
        {
            var entry = System.Net.Dns.GetHostEntry(domain);
            var sb = new StringBuilder();
            sb.AppendLine($"Host: {entry.HostName}");
            foreach (var ip in entry.AddressList) sb.AppendLine($"IP:   {ip}");
            return sb.ToString().Trim();
        }
        catch { return $"DNS lookup failed for {domain}"; }
    }

    public static string CurlToCode(string input)
    {
        var s = input.Trim();
        if (!s.StartsWith("curl ")) throw new ArgumentException("Input must be a curl command");
        var match = Regex.Match(s, @"curl\s+(?:-X\s+(?<method>\w+)\s+)?['""]?(?<url>https?://[^'""\s]+)['""]?");
        if (!match.Success) return "Could not parse curl command.";
        var url = match.Groups["url"].Value;
        var method = match.Groups["method"].Success ? match.Groups["method"].Value : "GET";
        return $"var client = new HttpClient();\nvar request = new HttpRequestMessage(HttpMethod.{method.ToUpperInvariant()}, \"{url}\");\nvar response = await client.SendAsync(request);";
    }

    public static string EnvVars(string input)
    {
        var sb = new StringBuilder();
        var envs = Environment.GetEnvironmentVariables();
        var keys = envs.Keys.Cast<string>().OrderBy(k => k).ToList();
        foreach (var key in keys)
        {
            var val = envs[key]?.ToString() ?? "";
            if (key.Equals("PATH", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine($"{key}:\n  {val.Replace(";", "\n  ")}\n");
            else
                sb.AppendLine($"{key}: {val}");
        }
        return sb.ToString().Trim();
    }
}
